using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.Domain.Rules;

/// <summary>A single, independently testable health check over a system snapshot.</summary>
public interface IHealthRule
{
    FindingCategory Category { get; }

    IEnumerable<FindingCandidate> Evaluate(SystemSnapshot snapshot);
}
