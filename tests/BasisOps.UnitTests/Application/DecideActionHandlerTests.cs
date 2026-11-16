using BasisOps.Application.Abstractions;
using BasisOps.Application.Actions;
using BasisOps.Domain.Actions;
using BasisOps.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace BasisOps.UnitTests.Application;

public class DecideActionHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Approve_RequesterOnProduction_ReturnsForbiddenAndKeepsStatus()
    {
        var action = Seed("AYSE");

        var result = await Approve().HandleAsync(new ApproveActionCommand(action.Id, "AYSE", null), Ct);

        Assert.Equal(ErrorKind.Forbidden, result.Error?.Kind);
        Assert.Equal(ActionStatus.Requested, action.Status);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Approve_OtherUser_ApprovesAndWritesAudit()
    {
        var action = Seed("AYSE");

        var result = await Approve().HandleAsync(new ApproveActionCommand(action.Id, "KTANAKA", "ok"), Ct);

        Assert.Equal(ActionStatus.Approved, result.Value.Status);
        Assert.Contains(_store.Audit, a => a.Action == "ActionApproved" && a.Actor == "KTANAKA");
    }

    [Fact]
    public async Task Approve_AlreadyRejected_ReturnsConflict()
    {
        var action = Seed("AYSE");
        await Reject().HandleAsync(new RejectActionCommand(action.Id, "KTANAKA", "not needed"), Ct);

        var result = await Approve().HandleAsync(new ApproveActionCommand(action.Id, "KTANAKA", null), Ct);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public async Task Reject_WithoutReason_ReturnsValidationError()
    {
        var action = Seed("AYSE");

        var result = await Reject().HandleAsync(new RejectActionCommand(action.Id, "KTANAKA", " "), Ct);

        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
    }

    [Fact]
    public async Task Approve_UnknownAction_ReturnsNotFound()
    {
        var result = await Approve().HandleAsync(new ApproveActionCommand(Guid.NewGuid(), "KTANAKA", null), Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
    }

    private ApproveActionHandler Approve() => new(_store, _store, _store, _time);

    private RejectActionHandler Reject() => new(_store, _store, _store, _time);

    private ActionRequest Seed(string requestedBy)
    {
        var (request, _) = ActionRequest.Create(ActionType.UnlockUser, TestData.Prd, "MEHMET", requestedBy, null, Guid.NewGuid().ToString(), TestData.Now);
        _store.Actions.Add(request);
        return request;
    }
}
