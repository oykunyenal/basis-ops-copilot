using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.Domain.Rules;

/// <summary>ST22: many dumps of the same runtime error within a short window.</summary>
public sealed class DumpFrequencyRule(int warningThreshold = 5, int criticalThreshold = 20, TimeSpan? window = null) : IHealthRule
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromHours(1);

    public FindingCategory Category => FindingCategory.Dump;

    public IEnumerable<FindingCandidate> Evaluate(SystemSnapshot snapshot)
    {
        var since = snapshot.CapturedAt - _window;
        var sid = snapshot.System.Sid;

        var groups = snapshot.Dumps
            .Where(d => d.OccurredAt >= since && d.OccurredAt <= snapshot.CapturedAt)
            .GroupBy(d => d.RuntimeError, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var count = group.Count();
            if (count < warningThreshold)
            {
                continue;
            }

            var programs = string.Join(", ", group.Select(d => d.Program).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            yield return new FindingCandidate(
                FindingCategory.Dump,
                count >= criticalThreshold ? Severity.Critical : Severity.Warning,
                $"DUMP:{sid}:{group.Key}",
                $"{count} x {group.Key} in the last {_window.TotalMinutes:0} minutes",
                $"Programs: {programs}. Latest dump: {group.Max(d => d.OccurredAt):u}.");
        }
    }
}
