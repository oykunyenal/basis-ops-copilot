namespace BasisOps.Domain.Audit;

/// <summary>Append-only record of who did what, when, and how the state changed.</summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    public string Actor { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }

    /// <summary>What the change was about, e.g. "PRD:MEHMET". Used to search the audit log.</summary>
    public string Target { get; private set; } = string.Empty;

    public string? FromState { get; private set; }

    public string? ToState { get; private set; }

    public string? Detail { get; private set; }

    public static AuditEntry Create(
        DateTimeOffset timestamp,
        string actor,
        string action,
        string entityType,
        Guid entityId,
        string target,
        string? fromState,
        string? toState,
        string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        return new AuditEntry
        {
            Id = Guid.CreateVersion7(timestamp),
            Timestamp = timestamp,
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Target = target,
            FromState = fromState,
            ToState = toState,
            Detail = detail,
        };
    }
}
