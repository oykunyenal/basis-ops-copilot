using System.Collections.Concurrent;
using BasisOps.Application.Ports;
using BasisOps.Domain.Observations;

namespace BasisOps.IntegrationTests;

/// <summary>SAP test double with configurable observations. Actions always succeed.</summary>
public sealed class StubSapGateway : ISapGateway
{
    public List<JobRun> Jobs { get; } = [];

    public List<LockedUser> LockedUsers { get; } = [];

    /// <summary>Number of unlock calls per "SID:USER", to prove an action is applied once.</summary>
    public ConcurrentDictionary<string, int> UnlockCalls { get; } = new();

    /// <summary>Simulated SAP latency for actions, so parallel executors overlap.</summary>
    public TimeSpan ActionDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    public Task<IReadOnlyList<JobRun>> GetJobRunsAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<JobRun>>([.. Jobs]);

    /// <summary>Dumps by id (ST22), returned for every SID.</summary>
    public ConcurrentDictionary<string, ShortDumpDetail> Dumps { get; } = new();

    public Task<IReadOnlyList<ShortDump>> GetShortDumpsAsync(string sid, DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ShortDump>>([.. Dumps.Values.Select(d => d.Dump).Where(d => d.OccurredAt >= since)]);

    public Task<ShortDumpDetail?> GetShortDumpAsync(string sid, string dumpId, CancellationToken cancellationToken) =>
        Task.FromResult(Dumps.GetValueOrDefault(dumpId));

    public Task<IReadOnlyList<LockedUser>> GetLockedUsersAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LockedUser>>([.. LockedUsers]);

    public Task<IReadOnlyList<TransportRequest>> GetTransportQueueAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TransportRequest>>([]);

    public Task<IReadOnlyList<Certificate>> GetCertificatesAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Certificate>>([]);

    public Task<IReadOnlyList<WorkProcessUsage>> GetWorkProcessUsageAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkProcessUsage>>([]);

    public async Task<SapActionOutcome> UnlockUserAsync(string sid, string userName, CancellationToken cancellationToken)
    {
        UnlockCalls.AddOrUpdate($"{sid}:{userName}", 1, (_, count) => count + 1);
        await Task.Delay(ActionDelay, cancellationToken);
        return new SapActionOutcome(SapActionStatus.Succeeded, "unlocked");
    }

    public Task<SapActionOutcome> RestartJobAsync(string sid, string jobName, string runId, CancellationToken cancellationToken) =>
        Task.FromResult(new SapActionOutcome(SapActionStatus.Succeeded, "restarted"));

    public Task<SapActionOutcome> ImportTransportAsync(string sid, string transportNumber, CancellationToken cancellationToken) =>
        Task.FromResult(new SapActionOutcome(SapActionStatus.Succeeded, "imported"));
}
