using BasisOps.Domain.Findings;

namespace BasisOps.Application.Findings;

public sealed record FindingDto(
    Guid Id,
    string Sid,
    FindingCategory Category,
    Severity Severity,
    string Key,
    string Title,
    string Detail,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    DateTimeOffset? ResolvedAt)
{
    public static FindingDto From(Finding f) =>
        new(f.Id, f.Sid, f.Category, f.Severity, f.Key, f.Title, f.Detail, f.FirstSeen, f.LastSeen, f.ResolvedAt);
}

public enum HealthStatus
{
    Healthy,
    Warning,
    Critical,
}

public sealed record SeverityCounts(int Critical, int Warning, int Info)
{
    public static SeverityCounts From(IEnumerable<Finding> findings)
    {
        var list = findings as IReadOnlyCollection<Finding> ?? [.. findings];
        return new(
            list.Count(f => f.Severity == Severity.Critical),
            list.Count(f => f.Severity == Severity.Warning),
            list.Count(f => f.Severity == Severity.Info));
    }

    /// <summary>Info findings (e.g. a locked user) do not make a system unhealthy.</summary>
    public HealthStatus ToHealthStatus() =>
        Critical > 0 ? HealthStatus.Critical : Warning > 0 ? HealthStatus.Warning : HealthStatus.Healthy;
}
