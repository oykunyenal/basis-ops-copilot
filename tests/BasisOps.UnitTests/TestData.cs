using BasisOps.Domain.Observations;
using BasisOps.Domain.Systems;

namespace BasisOps.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

    public static readonly SapSystem Prd = new("PRD", SystemRole.Production);

    public static readonly SapSystem Qas = new("QAS", SystemRole.Quality);

    public static SystemSnapshot Snapshot(
        SapSystem? system = null,
        IReadOnlyList<JobRun>? jobs = null,
        IReadOnlyList<ShortDump>? dumps = null,
        IReadOnlyList<LockedUser>? users = null,
        IReadOnlyList<TransportRequest>? transports = null,
        IReadOnlyList<Certificate>? certificates = null,
        IReadOnlyList<WorkProcessUsage>? workProcesses = null) =>
        new(system ?? Prd, Now, jobs ?? [], dumps ?? [], users ?? [], transports ?? [], certificates ?? [], workProcesses ?? []);

    public static ShortDump Dump(string runtimeError, TimeSpan ago, string program = "Z_PROGRAM") =>
        new(Guid.NewGuid().ToString("N"), runtimeError, program, "MEHMET", "prdapp01", Now - ago, "Short text");

    public static JobRun Job(string name, JobRunStatus status, TimeSpan ago, string runId = "12345678") =>
        new(name, runId, status, Now - ago, "BATCH_ADM");
}
