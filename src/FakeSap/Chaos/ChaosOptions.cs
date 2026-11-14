using System.ComponentModel.DataAnnotations;

namespace FakeSap.Chaos;

/// <summary>Fault injection for /sap/* endpoints. Changes in appsettings apply without a restart.</summary>
public sealed class ChaosOptions
{
    public const string SectionName = "Chaos";

    // Share of requests answered with 503 Service Unavailable.
    [Range(0, 100)]
    public int FailurePercent { get; set; }

    // Share of requests answered with 429 Too Many Requests + Retry-After.
    [Range(0, 100)]
    public int RateLimitPercent { get; set; }

    // Extra delay added to every request.
    [Range(0, 60_000)]
    public int LatencyMs { get; set; }

    [Range(1, 300)]
    public int RetryAfterSeconds { get; set; } = 2;
}
