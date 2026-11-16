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
}
