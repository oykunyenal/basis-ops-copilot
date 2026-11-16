namespace BasisOps.Domain.Scans;

public enum HealthScanStatus
{
    Succeeded,
    Failed,
}

/// <summary>One health scan run against one system.</summary>
public sealed class HealthScan
{
    private HealthScan()
    {
    }

    public Guid Id { get; private set; }

    public string Sid { get; private set; } = string.Empty;

    public DateTimeOffset StartedAt { get; private set; }

    public TimeSpan Duration { get; private set; }

    public HealthScanStatus Status { get; private set; }

    public int FindingCount { get; private set; }

    public string? Error { get; private set; }

    public static HealthScan Succeeded(string sid, DateTimeOffset startedAt, DateTimeOffset finishedAt, int findingCount) =>
        Create(sid, startedAt, finishedAt, HealthScanStatus.Succeeded, findingCount, null);

    public static HealthScan Failed(string sid, DateTimeOffset startedAt, DateTimeOffset finishedAt, string error) =>
        Create(sid, startedAt, finishedAt, HealthScanStatus.Failed, 0, error);

    private static HealthScan Create(string sid, DateTimeOffset startedAt, DateTimeOffset finishedAt, HealthScanStatus status, int findingCount, string? error)
    {
        if (finishedAt < startedAt)
        {
            throw new ArgumentException("A scan cannot finish before it started.", nameof(finishedAt));
        }

        return new HealthScan
        {
            Id = Guid.CreateVersion7(startedAt),
            Sid = sid,
            StartedAt = startedAt,
            Duration = finishedAt - startedAt,
            Status = status,
            FindingCount = findingCount,
            Error = error,
        };
    }
}
