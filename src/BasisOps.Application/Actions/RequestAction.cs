using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;
using BasisOps.Domain.Systems;

namespace BasisOps.Application.Actions;

public sealed record RequestActionCommand(
    ActionType Type,
    string Sid,
    string Target,
    string RequestedBy,
    string? Reason,
    string IdempotencyKey);

/// <param name="Created">False when the idempotency key was already used for the same request.</param>
public sealed record RequestActionResult(ActionRequestDto Action, bool Created);

public sealed class RequestActionHandler(
    ISystemRegistry systems,
    IActionRequestRepository actions,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<RequestActionCommand, RequestActionResult>
{
    public const int MaxIdempotencyKeyLength = 128;
    public const int MaxUserLength = 100;
    public const int MaxReasonLength = 500;

    public async Task<Result<RequestActionResult>> HandleAsync(RequestActionCommand command, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors()
            .Required("idempotencyKey", command.IdempotencyKey, MaxIdempotencyKeyLength)
            .Required("requestedBy", command.RequestedBy, MaxUserLength);

        if (!Enum.IsDefined(command.Type))
        {
            errors.Add("type", "Unknown action type.");
        }
        else if (ActionRequest.ValidateTarget(command.Type, command.Target) is { } targetError)
        {
            errors.Add("target", targetError);
        }

        if (!SapSystem.IsValidSid(command.Sid))
        {
            errors.Add("sid", "SID must be three uppercase letters or digits.");
        }

        if (command.Reason is { Length: > MaxReasonLength })
        {
            errors.Add("reason", $"reason must be at most {MaxReasonLength} characters.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        // Same key again: return the original request if the content matches, otherwise refuse.
        if (await actions.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken) is { } existing)
        {
            return existing.HasSameContentAs(command.Type, command.Sid, command.Target, command.RequestedBy)
                ? new RequestActionResult(ActionRequestDto.From(existing), Created: false)
                : Error.Conflict("idempotency_key_reused", "The Idempotency-Key was already used for a different request.");
        }

        if (await systems.FindAsync(command.Sid, cancellationToken) is not { } system)
        {
            return Error.NotFound("system_not_found", $"System {command.Sid} is not managed.");
        }

        var (request, audit) = ActionRequest.Create(
            command.Type, system, command.Target, command.RequestedBy, command.Reason, command.IdempotencyKey, timeProvider.GetUtcNow());

        actions.Add(request);
        auditLog.Add(audit);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RequestActionResult(ActionRequestDto.From(request), Created: true);
    }
}
