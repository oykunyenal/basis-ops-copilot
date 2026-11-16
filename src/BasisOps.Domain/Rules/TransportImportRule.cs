using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.Domain.Rules;

/// <summary>STMS: imported transports with warnings (RC 4) or errors (RC 8 and above).</summary>
public sealed class TransportImportRule : IHealthRule
{
    public FindingCategory Category => FindingCategory.Transport;

    public IEnumerable<FindingCandidate> Evaluate(SystemSnapshot snapshot)
    {
        var sid = snapshot.System.Sid;

        foreach (var transport in snapshot.Transports.Where(t => t is { Status: TransportImportStatus.Imported, ReturnCode: >= 4 }))
        {
            var rc = transport.ReturnCode!.Value;
            yield return new FindingCandidate(
                FindingCategory.Transport,
                rc >= 8 ? Severity.Critical : Severity.Info,
                $"TRANSPORT:{sid}:{transport.Number}",
                $"Transport {transport.Number} imported with RC {rc}",
                $"{transport.Description} (owner {transport.Owner}).");
        }
    }
}
