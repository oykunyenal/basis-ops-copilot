using BasisOps.Application.Actions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;
using BasisOps.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BasisOps.UnitTests.Application;

public class ActionExecutorTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeSapGateway _sap = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly ActionExecutorOptions _options = new()
    {
        MaxAttempts = 3,
        RetryBaseDelay = TimeSpan.FromSeconds(10),
        MaxRetryDelay = TimeSpan.FromMinutes(1),
        CircuitOpenDelay = TimeSpan.FromSeconds(15),
        ExecutionLease = TimeSpan.FromMinutes(2),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExecuteNext_NothingApproved_ReturnsFalse()
    {
        SeedRequested();

        var processed = await Executor().ExecuteNextAsync(Ct);

        Assert.False(processed);
        Assert.Empty(_sap.ActionCalls);
    }

    [Fact]
    public async Task ExecuteNext_ApprovedAction_SucceedsAndWritesAudit()
    {
        var action = SeedApproved();

        var processed = await Executor().ExecuteNextAsync(Ct);

        Assert.True(processed);
        Assert.Equal(ActionStatus.Succeeded, action.Status);
        Assert.Equal(1, action.Attempts);
        Assert.Equal(["unlock PRD MEHMET"], _sap.ActionCalls);
        Assert.Equal(["ActionRequested", "ActionApproved", "ActionExecutionStarted", "ActionSucceeded"], _store.Audit.Select(a => a.Action));
    }

    [Fact]
    public async Task ExecuteNext_RestartJob_SplitsJobNameAndRunId()
    {
        SeedApproved(ActionType.RestartJob, "Z_BILLING_DAILY/12345678");

        await Executor().ExecuteNextAsync(Ct);

        Assert.Equal(["restart PRD Z_BILLING_DAILY 12345678"], _sap.ActionCalls);
    }

    [Fact]
    public async Task ExecuteNext_SapRejects_FailsWithoutRetry()
    {
        var action = SeedApproved();
        _sap.ActionResult = () => new SapActionOutcome(SapActionStatus.Rejected, "User does not exist");

        await Executor().ExecuteNextAsync(Ct);

        Assert.Equal(ActionStatus.Failed, action.Status);
        Assert.Equal("User does not exist", action.LastError);
    }

    [Fact]
    public async Task ExecuteNext_SapUnavailable_SchedulesRetryWithBackoff()
    {
        var action = SeedApproved();
        _sap.ActionResult = () => throw new SapUnavailableException("SAP answered 503");

        await Executor().ExecuteNextAsync(Ct);

        Assert.Equal(ActionStatus.Approved, action.Status);
        Assert.Equal(1, action.Attempts);
        // First retry: base delay ±20% jitter.
        Assert.InRange(action.NextAttemptAt!.Value - TestData.Now, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task ExecuteNext_RetryNotDueYet_DoesNotCallSap()
    {
        SeedApproved();
        _sap.ActionResult = () => throw new SapUnavailableException("SAP answered 503");
        await Executor().ExecuteNextAsync(Ct);

        var processed = await Executor().ExecuteNextAsync(Ct);

        Assert.False(processed);
        Assert.Single(_sap.ActionCalls);
    }

    [Fact]
    public async Task ExecuteNext_UnavailableUntilMaxAttempts_FailsForGood()
    {
        var action = SeedApproved();
        _sap.ActionResult = () => throw new SapUnavailableException("SAP answered 503");

        for (var i = 0; i < _options.MaxAttempts; i++)
        {
            Assert.True(await Executor().ExecuteNextAsync(Ct));
            _time.Advance(_options.MaxRetryDelay * 2);
        }

        Assert.Equal(ActionStatus.Failed, action.Status);
        Assert.Equal(_options.MaxAttempts, action.Attempts);
        Assert.False(await Executor().ExecuteNextAsync(Ct));
    }

    [Fact]
    public async Task ExecuteNext_TransientThenSuccess_Succeeds()
    {
        var action = SeedApproved();
        var calls = 0;
        _sap.ActionResult = () => ++calls == 1
            ? throw new SapUnavailableException("SAP answered 429")
            : new SapActionOutcome(SapActionStatus.Succeeded, "User MEHMET unlocked");

        await Executor().ExecuteNextAsync(Ct);
        _time.Advance(TimeSpan.FromMinutes(1));
        await Executor().ExecuteNextAsync(Ct);

        Assert.Equal(ActionStatus.Succeeded, action.Status);
        Assert.Equal(2, action.Attempts);
        Assert.Null(action.LastError);
    }

    [Fact]
    public async Task ExecuteNext_CircuitOpen_DefersWithoutCountingAttempt()
    {
        var action = SeedApproved();
        _sap.ActionResult = () => throw new SapUnavailableException("circuit open", isCircuitOpen: true);

        await Executor().ExecuteNextAsync(Ct);

        Assert.Equal(ActionStatus.Approved, action.Status);
        Assert.Equal(0, action.Attempts);
        Assert.Equal(TestData.Now + _options.CircuitOpenDelay, action.NextAttemptAt);
        Assert.Equal("ActionDeferred", _store.Audit[^1].Action);
    }

    [Fact]
    public async Task ExecuteNext_UnexpectedError_IsRetriedLikeTransient()
    {
        var action = SeedApproved();
        _sap.ActionResult = () => throw new InvalidOperationException("Unexpected response");

        await Executor().ExecuteNextAsync(Ct);

        Assert.Equal(ActionStatus.Approved, action.Status);
        Assert.Equal("Unexpected response", action.LastError);
    }

    [Fact]
    public async Task ExecuteNext_ActionLeftExecutingPastLease_IsRecoveredAndExecuted()
    {
        var action = SeedApproved();
        action.StartExecution(TestData.Now); // executor died here
        _time.Advance(_options.ExecutionLease + TimeSpan.FromSeconds(1));

        var processed = await Executor().ExecuteNextAsync(Ct);

        Assert.True(processed);
        Assert.Equal(ActionStatus.Succeeded, action.Status);
        Assert.Equal(2, action.Attempts);
        Assert.Contains(_store.Audit, a => a.Action == "ActionRetryScheduled");
    }

    [Fact]
    public async Task ExecuteNext_ActionExecutingWithinLease_IsLeftAlone()
    {
        var action = SeedApproved();
        action.StartExecution(TestData.Now);
        _time.Advance(TimeSpan.FromSeconds(30));

        var processed = await Executor().ExecuteNextAsync(Ct);

        Assert.False(processed);
        Assert.Equal(ActionStatus.Executing, action.Status);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(10, 60)]
    public void RetryDelayAfter_Attempt_GrowsExponentiallyUpToCap(int attempt, int expectedSeconds)
    {
        var delay = _options.RetryDelayAfter(attempt);

        Assert.InRange(delay.TotalSeconds, expectedSeconds * 0.8, expectedSeconds * 1.2);
    }

    private ActionExecutor Executor() => new(
        _store, _store, _store, _sap, Options.Create(_options), _time, NullLogger<ActionExecutor>.Instance);

    private ActionRequest SeedRequested(ActionType type = ActionType.UnlockUser, string target = "MEHMET")
    {
        var (request, audit) = ActionRequest.Create(type, TestData.Prd, target, "AYSE", null, Guid.NewGuid().ToString(), TestData.Now);
        _store.Actions.Add(request);
        _store.Audit.Add(audit);
        return request;
    }

    private ActionRequest SeedApproved(ActionType type = ActionType.UnlockUser, string target = "MEHMET")
    {
        var request = SeedRequested(type, target);
        _store.Audit.Add(request.Approve("KTANAKA", null, TestData.Now));
        return request;
    }
}
