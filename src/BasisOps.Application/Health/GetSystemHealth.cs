using BasisOps.Application.Abstractions;
using BasisOps.Application.Findings;
using BasisOps.Application.Ports;
using BasisOps.Domain.Scans;
using BasisOps.Domain.Systems;

namespace BasisOps.Application.Health;

public sealed record GetSystemHealthQuery(string Sid);

public sealed record LastScanDto(DateTimeOffset StartedAt, TimeSpan Duration, HealthScanStatus Status, string? Error);

public sealed record SystemHealthDto(
    string Sid,
    SystemRole Role,
    HealthStatus Status,
    SeverityCounts Counts,
    LastScanDto? LastScan,
    IReadOnlyList<FindingDto> OpenFindings);

public sealed class GetSystemHealthHandler(
    ISystemRegistry systems,
    IFindingRepository findings,
    IHealthScanRepository scans) : IQueryHandler<GetSystemHealthQuery, SystemHealthDto>
{
    public async Task<Result<SystemHealthDto>> HandleAsync(GetSystemHealthQuery query, CancellationToken cancellationToken)
    {
        if (await systems.FindAsync(query.Sid, cancellationToken) is not { } system)
        {
            return Error.NotFound("system_not_found", $"System {query.Sid} is not managed.");
        }

        var open = await findings.GetOpenAsync(system.Sid, cancellationToken);
        var lastScan = await scans.GetLatestAsync(system.Sid, cancellationToken);
        var counts = SeverityCounts.From(open);

        return new SystemHealthDto(
            system.Sid,
            system.Role,
            counts.ToHealthStatus(),
            counts,
            lastScan is null ? null : new LastScanDto(lastScan.StartedAt, lastScan.Duration, lastScan.Status, lastScan.Error),
            [.. open.OrderByDescending(f => f.Severity).ThenBy(f => f.Category).ThenBy(f => f.Key, StringComparer.Ordinal).Select(FindingDto.From)]);
    }
}
