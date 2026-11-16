using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;
using BasisOps.Domain.Rules;

namespace BasisOps.UnitTests.Rules;

public class CanceledJobRuleTests
{
    private readonly CanceledJobRule _rule = new();

    [Theory]
    [InlineData("Z_BILLING_DAILY", Severity.Critical)]
    [InlineData("SAP_COLLECTOR_FOR_PERFMONITOR", Severity.Critical)]
    [InlineData("Z_MM_STOCK_SYNC", Severity.Warning)]
    public void Evaluate_CanceledJob_SeverityDependsOnJobName(string jobName, Severity expected)
    {
        var snapshot = TestData.Snapshot(jobs: [TestData.Job(jobName, JobRunStatus.Canceled, TimeSpan.FromHours(1))]);

        var finding = Assert.Single(_rule.Evaluate(snapshot));

        Assert.Equal(expected, finding.Severity);
        Assert.Equal($"JOB:PRD:{jobName}:12345678", finding.Key);
    }

    [Fact]
    public void Evaluate_CanceledJobWithLaterSuccessfulRun_ReportsNothing()
    {
        var snapshot = TestData.Snapshot(jobs:
        [
            TestData.Job("Z_BILLING_DAILY", JobRunStatus.Canceled, TimeSpan.FromHours(3), "11111111"),
            TestData.Job("Z_BILLING_DAILY", JobRunStatus.Finished, TimeSpan.FromHours(1), "22222222"),
        ]);

        Assert.Empty(_rule.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_CanceledJobOlderThanLookback_ReportsNothing()
    {
        var snapshot = TestData.Snapshot(jobs: [TestData.Job("Z_BILLING_DAILY", JobRunStatus.Canceled, TimeSpan.FromHours(30))]);

        Assert.Empty(_rule.Evaluate(snapshot));
    }
}

public class DumpFrequencyRuleTests
{
    private readonly DumpFrequencyRule _rule = new();

    [Theory]
    [InlineData(4, null)]
    [InlineData(5, Severity.Warning)]
    [InlineData(19, Severity.Warning)]
    [InlineData(20, Severity.Critical)]
    public void Evaluate_DumpsOfSameTypeInLastHour_SeverityFollowsThresholds(int count, Severity? expected)
    {
        var dumps = Enumerable.Range(0, count).Select(i => TestData.Dump("TIME_OUT", TimeSpan.FromMinutes(i + 1))).ToList();

        var findings = _rule.Evaluate(TestData.Snapshot(dumps: dumps)).ToList();

        Assert.Equal(expected, findings.SingleOrDefault()?.Severity);
    }

    [Fact]
    public void Evaluate_DumpsOutsideWindow_AreNotCounted()
    {
        var dumps = Enumerable.Range(0, 10).Select(_ => TestData.Dump("TIME_OUT", TimeSpan.FromHours(2))).ToList();

        Assert.Empty(_rule.Evaluate(TestData.Snapshot(dumps: dumps)));
    }

    [Fact]
    public void Evaluate_DifferentRuntimeErrors_AreCountedSeparately()
    {
        var dumps = Enumerable.Range(0, 3).Select(_ => TestData.Dump("TIME_OUT", TimeSpan.FromMinutes(5)))
            .Concat(Enumerable.Range(0, 3).Select(_ => TestData.Dump("MESSAGE_TYPE_X", TimeSpan.FromMinutes(5))))
            .ToList();

        Assert.Empty(_rule.Evaluate(TestData.Snapshot(dumps: dumps)));
    }
}

public class CertificateExpiryRuleTests
{
    private readonly CertificateExpiryRule _rule = new();

    [Theory]
    [InlineData(45, null)]
    [InlineData(29, Severity.Warning)]
    [InlineData(6, Severity.Critical)]
    [InlineData(-1, Severity.Critical)]
    public void Evaluate_DaysUntilExpiry_SeverityFollowsThresholds(int days, Severity? expected)
    {
        var certificate = new Certificate("SSL server Standard", "CN=prdapp01", TestData.Now.AddDays(days));

        var finding = _rule.Evaluate(TestData.Snapshot(certificates: [certificate])).SingleOrDefault();

        Assert.Equal(expected, finding?.Severity);
    }
}

public class TransportImportRuleTests
{
    private readonly TransportImportRule _rule = new();

    [Theory]
    [InlineData(0, null)]
    [InlineData(4, Severity.Info)]
    [InlineData(8, Severity.Critical)]
    [InlineData(12, Severity.Critical)]
    public void Evaluate_ImportedTransport_SeverityFollowsReturnCode(int returnCode, Severity? expected)
    {
        var transport = new TransportRequest("DEVK900123", "AYSE", "Fix", TransportImportStatus.Imported, returnCode);

        var finding = _rule.Evaluate(TestData.Snapshot(transports: [transport])).SingleOrDefault();

        Assert.Equal(expected, finding?.Severity);
    }

    [Fact]
    public void Evaluate_WaitingTransport_ReportsNothing()
    {
        var transport = new TransportRequest("DEVK900123", "AYSE", "Fix", TransportImportStatus.Waiting, null);

        Assert.Empty(_rule.Evaluate(TestData.Snapshot(transports: [transport])));
    }
}

public class WorkProcessUtilizationRuleTests
{
    private readonly WorkProcessUtilizationRule _rule = new();

    [Theory]
    [InlineData(16, null)]
    [InlineData(17, Severity.Warning)]
    [InlineData(19, Severity.Warning)]
    [InlineData(20, Severity.Critical)]
    public void Evaluate_DialogUtilization_SeverityFollowsThresholds(int busy, Severity? expected)
    {
        var usage = new WorkProcessUsage(WorkProcessType.Dialog, 20, busy);

        var finding = _rule.Evaluate(TestData.Snapshot(workProcesses: [usage])).SingleOrDefault();

        Assert.Equal(expected, finding?.Severity);
    }

    [Fact]
    public void Evaluate_FullyBusyBackgroundProcesses_ReportsNothing()
    {
        var usage = new WorkProcessUsage(WorkProcessType.Background, 10, 10);

        Assert.Empty(_rule.Evaluate(TestData.Snapshot(workProcesses: [usage])));
    }
}

public class LockedUserRuleTests
{
    [Fact]
    public void Evaluate_LockedUser_ReportsInfoFinding()
    {
        var user = new LockedUser("MEHMET", LockReason.FailedLogons, TestData.Now.AddMinutes(-5));

        var finding = Assert.Single(new LockedUserRule().Evaluate(TestData.Snapshot(users: [user])));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("USER:PRD:MEHMET", finding.Key);
    }
}
