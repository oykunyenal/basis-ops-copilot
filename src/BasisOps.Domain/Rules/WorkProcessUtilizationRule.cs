using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.Domain.Rules;

/// <summary>SM50 / SM66: dialog work processes close to saturation.</summary>
public sealed class WorkProcessUtilizationRule(double warningAbove = 0.80, double criticalAbove = 0.95) : IHealthRule
{
    public FindingCategory Category => FindingCategory.WorkProcess;

    public IEnumerable<FindingCandidate> Evaluate(SystemSnapshot snapshot)
    {
        var sid = snapshot.System.Sid;

        foreach (var usage in snapshot.WorkProcesses.Where(w => w.Type == WorkProcessType.Dialog && w.Utilization > warningAbove))
        {
            yield return new FindingCandidate(
                FindingCategory.WorkProcess,
                usage.Utilization > criticalAbove ? Severity.Critical : Severity.Warning,
                $"WP:{sid}:{usage.Type}",
                FormattableString.Invariant($"Dialog work processes {usage.Utilization * 100:0}% busy"),
                $"{usage.Busy} of {usage.Total} dialog work processes are busy.");
        }
    }
}
