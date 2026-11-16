using BasisOps.Application.Findings;
using BasisOps.Application.Reports;
using BasisOps.Domain.Findings;
using BasisOps.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BasisOps.UnitTests.Application;

public class GetDailyReportHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeAiAnalyzer _ai = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandleAsync_AiAvailable_ReturnsSummaryAndSortedFindings()
    {
        SeedFindings();

        var report = (await Handler().HandleAsync(new GetDailyReportQuery("PRD"), Ct)).Value;

        Assert.True(report.AiAvailable);
        Assert.Equal(_ai.Summary, report.AiSummary);
        Assert.Equal(HealthStatus.Critical, report.Status);
        Assert.Equal(Severity.Critical, report.Findings[0].Severity);
    }

    [Fact]
    public async Task HandleAsync_AiThrows_ReturnsRuleBasedReportWithoutSummary()
    {
        SeedFindings();
        _ai.Failure = new HttpRequestException("Connection refused (localhost:11434)");

        var result = await Handler().HandleAsync(new GetDailyReportQuery("PRD"), Ct);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AiAvailable);
        Assert.Null(result.Value.AiSummary);
        Assert.Equal(2, result.Value.Findings.Count);
    }

    [Fact]
    public async Task HandleAsync_AiTimesOut_ReturnsRuleBasedReport()
    {
        _ai.Failure = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

        var result = await Handler().HandleAsync(new GetDailyReportQuery("PRD"), Ct);

        Assert.False(result.Value.AiAvailable);
    }

    private GetDailyReportHandler Handler() =>
        new(_store, _store, _ai, new FakeTimeProvider(TestData.Now), NullLogger<GetDailyReportHandler>.Instance);

    private void SeedFindings()
    {
        _store.Findings.Add(Finding.Open("PRD", new FindingCandidate(FindingCategory.User, Severity.Info, "USER:PRD:MEHMET", "locked", "d"), TestData.Now));
        _store.Findings.Add(Finding.Open("PRD", new FindingCandidate(FindingCategory.Job, Severity.Critical, "JOB:PRD:Z_BILLING_DAILY:1", "canceled", "d"), TestData.Now));
    }
}
