using BasisOps.Domain.Actions;

namespace BasisOps.Application.Actions;

public sealed record ActionRequestDto(
    Guid Id,
    ActionType Type,
    string Sid,
    string Target,
    ActionStatus Status,
    string RequestedBy,
    string? Reason,
    string? DecidedBy,
    string? DecisionComment,
    int Attempts,
    string? LastError,
    string? ResultMessage,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt)
{
    public static ActionRequestDto From(ActionRequest a) => new(
        a.Id, a.Type, a.Sid, a.Target, a.Status, a.RequestedBy, a.Reason, a.DecidedBy, a.DecisionComment,
        a.Attempts, a.LastError, a.ResultMessage, a.RequestedAt, a.UpdatedAt);
}
