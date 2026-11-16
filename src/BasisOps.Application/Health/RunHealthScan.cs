using System.Diagnostics;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;
using BasisOps.Domain.Rules;
using BasisOps.Domain.Scans;
using BasisOps.Domain.Systems;
using Microsoft.Extensions.Logging;

namespace BasisOps.Application.Health;

public sealed record RunHealthScanCommand(string Sid);

public sealed record HealthScanResult(Guid ScanId, string Sid, HealthScanStatus Status, int Opened, int Updated, int Resolved, string? Error);

/// <summary>Collects a snapshot from SAP, runs all health rules and reconciles the findings.</summary>
public sealed partial class RunHealthScanHandler(
    ISystemRegistry systems,
    ISapGateway sap,
    IEnumerable<IHealthRule> rules,
    IFindingRepository findings,
    IHealthScanRepository scans,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<RunHealthScanHandler> logger) : ICommandHandler<RunHealthScanCommand, HealthScanResult>
{
    // ST22 lookback for every scan. Rules use shorter windows on top of this.
    private static readonly TimeSpan DumpLookback = TimeSpan.FromHours(24);

    public async Task<Result<HealthScanResult>> HandleAsync(RunHealthScanCommand command, CancellationToken cancellationToken)
    {
        if (await systems.FindAsync(command.Sid, cancellationToken) is not { } system)
        {
            return Error.NotFound("system_not_found", $"System {command.Sid} is not managed.");
        }

        using var activity = Telemetry.Source.StartActivity("health-scan");
        activity?.SetTag("sap.sid", system.Sid);

        var startedAt = timeProvider.GetUtcNow();

        SystemSnapshot snapshot;
        try
        {
            snapshot = await CaptureAsync(system, startedAt, cancellationToken);
        }
        catch (SapUnavailableException ex)
        {
            // A failed scan is recorded, not thrown: findings stay as they are until SAP answers again.
            LogScanFailed(system.Sid, ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var failed = HealthScan.Failed(system.Sid, startedAt, timeProvider.GetUtcNow(), ex.Message);
            scans.Add(failed);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new HealthScanResult(failed.Id, system.Sid, failed.Status, 0, 0, 0, ex.Message);
        }

        var candidates = rules.SelectMany(rule => rule.Evaluate(snapshot)).ToList();
        var open = await findings.GetOpenAsync(system.Sid, cancellationToken);
        var result = FindingReconciler.Reconcile(system.Sid, open, candidates, startedAt);

        findings.AddRange(result.Opened);
        var scan = HealthScan.Succeeded(system.Sid, startedAt, timeProvider.GetUtcNow(), candidates.Count);
        scans.Add(scan);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("findings.opened", result.Opened.Count);
        activity?.SetTag("findings.resolved", result.Resolved.Count);
        LogScanCompleted(system.Sid, result.Opened.Count, result.Updated.Count, result.Resolved.Count);
        return new HealthScanResult(scan.Id, system.Sid, scan.Status, result.Opened.Count, result.Updated.Count, result.Resolved.Count, null);
    }

    private async Task<SystemSnapshot> CaptureAsync(SapSystem system, DateTimeOffset now, CancellationToken ct)
    {
        var sid = system.Sid;
        var jobs = sap.GetJobRunsAsync(sid, ct);
        var dumps = sap.GetShortDumpsAsync(sid, now - DumpLookback, ct);
        var users = sap.GetLockedUsersAsync(sid, ct);
        var transports = sap.GetTransportQueueAsync(sid, ct);
        var certificates = sap.GetCertificatesAsync(sid, ct);
        var workProcesses = sap.GetWorkProcessUsageAsync(sid, ct);

        await Task.WhenAll(jobs, dumps, users, transports, certificates, workProcesses);

        return new SystemSnapshot(system, now, await jobs, await dumps, await users, await transports, await certificates, await workProcesses);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Health scan of {Sid} completed: {Opened} opened, {Updated} updated, {Resolved} resolved")]
    private partial void LogScanCompleted(string sid, int opened, int updated, int resolved);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Health scan of {Sid} failed: {Reason}")]
    private partial void LogScanFailed(string sid, string reason);
}
