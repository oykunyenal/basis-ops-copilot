using BasisOps.Application.Health;
using BasisOps.Domain.Observations;
using BasisOps.Domain.Rules;
using BasisOps.Domain.Scans;
using BasisOps.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BasisOps.UnitTests.Application;

public class RunHealthScanHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeSapGateway _sap = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly RunHealthScanHandler _handler;

    public RunHealthScanHandlerTests()
    {
        IHealthRule[] rules = [new CanceledJobRule(), new LockedUserRule()];
        _handler = new RunHealthScanHandler(_store, _sap, rules, _store, _store, _store, _time, NullLogger<RunHealthScanHandler>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandleAsync_ProblemsFound_OpensFindingsAndRecordsScan()
    {
        _sap.Jobs.Add(TestData.Job("Z_BILLING_DAILY", JobRunStatus.Canceled, TimeSpan.FromHours(1)));
        _sap.LockedUsers.Add(new LockedUser("MEHMET", LockReason.FailedLogons, TestData.Now));

        var result = await _handler.HandleAsync(new RunHealthScanCommand("PRD"), Ct);

        Assert.Equal(2, result.Value.Opened);
        Assert.Equal(2, _store.Findings.Count);
        Assert.Equal(HealthScanStatus.Succeeded, Assert.Single(_store.Scans).Status);
    }

    [Fact]
    public async Task HandleAsync_UserUnlockedBeforeSecondScan_ResolvesFinding()
    {
        _sap.LockedUsers.Add(new LockedUser("MEHMET", LockReason.FailedLogons, TestData.Now));
        await _handler.HandleAsync(new RunHealthScanCommand("PRD"), Ct);
        _sap.LockedUsers.Clear();
        _time.Advance(TimeSpan.FromMinutes(1));

        var result = await _handler.HandleAsync(new RunHealthScanCommand("PRD"), Ct);

        Assert.Equal(1, result.Value.Resolved);
        Assert.True(Assert.Single(_store.Findings).IsResolved);
    }

    [Fact]
    public async Task HandleAsync_SapUnavailable_RecordsFailedScanAndKeepsFindings()
    {
        _sap.LockedUsers.Add(new LockedUser("MEHMET", LockReason.FailedLogons, TestData.Now));
        await _handler.HandleAsync(new RunHealthScanCommand("PRD"), Ct);
        _sap.Unavailable = true;

        var result = await _handler.HandleAsync(new RunHealthScanCommand("PRD"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(HealthScanStatus.Failed, result.Value.Status);
        Assert.False(Assert.Single(_store.Findings).IsResolved);
    }
}
