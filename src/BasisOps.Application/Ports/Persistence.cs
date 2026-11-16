using BasisOps.Application.Abstractions;
using BasisOps.Domain.Actions;
using BasisOps.Domain.Audit;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Scans;

namespace BasisOps.Application.Ports;

public sealed record FindingFilter(string? Sid, Severity? Severity, bool OpenOnly);

public interface IFindingRepository
{
    Task<IReadOnlyList<Finding>> GetOpenAsync(string sid, CancellationToken cancellationToken);

    Task<PagedResult<Finding>> ListAsync(FindingFilter filter, int page, int pageSize, CancellationToken cancellationToken);

    void AddRange(IEnumerable<Finding> findings);
}

public interface IActionRequestRepository
{
    Task<ActionRequest?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ActionRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionRequest>> ListAsync(ActionStatus? status, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the next action the executor should work on and skips rows locked by other executors
    /// (<c>FOR UPDATE SKIP LOCKED</c>): an approved action that is due, or one stuck in Executing since
    /// before <paramref name="staleExecutingBefore"/> (its executor died). Call inside
    /// <see cref="IUnitOfWork.ExecuteInTransactionAsync"/>; the lock is held until the transaction ends.
    /// </summary>
    Task<ActionRequest?> LockNextDueAsync(DateTimeOffset now, DateTimeOffset staleExecutingBefore, CancellationToken cancellationToken);

    void Add(ActionRequest request);
}

public interface IHealthScanRepository
{
    Task<HealthScan?> GetLatestAsync(string sid, CancellationToken cancellationToken);

    void Add(HealthScan scan);
}

public interface IAuditLog
{
    Task<IReadOnlyList<AuditEntry>> ListAsync(string? target, int limit, CancellationToken cancellationToken);

    void Add(AuditEntry entry);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="operation"/> in a database transaction. The operation may run more than once
    /// (transient database errors are retried), so it must start from a clean state and be safe to repeat.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}
