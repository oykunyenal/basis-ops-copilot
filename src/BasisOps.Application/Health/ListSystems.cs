using BasisOps.Application.Abstractions;
using BasisOps.Application.Findings;
using BasisOps.Application.Ports;
using BasisOps.Domain.Scans;
using BasisOps.Domain.Systems;

namespace BasisOps.Application.Health;

public sealed record ListSystemsQuery;

public sealed record SystemSummaryDto(
    string Sid,
    SystemRole Role,
    HealthStatus Status,
    SeverityCounts Counts,
    DateTimeOffset? LastScanAt,
    HealthScanStatus? LastScanStatus);

public sealed class ListSystemsHandler(
    ISystemRegistry systems,
    IFindingRepository findings,
    IHealthScanRepository scans) : IQueryHandler<ListSystemsQuery, IReadOnlyList<SystemSummaryDto>>
{
    public async Task<Result<IReadOnlyList<SystemSummaryDto>>> HandleAsync(ListSystemsQuery query, CancellationToken cancellationToken)
    {
        var result = new List<SystemSummaryDto>();
        foreach (var system in await systems.ListAsync(cancellationToken))
        {
            var counts = SeverityCounts.From(await findings.GetOpenAsync(system.Sid, cancellationToken));
            var lastScan = await scans.GetLatestAsync(system.Sid, cancellationToken);
            result.Add(new SystemSummaryDto(system.Sid, system.Role, counts.ToHealthStatus(), counts, lastScan?.StartedAt, lastScan?.Status));
        }

        return result;
    }
}
