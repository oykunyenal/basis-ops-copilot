using BasisOps.Application.Ports;
using BasisOps.Domain.Observations;

namespace BasisOps.IntegrationTests;

/// <summary>SAP test double with configurable observations. Actions always succeed.</summary>
public sealed class StubSapGateway : ISapGateway
{
    public List<JobRun> Jobs { get; } = [];

    public List<LockedUser> LockedUsers { get; } = [];

    public Task<IReadOnlyList<JobRun>> GetJobRunsAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<JobRun>>([.. Jobs]);

    public Task<IReadOnlyList<ShortDump>> GetShortDumpsAsync(string sid, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ShortDump>>([]);

    public Task<ShortDumpDetail?> GetShortDumpAsync(string sid, string dumpId, CancellationToken cancellationToken) =>
        Task.FromResult<ShortDumpDetail?>(null);

    public Task<IReadOnlyList<LockedUser>> GetLockedUsersAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LockedUser>>([.. LockedUsers]);

    public Task<IReadOnlyList<TransportRequest>> GetTransportQueueAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TransportRequest>>([]);

    public Task<IReadOnlyList<Certificate>> GetCertificatesAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Certificate>>([]);

    public Task<IReadOnlyList<WorkProcessUsage>> GetWorkProcessUsageAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkProcessUsage>>([]);

    public Task<SapActionOutcome> UnlockUserAsync(string sid, string userName, CancellationToken cancellationToken) =>
        Task.FromResult(new SapActionOutcome(SapActionStatus.Succeeded, "unlocked"));

    public Task<SapActionOutcome> RestartJobAsync(string sid, string jobName, string runId, CancellationToken cancellationToken) =>
        Task.FromResult(new SapActionOutcome(SapActionStatus.Succeeded, "restarted"));

    public Task<SapActionOutcome> ImportTransportAsync(string sid, string transportNumber, CancellationToken cancellationToken) =>
        Task.FromResult(new SapActionOutcome(SapActionStatus.Succeeded, "imported"));
}
