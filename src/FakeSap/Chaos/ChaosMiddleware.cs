using Microsoft.Extensions.Options;

namespace FakeSap.Chaos;

/// <summary>Injects latency, rate limiting (429) and failures (503) into /sap/* requests.</summary>
internal sealed partial class ChaosMiddleware(
    RequestDelegate next,
    IOptionsMonitor<ChaosOptions> options,
    TimeProvider timeProvider,
    ILogger<ChaosMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/sap", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var chaos = options.CurrentValue;

        if (chaos.LatencyMs > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(chaos.LatencyMs), timeProvider, context.RequestAborted);
        }

        if (Hit(chaos.RateLimitPercent))
        {
            LogInjected(StatusCodes.Status429TooManyRequests, context.Request.Path);
            context.Response.Headers.RetryAfter = chaos.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Results.Problem(
                    title: "Too many requests",
                    detail: "Simulated rate limit (chaos).",
                    statusCode: StatusCodes.Status429TooManyRequests)
                .ExecuteAsync(context);
            return;
        }

        if (Hit(chaos.FailurePercent))
        {
            LogInjected(StatusCodes.Status503ServiceUnavailable, context.Request.Path);
            await Results.Problem(
                    title: "SAP system unavailable",
                    detail: "Simulated outage (chaos).",
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool Hit(int percent) => percent > 0 && Random.Shared.Next(100) < percent;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Chaos injected {StatusCode} for {Path}")]
    private partial void LogInjected(int statusCode, PathString path);
}
