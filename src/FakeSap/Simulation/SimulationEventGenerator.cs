using Microsoft.Extensions.Options;

namespace FakeSap.Simulation;

/// <summary>Periodically generates new dumps, failed logons, job runs and work process load.</summary>
internal sealed partial class SimulationEventGenerator(
    SapLandscape landscape,
    TimeProvider timeProvider,
    IOptions<SimulationOptions> options,
    ILogger<SimulationEventGenerator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = options.Value.EventIntervalSeconds;
        if (intervalSeconds <= 0)
        {
            LogDisabled();
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = timeProvider.GetUtcNow();
            foreach (var system in landscape.Systems)
            {
                system.Tick(now);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Simulation event generator is disabled")]
    private partial void LogDisabled();
}
