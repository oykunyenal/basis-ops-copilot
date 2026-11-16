using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.Domain.Rules;

/// <summary>
/// SM37: canceled background jobs. Critical for business-critical jobs, Warning otherwise.
/// A canceled run counts as resolved once a later run of the same job finished successfully.
/// </summary>
public sealed class CanceledJobRule(IReadOnlyList<string>? criticalJobPatterns = null, TimeSpan? lookback = null) : IHealthRule
{
    public static readonly IReadOnlyList<string> DefaultCriticalJobPatterns = ["Z_BILLING*", "SAP_COLLECTOR*"];

    private readonly IReadOnlyList<string> _criticalPatterns = criticalJobPatterns ?? DefaultCriticalJobPatterns;
    private readonly TimeSpan _lookback = lookback ?? TimeSpan.FromHours(24);

    public FindingCategory Category => FindingCategory.Job;

    public IEnumerable<FindingCandidate> Evaluate(SystemSnapshot snapshot)
    {
        var since = snapshot.CapturedAt - _lookback;
        var sid = snapshot.System.Sid;

        var lastSuccessByJob = snapshot.Jobs
            .Where(j => j.Status == JobRunStatus.Finished)
            .GroupBy(j => j.JobName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(j => j.StartedAt), StringComparer.Ordinal);

        foreach (var run in snapshot.Jobs.Where(j => j.Status == JobRunStatus.Canceled && j.StartedAt >= since))
        {
            if (lastSuccessByJob.TryGetValue(run.JobName, out var lastSuccess) && lastSuccess > run.StartedAt)
            {
                continue;
            }

            var critical = _criticalPatterns.Any(p => WildcardPattern.IsMatch(p, run.JobName));
            yield return new FindingCandidate(
                FindingCategory.Job,
                critical ? Severity.Critical : Severity.Warning,
                $"JOB:{sid}:{run.JobName}:{run.RunId}",
                $"Job {run.JobName} canceled",
                $"Run {run.RunId} of {run.JobName} (owner {run.Owner}) was canceled at {run.StartedAt:u}.");
        }
    }
}
