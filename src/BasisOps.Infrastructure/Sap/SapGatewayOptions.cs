using System.ComponentModel.DataAnnotations;

namespace BasisOps.Infrastructure.Sap;

public sealed class SapGatewayOptions
{
    public const string SectionName = "Sap";

    /// <summary>Resolved by Aspire service discovery ("fakesap" resource).</summary>
    [Required]
    public Uri BaseAddress { get; set; } = new("http://fakesap");

    [Range(1, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First retry delay; grows exponentially with jitter. A Retry-After header from SAP wins.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long calls are short-circuited after the breaker opens.</summary>
    public TimeSpan CircuitBreakDuration { get; set; } = TimeSpan.FromSeconds(15);

    [Range(2, 1000)]
    public int CircuitMinimumThroughput { get; set; } = 10;
}
