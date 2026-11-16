using BasisOps.Application.Abstractions;
using BasisOps.Application.Actions;
using BasisOps.Domain.Actions;
using BasisOps.UnitTests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace BasisOps.UnitTests.Application;

public class RequestActionHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly RequestActionHandler _handler;

    public RequestActionHandlerTests()
    {
        _handler = new RequestActionHandler(_store, _store, _store, _store, new FakeTimeProvider(TestData.Now));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandleAsync_ValidRequest_CreatesActionAndAuditEntry()
    {
        var result = await _handler.HandleAsync(Command(), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        Assert.Single(_store.Actions);
        Assert.Single(_store.Audit);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_SameIdempotencyKeyAndContent_ReturnsExistingAction()
    {
        var first = await _handler.HandleAsync(Command(), Ct);

        var second = await _handler.HandleAsync(Command() with { Reason = "reworded reason" }, Ct);

        Assert.False(second.Value.Created);
        Assert.Equal(first.Value.Action.Id, second.Value.Action.Id);
        Assert.Single(_store.Actions);
    }

    [Fact]
    public async Task HandleAsync_SameIdempotencyKeyDifferentTarget_ReturnsConflict()
    {
        await _handler.HandleAsync(Command(), Ct);

        var result = await _handler.HandleAsync(Command() with { Target = "JDOE" }, Ct);

        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal("idempotency_key_reused", result.Error?.Code);
    }

    [Fact]
    public async Task HandleAsync_InvalidInput_ReturnsValidationErrorsPerField()
    {
        var result = await _handler.HandleAsync(new RequestActionCommand(ActionType.RestartJob, "prd", "Z_BILLING", "", null, ""), Ct);

        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
        Assert.Equal(["idempotencyKey", "requestedBy", "sid", "target"], result.Error!.ValidationErrors!.Keys.Order());
        Assert.Empty(_store.Actions);
    }

    [Fact]
    public async Task HandleAsync_UnknownSystem_ReturnsNotFound()
    {
        var result = await _handler.HandleAsync(Command() with { Sid = "XYZ" }, Ct);

        Assert.Equal(ErrorKind.NotFound, result.Error?.Kind);
    }

    private static RequestActionCommand Command() =>
        new(ActionType.UnlockUser, "PRD", "MEHMET", "AYSE", "Locked after vacation", "chat-42:ayse");
}
