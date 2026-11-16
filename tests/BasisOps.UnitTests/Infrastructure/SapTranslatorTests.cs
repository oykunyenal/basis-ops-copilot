using BasisOps.Application.Ports;
using BasisOps.Domain.Observations;
using BasisOps.Infrastructure.Sap;

namespace BasisOps.UnitTests.Infrastructure;

public class SapTranslatorTests
{
    [Theory]
    [InlineData("S", SapActionStatus.Succeeded)]
    [InlineData("W", SapActionStatus.Succeeded)]
    [InlineData("I", SapActionStatus.Succeeded)]
    [InlineData("E", SapActionStatus.Rejected)]
    [InlineData("A", SapActionStatus.Rejected)]
    public void ToOutcome_BapiReturnType_MapsToStatus(string type, SapActionStatus expected)
    {
        var outcome = SapTranslator.ToOutcome(new SapBapiReturn(type, "message"));

        Assert.Equal(expected, outcome.Status);
        Assert.Equal("message", outcome.Message);
    }

    [Theory]
    [InlineData("A", JobRunStatus.Canceled)]
    [InlineData("F", JobRunStatus.Finished)]
    [InlineData("R", JobRunStatus.Running)]
    [InlineData("S", JobRunStatus.Scheduled)]
    [InlineData("P", JobRunStatus.Scheduled)]
    public void ToJobRun_SapStatus_MapsToDomainStatus(string status, JobRunStatus expected)
    {
        var run = SapTranslator.ToJobRun(new SapJob("Z_BILLING_DAILY", "12345678", status, "20261116", "071500", "BATCH_ADM"));

        Assert.Equal(expected, run.Status);
        Assert.Equal("12345678", run.RunId);
    }

    [Theory]
    [InlineData(128, LockReason.FailedLogons)]
    [InlineData(192, LockReason.FailedLogons)]
    [InlineData(64, LockReason.Administrator)]
    public void ToLockedUser_LockFlag_MapsToReason(int flag, LockReason expected)
    {
        var user = SapTranslator.ToLockedUser(new SapLockedUser("MEHMET", flag, "20261116", "063000"));

        Assert.Equal(expected, user.Reason);
    }

    [Fact]
    public void ToTimestamp_DatsAndTims_ReturnsUtc()
    {
        var value = SapTranslator.ToTimestamp("20261116", "071530");

        Assert.Equal(new DateTimeOffset(2026, 11, 16, 7, 15, 30, TimeSpan.Zero), value);
        Assert.Equal(TimeSpan.Zero, value.Offset);
    }

    [Fact]
    public void ToWorkProcessUsage_UnknownType_ReturnsNull()
    {
        Assert.Null(SapTranslator.ToWorkProcessUsage(new SapWorkProcess("ENQ", 1, 0)));
        Assert.Equal(WorkProcessType.Dialog, SapTranslator.ToWorkProcessUsage(new SapWorkProcess("DIA", 10, 9))!.Type);
    }
}
