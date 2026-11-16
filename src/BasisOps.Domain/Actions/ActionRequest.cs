using System.Security.Cryptography;
using System.Text;
using BasisOps.Domain.Audit;
using BasisOps.Domain.Common;
using BasisOps.Domain.Systems;

namespace BasisOps.Domain.Actions;

public enum ActionType
{
    UnlockUser,
    RestartJob,
    ImportTransport,
}

public enum ActionStatus
{
    Requested,
    Approved,
    Executing,
    Succeeded,
    Failed,
    Rejected,
}

/// <summary>
/// A change to an SAP system that needs human approval before it is executed.
/// <code>
/// Requested → Approved → Executing → Succeeded
///           ↘ Rejected             ↘ Failed
///                       Executing → Approved (transient error: retry scheduled; or deferred without counting)
/// </code>
/// Every transition returns the audit entry that records it.
/// </summary>
public sealed class ActionRequest
{
    public const string ExecutorActor = "system:executor";

    private static readonly Dictionary<ActionStatus, ActionStatus[]> AllowedTransitions = new()
    {
        [ActionStatus.Requested] = [ActionStatus.Approved, ActionStatus.Rejected],
        [ActionStatus.Approved] = [ActionStatus.Executing],
        [ActionStatus.Executing] = [ActionStatus.Succeeded, ActionStatus.Failed, ActionStatus.Approved],
        [ActionStatus.Succeeded] = [],
        [ActionStatus.Failed] = [],
        [ActionStatus.Rejected] = [],
    };

    private ActionRequest()
    {
    }

    public Guid Id { get; private set; }

    public ActionType Type { get; private set; }

    public string Sid { get; private set; } = string.Empty;

    public SystemRole SystemRole { get; private set; }

    /// <summary>Normalized target: user name, "JOBNAME/JOBCOUNT" or transport number.</summary>
    public string Target { get; private set; } = string.Empty;

    public string RequestedBy { get; private set; } = string.Empty;

    public string? Reason { get; private set; }

    public ActionStatus Status { get; private set; }

    public string? DecidedBy { get; private set; }

    public string? DecisionComment { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public string? ResultMessage { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Hash of the request content; used to detect an idempotency key reused for a different request.</summary>
    public string Fingerprint { get; private set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTerminal => Status is ActionStatus.Succeeded or ActionStatus.Failed or ActionStatus.Rejected;

    /// <summary>"PRD:MEHMET", used as the audit target.</summary>
    public string AuditTarget => $"{Sid}:{Target}";

    public static (ActionRequest Request, AuditEntry Audit) Create(
        ActionType type,
        SapSystem system,
        string target,
        string requestedBy,
        string? reason,
        string idempotencyKey,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (ValidateTarget(type, target) is { } error)
        {
            throw new ArgumentException(error, nameof(target));
        }

        var normalizedTarget = NormalizeTarget(target);
        var request = new ActionRequest
        {
            Id = Guid.CreateVersion7(now),
            Type = type,
            Sid = system.Sid,
            SystemRole = system.Role,
            Target = normalizedTarget,
            RequestedBy = requestedBy.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Status = ActionStatus.Requested,
            IdempotencyKey = idempotencyKey,
            Fingerprint = ComputeFingerprint(type, system.Sid, normalizedTarget, requestedBy),
            RequestedAt = now,
            UpdatedAt = now,
        };

        var audit = AuditEntry.Create(now, request.RequestedBy, "ActionRequested", nameof(ActionRequest), request.Id,
            request.AuditTarget, null, nameof(ActionStatus.Requested), $"{type} requested. {request.Reason}".Trim());

        return (request, audit);
    }

    /// <summary>Four-eyes principle: on production the requester cannot approve their own request.</summary>
    public AuditEntry Approve(string approver, string? comment, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approver);

        if (SystemRole == SystemRole.Production && IsSamePerson(approver, RequestedBy))
        {
            throw new FourEyesViolationException(Sid, approver);
        }

        var from = TransitionTo(ActionStatus.Approved, now);
        DecidedBy = approver.Trim();
        DecisionComment = comment;
        NextAttemptAt = now;
        return Audit(now, DecidedBy, "ActionApproved", from, comment);
    }

    public AuditEntry Reject(string rejectedBy, string? reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectedBy);

        var from = TransitionTo(ActionStatus.Rejected, now);
        DecidedBy = rejectedBy.Trim();
        DecisionComment = reason;
        NextAttemptAt = null;
        return Audit(now, DecidedBy, "ActionRejected", from, reason);
    }

    /// <summary>The executor picked the request up. Counts as one attempt.</summary>
    public AuditEntry StartExecution(DateTimeOffset now)
    {
        if (Status == ActionStatus.Approved && NextAttemptAt > now)
        {
            throw new InvalidOperationException($"Action {Id} is not due before {NextAttemptAt:u}.");
        }

        var from = TransitionTo(ActionStatus.Executing, now);
        Attempts++;
        NextAttemptAt = null;
        return Audit(now, ExecutorActor, "ActionExecutionStarted", from, $"Attempt {Attempts}.");
    }

    public AuditEntry Succeed(string? resultMessage, DateTimeOffset now)
    {
        var from = TransitionTo(ActionStatus.Succeeded, now);
        ResultMessage = resultMessage;
        LastError = null;
        return Audit(now, ExecutorActor, "ActionSucceeded", from, resultMessage);
    }

    /// <summary>
    /// Records a failed attempt. Transient failures are retried after <paramref name="retryDelay"/> until
    /// <paramref name="maxAttempts"/> is reached; permanent failures fail immediately.
    /// </summary>
    public AuditEntry RecordFailure(string error, bool transient, int maxAttempts, TimeSpan retryDelay, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        // Executing -> Approved is only a retry; it must never stand in for a human approval.
        EnsureStatus(ActionStatus.Executing, transient && Attempts < maxAttempts ? ActionStatus.Approved : ActionStatus.Failed);
        LastError = error;

        if (transient && Attempts < maxAttempts)
        {
            var from = TransitionTo(ActionStatus.Approved, now);
            NextAttemptAt = now + retryDelay;
            return Audit(now, ExecutorActor, "ActionRetryScheduled", from, $"Attempt {Attempts} of {maxAttempts} failed: {error}");
        }

        var previous = TransitionTo(ActionStatus.Failed, now);
        var why = transient ? $"Gave up after {Attempts} attempts: {error}" : $"Permanent error: {error}";
        return Audit(now, ExecutorActor, "ActionFailed", previous, why);
    }

    /// <summary>
    /// The attempt never reached SAP (e.g. the circuit breaker is open), so it does not count:
    /// the request goes back to <see cref="ActionStatus.Approved"/> and waits until <paramref name="nextAttemptAt"/>.
    /// </summary>
    public AuditEntry Defer(string reason, DateTimeOffset nextAttemptAt, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureStatus(ActionStatus.Executing, ActionStatus.Approved);

        var from = TransitionTo(ActionStatus.Approved, now);
        Attempts--;
        NextAttemptAt = nextAttemptAt;
        return Audit(now, ExecutorActor, "ActionDeferred", from, reason);
    }

    public bool HasSameContentAs(ActionType type, string sid, string target, string requestedBy) =>
        Fingerprint == ComputeFingerprint(type, sid, NormalizeTarget(target), requestedBy);

    /// <summary>Returns an error message, or null when the target is valid for the action type.</summary>
    public static string? ValidateTarget(ActionType type, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return "Target is required.";
        }

        var value = NormalizeTarget(target);
        return type switch
        {
            // SU01: BNAME is at most 12 characters.
            ActionType.UnlockUser => value.Length <= 12 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')
                ? null
                : "User name must be 1-12 characters (letters, digits, '_', '-', '.').",

            // SM37: JOBNAME (max 32) and the 8-digit JOBCOUNT.
            ActionType.RestartJob => value.Split('/') is [{ Length: > 0 and <= 32 } name, { Length: 8 } count]
                && !name.Any(char.IsWhiteSpace) && count.All(char.IsAsciiDigit)
                ? null
                : "Job target must be 'JOBNAME/JOBCOUNT' (JOBCOUNT has 8 digits).",

            // STMS: <SID>K<6 digits>, e.g. DEVK900123.
            ActionType.ImportTransport => value.Length == 10 && SapSystem.IsValidSid(value[..3]) && value[3] == 'K' && value[4..].All(char.IsAsciiDigit)
                ? null
                : "Transport number must look like DEVK900123.",

            _ => $"Unknown action type {type}.",
        };
    }

    public static string ComputeFingerprint(ActionType type, string sid, string normalizedTarget, string requestedBy)
    {
        // Reason is deliberately not part of the fingerprint: a client retrying with a reworded reason is the same request.
        var content = $"{type}|{sid.ToUpperInvariant()}|{normalizedTarget}|{requestedBy.Trim().ToUpperInvariant()}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private static string NormalizeTarget(string target) => target.Trim().ToUpperInvariant();

    private static bool IsSamePerson(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private void EnsureStatus(ActionStatus required, ActionStatus next)
    {
        if (Status != required)
        {
            throw new InvalidStateTransitionException(nameof(ActionRequest), Status.ToString(), next.ToString());
        }
    }

    private ActionStatus TransitionTo(ActionStatus next, DateTimeOffset now)
    {
        if (!AllowedTransitions[Status].Contains(next))
        {
            throw new InvalidStateTransitionException(nameof(ActionRequest), Status.ToString(), next.ToString());
        }

        var previous = Status;
        Status = next;
        UpdatedAt = now;
        return previous;
    }

    private AuditEntry Audit(DateTimeOffset now, string actor, string action, ActionStatus from, string? detail) =>
        AuditEntry.Create(now, actor, action, nameof(ActionRequest), Id, AuditTarget, from.ToString(), Status.ToString(), detail);
}
