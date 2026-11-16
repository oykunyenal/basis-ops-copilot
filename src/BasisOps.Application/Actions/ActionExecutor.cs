using System.Diagnostics;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;
using BasisOps.Domain.Audit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BasisOps.Application.Actions;

/// <summary>
/// Applies approved actions to SAP, one per call. Several executors can run in parallel: an action is claimed
/// under a row lock (SKIP LOCKED) and moved to Executing before SAP is called, so it is applied only once.
/// </summary>
public sealed partial class ActionExecutor(
    IActionRequestRepository actions,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    ISapGateway sap,
    IOptions<ActionExecutorOptions> options,
    TimeProvider timeProvider,
    ILogger<ActionExecutor> logger)
{
    private readonly ActionExecutorOptions _options = options.Value;

    /// <summary>Returns false when nothing was due, so the caller can wait before polling again.</summary>
    public async Task<bool> ExecuteNextAsync(CancellationToken cancellationToken)
    {
        var (request, processed) = await ClaimNextAsync(cancellationToken);
        if (request is null)
        {
            return processed;
        }

        using var activity = Telemetry.Source.StartActivity("execute-action");
        activity?.SetTag("action.id", request.Id);
        activity?.SetTag("action.type", request.Type.ToString());
        activity?.SetTag("action.attempt", request.Attempts);
        activity?.SetTag("sap.sid", request.Sid);
        using var logScope = logger.BeginScope(new Dictionary<string, object> { ["ActionId"] = request.Id, ["Sid"] = request.Sid });

        AuditEntry audit;
        try
        {
            var outcome = await SendToSapAsync(request, cancellationToken);
            var now = timeProvider.GetUtcNow();
            if (outcome.Succeeded)
            {
                audit = request.Succeed(outcome.Message, now);
                LogSucceeded(request.Id, request.Sid, request.Type, request.Target, request.Attempts);
            }
            else
            {
                audit = request.RecordFailure(outcome.Message, transient: false, _options.MaxAttempts, TimeSpan.Zero, now);
            }
        }
        catch (SapUnavailableException ex) when (ex.IsCircuitOpen)
        {
            var now = timeProvider.GetUtcNow();
            audit = request.Defer(ex.Message, now + _options.CircuitOpenDelay, now);
            LogDeferred(request.Id, request.Sid, _options.CircuitOpenDelay);
        }
        catch (SapUnavailableException ex)
        {
            audit = RecordTransientFailure(request, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unknown failures are retried like transient ones; MaxAttempts bounds them.
            LogUnexpectedError(ex, request.Id, request.Sid);
            audit = RecordTransientFailure(request, ex.Message);
        }

        auditLog.Add(audit);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.Status == ActionStatus.Failed)
        {
            activity?.SetStatus(ActivityStatusCode.Error, request.LastError);
            RaiseAlarm(request);
        }

        activity?.SetTag("action.status", request.Status.ToString());
        return true;
    }

    private async Task<(ActionRequest? Claimed, bool Processed)> ClaimNextAsync(CancellationToken cancellationToken)
    {
        ActionRequest? claimed = null;
        var processed = false;

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            claimed = null;
            processed = false;

            var now = timeProvider.GetUtcNow();
            if (await actions.LockNextDueAsync(now, now - _options.ExecutionLease, ct) is not { } next)
            {
                return;
            }

            processed = true;
            if (next.Status == ActionStatus.Executing)
            {
                // The previous executor stopped before recording a result. SAP actions are idempotent, so retry.
                LogRecovered(next.Id, next.Sid);
                auditLog.Add(next.RecordFailure("Execution was interrupted before a result was recorded.", transient: true, _options.MaxAttempts, TimeSpan.Zero, now));
                if (next.Status == ActionStatus.Failed)
                {
                    await unitOfWork.SaveChangesAsync(ct);
                    RaiseAlarm(next);
                    return;
                }
            }

            auditLog.Add(next.StartExecution(now));
            await unitOfWork.SaveChangesAsync(ct);
            claimed = next;
        }, cancellationToken);

        return (claimed, processed);
    }

    private Task<SapActionOutcome> SendToSapAsync(ActionRequest request, CancellationToken cancellationToken)
    {
        switch (request.Type)
        {
            case ActionType.UnlockUser:
                return sap.UnlockUserAsync(request.Sid, request.Target, cancellationToken);
            case ActionType.RestartJob:
                var (jobName, runId) = SplitJobTarget(request.Target);
                return sap.RestartJobAsync(request.Sid, jobName, runId, cancellationToken);
            case ActionType.ImportTransport:
                return sap.ImportTransportAsync(request.Sid, request.Target, cancellationToken);
            default:
                throw new InvalidOperationException($"Action type {request.Type} has no executor.");
        }
    }

    // "JOBNAME/JOBCOUNT", validated when the request was created.
    private static (string JobName, string RunId) SplitJobTarget(string target)
    {
        var separator = target.LastIndexOf('/');
        return (target[..separator], target[(separator + 1)..]);
    }

    private AuditEntry RecordTransientFailure(ActionRequest request, string error)
    {
        var delay = _options.RetryDelayAfter(request.Attempts);
        var audit = request.RecordFailure(error, transient: true, _options.MaxAttempts, delay, timeProvider.GetUtcNow());
        if (request.Status == ActionStatus.Approved)
        {
            LogRetryScheduled(request.Id, request.Sid, request.Attempts, _options.MaxAttempts, delay, error);
        }

        return audit;
    }

    private void RaiseAlarm(ActionRequest request) =>
        LogAlarm(request.Id, request.Sid, request.Type, request.Target, request.Attempts, request.LastError);

    [LoggerMessage(Level = LogLevel.Information, Message = "Action {ActionId} on {Sid} succeeded: {ActionType} {Target} (attempt {Attempt})")]
    private partial void LogSucceeded(Guid actionId, string sid, ActionType actionType, string target, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action {ActionId} on {Sid} failed (attempt {Attempt} of {MaxAttempts}), retry in {RetryDelay}: {Error}")]
    private partial void LogRetryScheduled(Guid actionId, string sid, int attempt, int maxAttempts, TimeSpan retryDelay, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action {ActionId} on {Sid} deferred by {Delay}: SAP circuit breaker is open")]
    private partial void LogDeferred(Guid actionId, string sid, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Action {ActionId} on {Sid} was left in Executing by a stopped executor; retrying")]
    private partial void LogRecovered(Guid actionId, string sid);

    [LoggerMessage(Level = LogLevel.Error, Message = "Action {ActionId} on {Sid} hit an unexpected error")]
    private partial void LogUnexpectedError(Exception exception, Guid actionId, string sid);

    [LoggerMessage(Level = LogLevel.Critical, Message = "ALARM: action {ActionId} on {Sid} failed for good: {ActionType} {Target} after {Attempts} attempts. Last error: {Error}")]
    private partial void LogAlarm(Guid actionId, string sid, ActionType actionType, string target, int attempts, string? error);
}
