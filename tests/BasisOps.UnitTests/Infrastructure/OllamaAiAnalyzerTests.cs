using System.Net;
using System.Text;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;
using BasisOps.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OllamaSharp;

namespace BasisOps.UnitTests.Infrastructure;

public class OllamaAiAnalyzerTests
{
    private readonly FakeTimeProvider _time = new(TestData.Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ShortDumpDetail Detail => new(
        new ShortDump("PRD-1", "TIME_OUT", "Z_SALES_REPORT", "MEHMET", "prdapp01", TestData.Now, "Time limit exceeded."),
        "User MEHMET, application server prdapp01 (10.10.3.11).",
        "A SELECT on VBAP without a selective WHERE clause was running.");

    [Fact]
    public async Task AnalyzeDumpAsync_ValidJson_ReturnsCleanedAnalysis()
    {
        var chat = new FakeChatClient("""
            {"probableCause":" Unselective SELECT on VBAP. ","impact":"Sales report fails.",
             "firstSteps":["Check the SQL trace","", "Add an index"],
             "relatedTransactions":["st05","ST22","rm -rf /","ST05"]}
            """);

        var analysis = await Analyzer(chat).AnalyzeDumpAsync(Detail, Ct);

        Assert.Equal("Unselective SELECT on VBAP.", analysis.ProbableCause);
        Assert.Equal(["Check the SQL trace", "Add an index"], analysis.FirstSteps);
        Assert.Equal(["ST05", "ST22"], analysis.RelatedTransactions);
    }

    [Fact]
    public async Task AnalyzeDumpAsync_SendsMaskedDataAndAsksForJsonSchema()
    {
        var chat = new FakeChatClient("""{"probableCause":"x","impact":"y","firstSteps":[],"relatedTransactions":[]}""");

        await Analyzer(chat).AnalyzeDumpAsync(Detail, Ct);

        var prompt = string.Join('\n', chat.LastMessages.Select(m => m.Text));
        Assert.DoesNotContain("MEHMET", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("10.10.3.11", prompt, StringComparison.Ordinal);
        Assert.Contains("<sap_data>", prompt, StringComparison.Ordinal);
        Assert.IsType<ChatResponseFormatJson>(chat.LastOptions?.ResponseFormat);
        Assert.Equal("test-model", chat.LastOptions?.ModelId);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"probableCause":"","impact":"y"}""")]
    public async Task AnalyzeDumpAsync_UnusableAnswer_Throws(string answer)
    {
        var analyzer = Analyzer(new FakeChatClient(answer));

        await Assert.ThrowsAnyAsync<Exception>(() => analyzer.AnalyzeDumpAsync(Detail, Ct));
    }

    [Fact]
    public async Task SummarizeDailyAsync_AnswerWithThinkBlock_ReturnsOnlyTheSummary()
    {
        var chat = new FakeChatClient("<think>internal reasoning</think>\nPRD is critical.\n\n1. Restart job Z_BILLING_DAILY.");

        var summary = await Analyzer(chat).SummarizeDailyAsync("PRD", [Finding()], Ct);

        Assert.Equal("PRD is critical.\n1. Restart job Z_BILLING_DAILY.", summary);
        Assert.Contains("in English", chat.LastMessages[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeDailyAsync_EmptyAnswer_Throws()
    {
        var analyzer = Analyzer(new FakeChatClient("<think>only thoughts</think>  "));

        await Assert.ThrowsAsync<InvalidOperationException>(() => analyzer.SummarizeDailyAsync("PRD", [Finding()], Ct));
    }

    [Fact]
    public async Task SummarizeDailyAsync_ModelSlowerThanTimeout_ThrowsTimeout()
    {
        var analyzer = Analyzer(new FakeChatClient("late") { Delay = Timeout.InfiniteTimeSpan }, timeout: TimeSpan.FromMilliseconds(50), time: TimeProvider.System);

        await Assert.ThrowsAsync<TimeoutException>(() => analyzer.SummarizeDailyAsync("PRD", [Finding()], Ct));
    }

    [Fact]
    public async Task AnalyzeDumpAsync_OverOllamaClient_SendsThinkFalseAndJsonSchemaFormat()
    {
        var handler = new RecordingHandler("""
            {"model":"test-model","created_at":"2026-11-16T08:00:00Z",
             "message":{"role":"assistant","content":"{\"probableCause\":\"Long SELECT\",\"impact\":\"Slow\",\"firstSteps\":[\"ST05\"],\"relatedTransactions\":[\"ST05\"]}"},
             "done":true,"done_reason":"stop"}
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://ollama.test") };
        using var ollama = new OllamaApiClient(http, "test-model");

        var analysis = await Analyzer(ollama).AnalyzeDumpAsync(Detail, Ct);

        Assert.Equal("Long SELECT", analysis.ProbableCause);
        Assert.Contains("/api/chat", handler.RequestUri, StringComparison.Ordinal);
        Assert.Contains("\"think\":false", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"format\":{", handler.Body, StringComparison.Ordinal);
        Assert.Contains("probableCause", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("MEHMET", handler.Body, StringComparison.Ordinal);
    }

    private OllamaAiAnalyzer Analyzer(IChatClient chat, TimeSpan? timeout = null, TimeProvider? time = null) =>
        new(chat, Options.Create(new AiOptions { Model = "test-model", Timeout = timeout ?? TimeSpan.FromSeconds(30) }), time ?? _time);

    private static Finding Finding() =>
        BasisOps.Domain.Findings.Finding.Open("PRD", new FindingCandidate(FindingCategory.Job, Severity.Critical, "JOB:PRD:Z_BILLING_DAILY:1",
            "Job Z_BILLING_DAILY canceled", "Run 1 of Z_BILLING_DAILY (owner BATCH_ADM) was canceled."), TestData.Now);

    private sealed class FakeChatClient(string answer) : IChatClient
    {
        public TimeSpan Delay { get; init; } = TimeSpan.Zero;

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public ChatOptions? LastOptions { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastMessages = [.. messages];
            LastOptions = options;
            if (Delay != TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        public string RequestUri { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody.ReplaceLineEndings(" "), Encoding.UTF8, "application/x-ndjson"),
            };
        }
    }
}
