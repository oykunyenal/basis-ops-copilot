using BasisOps.Domain.Systems;

namespace BasisOps.Domain.Observations;

/// <summary>Everything a health scan collected from one system at one point in time. Health rules evaluate this.</summary>
public sealed record SystemSnapshot(
    SapSystem System,
    DateTimeOffset CapturedAt,
    IReadOnlyList<JobRun> Jobs,
    IReadOnlyList<ShortDump> Dumps,
    IReadOnlyList<LockedUser> LockedUsers,
    IReadOnlyList<TransportRequest> Transports,
    IReadOnlyList<Certificate> Certificates,
    IReadOnlyList<WorkProcessUsage> WorkProcesses)
{
    public static SystemSnapshot Empty(SapSystem system, DateTimeOffset capturedAt) =>
        new(system, capturedAt, [], [], [], [], [], []);
}
