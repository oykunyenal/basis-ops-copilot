using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.Domain.Rules;

/// <summary>STRUST: certificates that expire soon (or already expired).</summary>
public sealed class CertificateExpiryRule(int warningDays = 30, int criticalDays = 7) : IHealthRule
{
    public FindingCategory Category => FindingCategory.Certificate;

    public IEnumerable<FindingCandidate> Evaluate(SystemSnapshot snapshot)
    {
        var sid = snapshot.System.Sid;

        foreach (var certificate in snapshot.Certificates)
        {
            var remaining = certificate.ValidTo - snapshot.CapturedAt;
            if (remaining >= TimeSpan.FromDays(warningDays))
            {
                continue;
            }

            var title = remaining <= TimeSpan.Zero
                ? $"Certificate in {certificate.Store} expired"
                : $"Certificate in {certificate.Store} expires in {Math.Ceiling(remaining.TotalDays):0} days";

            yield return new FindingCandidate(
                FindingCategory.Certificate,
                remaining < TimeSpan.FromDays(criticalDays) ? Severity.Critical : Severity.Warning,
                $"CERT:{sid}:{certificate.Store}",
                title,
                $"Subject {certificate.Subject}, valid to {certificate.ValidTo:yyyy-MM-dd}.");
        }
    }
}
