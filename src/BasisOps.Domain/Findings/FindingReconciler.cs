namespace BasisOps.Domain.Findings;

public sealed record ReconcileResult(
    IReadOnlyList<Finding> Opened,
    IReadOnlyList<Finding> Updated,
    IReadOnlyList<Finding> Resolved);

/// <summary>Merges the candidates of a new scan into the open findings of a system.</summary>
public static class FindingReconciler
{
    public static ReconcileResult Reconcile(
        string sid,
        IReadOnlyCollection<Finding> openFindings,
        IReadOnlyCollection<FindingCandidate> candidates,
        DateTimeOffset now)
    {
        var openByKey = openFindings
            .Where(f => !f.IsResolved && f.Sid == sid)
            .ToDictionary(f => f.Key, StringComparer.Ordinal);

        // Two rules reporting the same key would be a bug; the most severe one wins.
        var candidatesByKey = candidates
            .GroupBy(c => c.Key, StringComparer.Ordinal)
            .Select(g => g.MaxBy(c => c.Severity)!)
            .ToList();

        List<Finding> opened = [];
        List<Finding> updated = [];

        foreach (var candidate in candidatesByKey)
        {
            if (openByKey.Remove(candidate.Key, out var existing))
            {
                existing.Observe(candidate, now);
                updated.Add(existing);
            }
            else
            {
                opened.Add(Finding.Open(sid, candidate, now));
            }
        }

        // Whatever is still in the dictionary was not observed in this scan.
        var resolved = openByKey.Values.ToList();
        foreach (var finding in resolved)
        {
            finding.Resolve(now);
        }

        return new ReconcileResult(opened, updated, resolved);
    }
}
