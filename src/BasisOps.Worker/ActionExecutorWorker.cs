using System.ComponentModel.DataAnnotations;
using BasisOps.Application.Actions;
using Microsoft.Extensions.Options;

namespace BasisOps.Worker;

public sealed class ActionExecutorWorkerOptions
{
    public const string SectionName = "Executor";

    /// <summary>Wait between polls when no action is due.</summary>
    [Range(1, 300)]
    public int PollIntervalSeconds { get; set; } = 2;
}

/// <summary>Drains due approved actions, then polls. Safe to run in several instances (SKIP LOCKED).</summary>
internal sealed partial class ActionExecutorWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ActionExecutorWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<ActionExecutorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<ActionExecutor>().ExecuteNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down. An action left in Executing is picked up again after its lease expires.
                return;
            }
            catch (Exception ex)
            {
                LogLoopError(ex);
                processed = false;
            }

            if (!processed)
            {
                try
                {
                    await Task.Delay(pollInterval, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Action executor loop failed; retrying after the poll interval")]
    private partial void LogLoopError(Exception exception);
}
