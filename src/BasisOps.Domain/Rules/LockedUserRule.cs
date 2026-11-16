using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.Domain.Rules;

/// <summary>SU01: locked users. Informational only; listed for the self-service unlock flow.</summary>
public sealed class LockedUserRule : IHealthRule
{
    public FindingCategory Category => FindingCategory.User;

    public IEnumerable<FindingCandidate> Evaluate(SystemSnapshot snapshot)
    {
        var sid = snapshot.System.Sid;

        return snapshot.LockedUsers.Select(user => new FindingCandidate(
            FindingCategory.User,
            Severity.Info,
            $"USER:{sid}:{user.UserName}",
            $"User {user.UserName} is locked",
            user.Reason == LockReason.FailedLogons
                ? $"Locked after failed logon attempts at {user.LockedAt:u}."
                : $"Locked by an administrator at {user.LockedAt:u}."));
    }
}
