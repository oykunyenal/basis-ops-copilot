using BasisOps.Domain.Observations;

namespace BasisOps.Application.Ports;

public enum SapActionStatus
{
    Succeeded,

    /// <summary>SAP refused the action (object not found, wrong state...). Retrying will not help.</summary>
    Rejected,
}

public sealed record SapActionOutcome(SapActionStatus Status, string Message)
{
    public bool Succeeded => Status == SapActionStatus.Succeeded;
}

/// <summary>
/// Anti-corruption layer towards SAP. Implementations translate SAP structures (JOBNAME, BNAME, TRKORR...)
/// into domain types. Today: FakeSap over HTTP. In a real landscape: SAP .NET Connector (RFC/BAPI) or OData.
/// Transient problems (system down, rate limited, timeouts) are reported as <see cref="SapUnavailableException"/>.
/// </summary>
public interface ISapGateway
{
    // SM37
    Task<IReadOnlyList<JobRun>> GetJobRunsAsync(string sid, CancellationToken cancellationToken);

    // ST22
    Task<IReadOnlyList<ShortDump>> GetShortDumpsAsync(string sid, DateTimeOffset since, CancellationToken cancellationToken);

    Task<ShortDumpDetail?> GetShortDumpAsync(string sid, string dumpId, CancellationToken cancellationToken);

    // SU01
    Task<IReadOnlyList<LockedUser>> GetLockedUsersAsync(string sid, CancellationToken cancellationToken);

    // STMS
    Task<IReadOnlyList<TransportRequest>> GetTransportQueueAsync(string sid, CancellationToken cancellationToken);

    // STRUST
    Task<IReadOnlyList<Certificate>> GetCertificatesAsync(string sid, CancellationToken cancellationToken);

    // SM50
    Task<IReadOnlyList<WorkProcessUsage>> GetWorkProcessUsageAsync(string sid, CancellationToken cancellationToken);

    // BAPI_USER_UNLOCK
    Task<SapActionOutcome> UnlockUserAsync(string sid, string userName, CancellationToken cancellationToken);

    // SM37 repeat scheduling
    Task<SapActionOutcome> RestartJobAsync(string sid, string jobName, string runId, CancellationToken cancellationToken);

    // STMS import
    Task<SapActionOutcome> ImportTransportAsync(string sid, string transportNumber, CancellationToken cancellationToken);
}

/// <summary>SAP could not be reached or asked us to back off. The operation may succeed later.</summary>
public sealed class SapUnavailableException(string message, bool isCircuitOpen = false, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>The client stopped calling SAP for a while (circuit breaker). The call was never sent.</summary>
    public bool IsCircuitOpen { get; } = isCircuitOpen;
}
