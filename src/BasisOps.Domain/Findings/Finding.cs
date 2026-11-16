namespace BasisOps.Domain.Findings;

public enum FindingCategory
{
    Job,
    Dump,
    User,
    Transport,
    Certificate,
    WorkProcess,
}

public enum Severity
{
    Info,
    Warning,
    Critical,
}

/// <summary>What a health rule reports. The key identifies the same problem across scans.</summary>
public sealed record FindingCandidate(FindingCategory Category, Severity Severity, string Key, string Title, string Detail);

/// <summary>
/// A problem detected by health scans. One open finding exists per key; when the problem is no longer
/// observed the finding is resolved. If it shows up again later, a new finding is opened (history is kept).
/// </summary>
public sealed class Finding
{
    private Finding()
    {
    }

    public Guid Id { get; private set; }

    public string Sid { get; private set; } = string.Empty;

    public FindingCategory Category { get; private set; }

    public Severity Severity { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Detail { get; private set; } = string.Empty;

    public DateTimeOffset FirstSeen { get; private set; }

    public DateTimeOffset LastSeen { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsResolved => ResolvedAt is not null;

    public static Finding Open(string sid, FindingCandidate candidate, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Sid = sid,
        Category = candidate.Category,
        Severity = candidate.Severity,
        Key = candidate.Key,
        Title = candidate.Title,
        Detail = candidate.Detail,
        FirstSeen = now,
        LastSeen = now,
    };

    /// <summary>The problem was observed again; severity and detail follow the latest observation.</summary>
    public void Observe(FindingCandidate candidate, DateTimeOffset now)
    {
        if (IsResolved)
        {
            throw new InvalidOperationException($"Finding {Key} is resolved and cannot be observed again; open a new finding instead.");
        }

        if (candidate.Key != Key)
        {
            throw new ArgumentException($"Candidate key {candidate.Key} does not match finding key {Key}.", nameof(candidate));
        }

        Severity = candidate.Severity;
        Title = candidate.Title;
        Detail = candidate.Detail;
        LastSeen = now;
    }

    public void Resolve(DateTimeOffset now)
    {
        if (!IsResolved)
        {
            ResolvedAt = now;
        }
    }
}
