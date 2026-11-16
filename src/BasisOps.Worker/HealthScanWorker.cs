using System.ComponentModel.DataAnnotations;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Health;
using BasisOps.Application.Ports;
using Microsoft.Extensions.Options;

namespace BasisOps.Worker;

public sealed class HealthScanOptions
{
    public const string SectionName = "HealthScan";

    [Range(5, 86_400)]
    public int ScanIntervalSeconds { get; set; } = 60;
}

/// <summary>Scans every managed system right after start and then every <see cref="HealthScanOptions.ScanIntervalSeconds"/>.</summary>
internal sealed partial class HealthScanWorker(
    IServiceScopeFactory scopeFactory,
    ISystemRegistry systems,
    IOptions<HealthScanOptions> options,
    TimeProvider timeProvider,
    ILogger<HealthScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.ScanIntervalSeconds);
        LogStarted(interval);

        using var timer = new PeriodicTimer(interval, timeProvider);
        do
        {
            foreach (var system in await systems.ListAsync(stoppingToken))
            {
                await ScanAsync(system.Sid, stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ScanAsync(string sid, CancellationToken stoppingToken)
    {
        try
        {
            // DbContext and repositories are scoped: one scope per scan.
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunHealthScanCommand, HealthScanResult>>();
            await handler.HandleAsync(new RunHealthScanCommand(sid), stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // One failing system must not stop the scans of the others; the next tick tries again.
            LogScanError(ex, sid);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Health scans every {Interval}")]
    private partial void LogStarted(TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Health scan of {Sid} failed")]
    private partial void LogScanError(Exception exception, string sid);
}
