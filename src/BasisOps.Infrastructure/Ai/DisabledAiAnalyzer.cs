using BasisOps.Application.Ports;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.Infrastructure.Ai;

/// <summary>Used when no LLM is configured. Callers fall back to rule-based output.</summary>
internal sealed class DisabledAiAnalyzer : IAiAnalyzer
{
    public Task<DumpAnalysis> AnalyzeDumpAsync(ShortDumpDetail dump, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("AI analysis is not configured.");

    public Task<string> SummarizeDailyAsync(string sid, IReadOnlyList<Finding> openFindings, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("AI analysis is not configured.");
}
