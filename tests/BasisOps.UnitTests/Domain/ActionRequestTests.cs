using BasisOps.Domain.Actions;
using BasisOps.Domain.Common;

namespace BasisOps.UnitTests.Domain;

public class ActionRequestTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void Create_ValidUnlock_IsRequestedWithNormalizedTargetAndAudit()
    {
        var (request, audit) = ActionRequest.Create(ActionType.UnlockUser, TestData.Prd, " mehmet ", "ayse", "Forgot password", "key-1", Now);

        Assert.Equal(ActionStatus.Requested, request.Status);
        Assert.Equal("MEHMET", request.Target);
        Assert.Equal("ActionRequested", audit.Action);
        Assert.Equal("PRD:MEHMET", audit.Target);
        Assert.Null(audit.FromState);
    }

    [Fact]
    public void Approve_RequesterApprovesOwnRequestOnProduction_ThrowsFourEyesViolation()
    {
        var request = NewRequest(requestedBy: "AYSE");

        Assert.Throws<FourEyesViolationException>(() => request.Approve("ayse", null, Now));
        Assert.Equal(ActionStatus.Requested, request.Status);
    }

    [Fact]
    public void Approve_RequesterApprovesOwnRequestOnQuality_IsAllowed()
    {
        var (request, _) = ActionRequest.Create(ActionType.UnlockUser, TestData.Qas, "MEHMET", "AYSE", null, "key-1", Now);

        request.Approve("AYSE", null, Now);

        Assert.Equal(ActionStatus.Approved, request.Status);
    }

    [Fact]
    public void Approve_DifferentApproverOnProduction_MovesToApprovedAndAuditsTransition()
    {
        var request = NewRequest();

        var audit = request.Approve("KTANAKA", "ok", Now);

        Assert.Equal(ActionStatus.Approved, request.Status);
        Assert.Equal("KTANAKA", request.DecidedBy);
        Assert.Equal(("Requested", "Approved"), (audit.FromState, audit.ToState));
    }

    [Fact]
    public void Reject_RequestedAction_MovesToRejected()
    {
        var request = NewRequest();

        request.Reject("KTANAKA", "Not needed", Now);

        Assert.Equal(ActionStatus.Rejected, request.Status);
        Assert.True(request.IsTerminal);
    }

    [Fact]
    public void Approve_RejectedAction_ThrowsInvalidStateTransition()
    {
        var request = NewRequest();
        request.Reject("KTANAKA", "Not needed", Now);

        Assert.Throws<InvalidStateTransitionException>(() => request.Approve("KTANAKA", null, Now));
    }

    [Fact]
    public void StartExecution_RequestedAction_ThrowsInvalidStateTransition()
    {
        var request = NewRequest();

        Assert.Throws<InvalidStateTransitionException>(() => request.StartExecution(Now));
    }

    [Fact]
    public void Succeed_AfterExecution_IsTerminalWithOneAttempt()
    {
        var request = ApprovedRequest();
        request.StartExecution(Now);

        request.Succeed("User MEHMET unlocked", Now);

        Assert.Equal(ActionStatus.Succeeded, request.Status);
        Assert.Equal(1, request.Attempts);
        Assert.True(request.IsTerminal);
    }

    [Fact]
    public void RecordFailure_TransientBelowMaxAttempts_SchedulesRetry()
    {
        var request = ApprovedRequest();
        request.StartExecution(Now);

        var audit = request.RecordFailure("503 Service Unavailable", transient: true, maxAttempts: 5, TimeSpan.FromSeconds(30), Now);

        Assert.Equal(ActionStatus.Approved, request.Status);
        Assert.Equal(Now.AddSeconds(30), request.NextAttemptAt);
        Assert.Equal("ActionRetryScheduled", audit.Action);
    }

    [Fact]
    public void RecordFailure_TransientAtMaxAttempts_Fails()
    {
        var request = ApprovedRequest();
        for (var attempt = 1; attempt < 3; attempt++)
        {
            request.StartExecution(Now.AddMinutes(attempt));
            request.RecordFailure("503", transient: true, maxAttempts: 3, TimeSpan.Zero, Now.AddMinutes(attempt));
        }

        request.StartExecution(Now.AddMinutes(3));
        request.RecordFailure("503", transient: true, maxAttempts: 3, TimeSpan.Zero, Now.AddMinutes(3));

        Assert.Equal(ActionStatus.Failed, request.Status);
        Assert.Equal(3, request.Attempts);
    }

    [Fact]
    public void RecordFailure_Permanent_FailsImmediately()
    {
        var request = ApprovedRequest();
        request.StartExecution(Now);

        request.RecordFailure("User does not exist", transient: false, maxAttempts: 5, TimeSpan.Zero, Now);

        Assert.Equal(ActionStatus.Failed, request.Status);
    }

    [Fact]
    public void StartExecution_BeforeRetryIsDue_Throws()
    {
        var request = ApprovedRequest();
        request.StartExecution(Now);
        request.RecordFailure("503", transient: true, maxAttempts: 5, TimeSpan.FromMinutes(1), Now);

        Assert.Throws<InvalidOperationException>(() => request.StartExecution(Now.AddSeconds(10)));
    }

    [Theory]
    [InlineData(ActionType.UnlockUser, "MEHMET", true)]
    [InlineData(ActionType.UnlockUser, "A_VERY_LONG_USER", false)]
    [InlineData(ActionType.RestartJob, "Z_BILLING_DAILY/12345678", true)]
    [InlineData(ActionType.RestartJob, "Z_BILLING_DAILY", false)]
    [InlineData(ActionType.RestartJob, "Z_BILLING_DAILY/1234", false)]
    [InlineData(ActionType.ImportTransport, "DEVK900123", true)]
    [InlineData(ActionType.ImportTransport, "DEV900123", false)]
    [InlineData(ActionType.ImportTransport, "", false)]
    public void ValidateTarget_VariousTargets_MatchesExpectedValidity(ActionType type, string target, bool valid)
    {
        Assert.Equal(valid, ActionRequest.ValidateTarget(type, target) is null);
    }

    [Fact]
    public void HasSameContentAs_DifferentCaseAndWhitespace_IsSameRequest()
    {
        var request = NewRequest(requestedBy: "AYSE");

        Assert.True(request.HasSameContentAs(ActionType.UnlockUser, "PRD", " mehmet", "ayse "));
        Assert.False(request.HasSameContentAs(ActionType.UnlockUser, "PRD", "JDOE", "AYSE"));
    }

    private static ActionRequest NewRequest(string requestedBy = "AYSE") =>
        ActionRequest.Create(ActionType.UnlockUser, TestData.Prd, "MEHMET", requestedBy, null, "key-1", Now).Request;

    private static ActionRequest ApprovedRequest()
    {
        var request = NewRequest();
        request.Approve("KTANAKA", null, Now);
        return request;
    }
}
