using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;
using BasisOps.Domain.Audit;
using BasisOps.Domain.Common;

namespace BasisOps.Application.Actions;

public sealed record ApproveActionCommand(Guid ActionId, string ApprovedBy, string? Comment);

public sealed record RejectActionCommand(Guid ActionId, string RejectedBy, string Reason);

public sealed class ApproveActionHandler(
    IActionRequestRepository actions,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<ApproveActionCommand, ActionRequestDto>
{
    public async Task<Result<ActionRequestDto>> HandleAsync(ApproveActionCommand command, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors().Required("approvedBy", command.ApprovedBy, RequestActionHandler.MaxUserLength);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        return await ActionDecision.ApplyAsync(
            actions, auditLog, unitOfWork, command.ActionId,
            request => request.Approve(command.ApprovedBy, command.Comment, timeProvider.GetUtcNow()),
            cancellationToken);
    }
}

public sealed class RejectActionHandler(
    IActionRequestRepository actions,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<RejectActionCommand, ActionRequestDto>
{
    public async Task<Result<ActionRequestDto>> HandleAsync(RejectActionCommand command, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors()
            .Required("rejectedBy", command.RejectedBy, RequestActionHandler.MaxUserLength)
            .Required("reason", command.Reason, RequestActionHandler.MaxReasonLength);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        return await ActionDecision.ApplyAsync(
            actions, auditLog, unitOfWork, command.ActionId,
            request => request.Reject(command.RejectedBy, command.Reason, timeProvider.GetUtcNow()),
            cancellationToken);
    }
}

internal static class ActionDecision
{
    /// <summary>Loads the request, applies the transition and translates domain rule violations into errors.</summary>
    public static async Task<Result<ActionRequestDto>> ApplyAsync(
        IActionRequestRepository actions,
        IAuditLog auditLog,
        IUnitOfWork unitOfWork,
        Guid actionId,
        Func<ActionRequest, AuditEntry> transition,
        CancellationToken cancellationToken)
    {
        if (await actions.GetAsync(actionId, cancellationToken) is not { } request)
        {
            return Error.NotFound("action_not_found", $"Action {actionId} does not exist.");
        }

        AuditEntry audit;
        try
        {
            audit = transition(request);
        }
        catch (InvalidStateTransitionException ex)
        {
            return Error.Conflict("invalid_state_transition", ex.Message);
        }
        catch (FourEyesViolationException ex)
        {
            return Error.Forbidden("four_eyes_violation", ex.Message);
        }

        auditLog.Add(audit);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ActionRequestDto.From(request);
    }
}
