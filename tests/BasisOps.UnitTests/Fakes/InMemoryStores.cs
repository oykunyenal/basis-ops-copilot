using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;
using BasisOps.Domain.Audit;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;
using BasisOps.Domain.Scans;
using BasisOps.Domain.Systems;

namespace BasisOps.UnitTests.Fakes;

internal sealed class InMemoryStore :
    IFindingRepository, IActionRequestRepository, IHealthScanRepository, IAuditLog, IUnitOfWork, ISystemRegistry
{
    public List<Finding> Findings { get; } = [];

    public List<ActionRequest> Actions { get; } = [];

    public List<HealthScan> Scans { get; } = [];

    public List<AuditEntry> Audit { get; } = [];

    public List<SapSystem> Systems { get; } = [TestData.Prd, TestData.Qas];

    public int SaveCount { get; private set; }

    Task<IReadOnlyList<Finding>> IFindingRepository.GetOpenAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Finding>>(Findings.Where(f => f.Sid == sid && !f.IsResolved).ToList());

    Task<PagedResult<Finding>> IFindingRepository.ListAsync(FindingFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = Findings.Where(f => (filter.Sid is null || f.Sid == filter.Sid)
            && (filter.Severity is null || f.Severity == filter.Severity)
            && (!filter.OpenOnly || !f.IsResolved)).ToList();
        return Task.FromResult(new PagedResult<Finding>(query.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, query.Count));
    }

    void IFindingRepository.AddRange(IEnumerable<Finding> findings) => Findings.AddRange(findings);

    Task<ActionRequest?> IActionRequestRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Actions.Find(a => a.Id == id));

    Task<ActionRequest?> IActionRequestRepository.GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(Actions.Find(a => a.IdempotencyKey == idempotencyKey));

    Task<IReadOnlyList<ActionRequest>> IActionRequestRepository.ListAsync(ActionStatus? status, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ActionRequest>>(Actions.Where(a => status is null || a.Status == status).Take(limit).ToList());

    Task<ActionRequest?> IActionRequestRepository.LockNextDueAsync(DateTimeOffset now, DateTimeOffset staleExecutingBefore, CancellationToken cancellationToken) =>
        Task.FromResult(Actions
            .Where(a => (a.Status == ActionStatus.Approved && (a.NextAttemptAt is null || a.NextAttemptAt <= now))
                || (a.Status == ActionStatus.Executing && a.UpdatedAt < staleExecutingBefore))
            .OrderBy(a => a.NextAttemptAt ?? a.UpdatedAt)
            .FirstOrDefault());

    void IActionRequestRepository.Add(ActionRequest request) => Actions.Add(request);

    Task<HealthScan?> IHealthScanRepository.GetLatestAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult(Scans.Where(s => s.Sid == sid).MaxBy(s => s.StartedAt));

    void IHealthScanRepository.Add(HealthScan scan) => Scans.Add(scan);

    Task<IReadOnlyList<AuditEntry>> IAuditLog.ListAsync(string? target, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(Audit.Where(a => target is null || a.Target == target).Take(limit).ToList());

    void IAuditLog.Add(AuditEntry entry) => Audit.Add(entry);

    Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task IUnitOfWork.ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);

    Task<IReadOnlyList<SapSystem>> ISystemRegistry.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SapSystem>>(Systems);

    Task<SapSystem?> ISystemRegistry.FindAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult(Systems.Find(s => s.Sid == sid));
}

/// <summary>SAP gateway double returning a configurable snapshot, or failing like an unreachable system.</summary>
internal sealed class FakeSapGateway : ISapGateway
{
    public List<JobRun> Jobs { get; } = [];

    public List<ShortDump> Dumps { get; } = [];

    public List<LockedUser> LockedUsers { get; } = [];

    public bool Unavailable { get; set; }

    /// <summary>Result of every action call; set to throw to simulate SAP failures.</summary>
    public Func<SapActionOutcome> ActionResult { get; set; } = () => new SapActionOutcome(SapActionStatus.Succeeded, "done");

    public List<string> ActionCalls { get; } = [];

    public Task<IReadOnlyList<JobRun>> GetJobRunsAsync(string sid, CancellationToken cancellationToken) => Return<JobRun>(Jobs);

    public Task<IReadOnlyList<ShortDump>> GetShortDumpsAsync(string sid, DateTimeOffset since, CancellationToken cancellationToken) => Return<ShortDump>(Dumps);

    public Task<ShortDumpDetail?> GetShortDumpAsync(string sid, string dumpId, CancellationToken cancellationToken) =>
        Task.FromResult<ShortDumpDetail?>(null);

    public Task<IReadOnlyList<LockedUser>> GetLockedUsersAsync(string sid, CancellationToken cancellationToken) => Return<LockedUser>(LockedUsers);

    public Task<IReadOnlyList<TransportRequest>> GetTransportQueueAsync(string sid, CancellationToken cancellationToken) => Return<TransportRequest>([]);

    public Task<IReadOnlyList<Certificate>> GetCertificatesAsync(string sid, CancellationToken cancellationToken) => Return<Certificate>([]);

    public Task<IReadOnlyList<WorkProcessUsage>> GetWorkProcessUsageAsync(string sid, CancellationToken cancellationToken) => Return<WorkProcessUsage>([]);

    public Task<SapActionOutcome> UnlockUserAsync(string sid, string userName, CancellationToken cancellationToken) =>
        Act($"unlock {sid} {userName}");

    public Task<SapActionOutcome> RestartJobAsync(string sid, string jobName, string runId, CancellationToken cancellationToken) =>
        Act($"restart {sid} {jobName} {runId}");

    public Task<SapActionOutcome> ImportTransportAsync(string sid, string transportNumber, CancellationToken cancellationToken) =>
        Act($"import {sid} {transportNumber}");

    private Task<SapActionOutcome> Act(string call)
    {
        ActionCalls.Add(call);
        return Task.FromResult(ActionResult());
    }

    private Task<IReadOnlyList<T>> Return<T>(List<T> items) =>
        Unavailable
            ? Task.FromException<IReadOnlyList<T>>(new SapUnavailableException("503 Service Unavailable"))
            : Task.FromResult<IReadOnlyList<T>>([.. items]);
}

internal sealed class FakeAiAnalyzer : IAiAnalyzer
{
    public Exception? Failure { get; set; }

    public string Summary { get; set; } = "PRD: 1 critical job cancellation, check Z_BILLING_DAILY first.";

    public Task<DumpAnalysis> AnalyzeDumpAsync(ShortDumpDetail dump, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<string> SummarizeDailyAsync(string sid, IReadOnlyList<Finding> openFindings, CancellationToken cancellationToken) =>
        Failure is null ? Task.FromResult(Summary) : Task.FromException<string>(Failure);
}
