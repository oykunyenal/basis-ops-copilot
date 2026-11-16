using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;
using BasisOps.Domain.Audit;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Scans;
using Microsoft.EntityFrameworkCore;

namespace BasisOps.Infrastructure.Persistence;

internal sealed class FindingRepository(BasisOpsDbContext db) : IFindingRepository
{
    public async Task<IReadOnlyList<Finding>> GetOpenAsync(string sid, CancellationToken cancellationToken) =>
        await db.Findings.Where(f => f.Sid == sid && f.ResolvedAt == null).ToListAsync(cancellationToken);

    public async Task<PagedResult<Finding>> ListAsync(FindingFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Findings.AsNoTracking();
        if (filter.Sid is not null)
        {
            query = query.Where(f => f.Sid == filter.Sid);
        }

        if (filter.Severity is { } severity)
        {
            query = query.Where(f => f.Severity == severity);
        }

        if (filter.OpenOnly)
        {
            query = query.Where(f => f.ResolvedAt == null);
        }

        var total = await query.CountAsync(cancellationToken);

        // Severity is stored as text, so order by rank explicitly. Id (UUIDv7) keeps paging stable.
        var items = await query
            .OrderByDescending(f => f.Severity == Severity.Critical ? 2 : f.Severity == Severity.Warning ? 1 : 0)
            .ThenByDescending(f => f.LastSeen)
            .ThenBy(f => f.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Finding>(items, page, pageSize, total);
    }

    public void AddRange(IEnumerable<Finding> findings) => db.Findings.AddRange(findings);
}

internal sealed class ActionRequestRepository(BasisOpsDbContext db) : IActionRequestRepository
{
    public Task<ActionRequest?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.ActionRequests.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<ActionRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        db.ActionRequests.FirstOrDefaultAsync(a => a.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<ActionRequest>> ListAsync(ActionStatus? status, int limit, CancellationToken cancellationToken)
    {
        var query = db.ActionRequests.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        return await query.OrderByDescending(a => a.RequestedAt).ThenByDescending(a => a.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public void Add(ActionRequest request) => db.ActionRequests.Add(request);
}

internal sealed class HealthScanRepository(BasisOpsDbContext db) : IHealthScanRepository
{
    public Task<HealthScan?> GetLatestAsync(string sid, CancellationToken cancellationToken) =>
        db.HealthScans.AsNoTracking().Where(s => s.Sid == sid).OrderByDescending(s => s.StartedAt).FirstOrDefaultAsync(cancellationToken);

    public void Add(HealthScan scan) => db.HealthScans.Add(scan);
}

internal sealed class AuditLog(BasisOpsDbContext db) : IAuditLog
{
    public async Task<IReadOnlyList<AuditEntry>> ListAsync(string? target, int limit, CancellationToken cancellationToken)
    {
        var query = db.AuditEntries.AsNoTracking();
        if (target is not null)
        {
            query = query.Where(e => e.Target == target);
        }

        return await query.OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public void Add(AuditEntry entry) => db.AuditEntries.Add(entry);
}
