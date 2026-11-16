using System.Text.RegularExpressions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaChatRequest = OllamaSharp.Models.Chat.ChatRequest;

namespace BasisOps.Infrastructure.Ai;

/// <summary>
/// <see cref="IAiAnalyzer"/> on a local LLM (Ollama) through Microsoft.Extensions.AI. Input is masked and
/// marked as data (<see cref="AiPrompts"/>); output is validated before it is returned. Any failure
/// (server down, timeout, unusable answer) is thrown so the caller can fall back to the rule-based result.
/// </summary>
internal sealed partial class OllamaAiAnalyzer(IChatClient chatClient, IOptions<AiOptions> options, TimeProvider timeProvider) : IAiAnalyzer
{
    private const int MaxTextLength = 1000;
    private const int MaxSummaryLength = 2000;
    private const int MaxSummaryLines = 12;

    private readonly AiOptions _options = options.Value;

    public async Task<DumpAnalysis> AnalyzeDumpAsync(ShortDumpDetail dump, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dump);

        List<ChatMessage> messages =
        [
            new(ChatRole.System, AiPrompts.DumpSystemPrompt),
            new(ChatRole.User, AiPrompts.DumpUserPrompt(dump)),
        ];

        var response = await WithTimeoutAsync(
            ct => chatClient.GetResponseAsync<DumpAnalysisAnswer>(messages, CreateChatOptions(), useJsonSchemaResponseFormat: true, ct),
            cancellationToken);

        if (!response.TryGetResult(out var answer) || string.IsNullOrWhiteSpace(answer?.ProbableCause))
        {
            throw new InvalidOperationException("The model did not return a usable dump analysis.");
        }

        return new DumpAnalysis(
            Clip(answer.ProbableCause, MaxTextLength),
            Clip(answer.Impact ?? string.Empty, MaxTextLength),
            [.. (answer.FirstSteps ?? []).OfType<string>().Where(s => !string.IsNullOrWhiteSpace(s)).Take(8).Select(s => Clip(s, 300))],
            [.. (answer.RelatedTransactions ?? [])
                .Select(t => t?.Trim().ToUpperInvariant() ?? string.Empty)
                .Where(t => TransactionCodePattern().IsMatch(t))
                .Distinct(StringComparer.Ordinal)
                .Take(8)]);
    }

    public async Task<string> SummarizeDailyAsync(string sid, IReadOnlyList<Finding> openFindings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openFindings);

        List<ChatMessage> messages =
        [
            new(ChatRole.System, AiPrompts.DailySystemPrompt(_options.SummaryLanguage)),
            new(ChatRole.User, AiPrompts.DailyUserPrompt(sid, openFindings, _options.MaxFindingsInSummary, timeProvider.GetUtcNow())),
        ];

        var response = await WithTimeoutAsync(ct => chatClient.GetResponseAsync(messages, CreateChatOptions(), ct), cancellationToken);

        // Reasoning models may still inline their thoughts; only the answer goes to the report.
        var text = ThinkBlockPattern().Replace(response.Text, string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new InvalidOperationException("The model returned an empty summary.");
        }

        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(MaxSummaryLines);
        return Clip(string.Join('\n', lines), MaxSummaryLength);
    }

    private ChatOptions CreateChatOptions() => new()
    {
        ModelId = _options.Model,
        Temperature = 0.2f,

        // qwen3 "thinks" by default; for these short, structured answers it only adds latency.
        RawRepresentationFactory = _ => new OllamaChatRequest { Think = false },
    };

    private async Task<T> WithTimeoutAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.Timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await call(linked.Token);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The model did not answer within {_options.Timeout.TotalSeconds:0} seconds.", ex);
        }
    }

    private static string Clip(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength - 3), "...");
    }

    // SE93: transaction codes are at most 20 characters, e.g. ST22, SM37, /SDF/MON.
    [GeneratedRegex(@"^/?[A-Z][A-Z0-9_/]{1,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex TransactionCodePattern();

    [GeneratedRegex(@"<think>.*?(?:</think>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThinkBlockPattern();

    /// <summary>Shape the model must answer with (JSON schema). Everything is optional here and validated above.</summary>
    internal sealed record DumpAnalysisAnswer(
        string? ProbableCause,
        string? Impact,
        IReadOnlyList<string?>? FirstSteps,
        IReadOnlyList<string?>? RelatedTransactions);
}
