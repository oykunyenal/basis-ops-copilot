using System.ComponentModel.DataAnnotations;

namespace BasisOps.Application.Actions;

public sealed class ActionExecutorOptions
{
    public const string SectionName = "Executor";

    /// <summary>Attempts that reached SAP. Attempts skipped by an open circuit breaker do not count.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Delay before the second attempt; doubles per attempt (with jitter) up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long to wait when SAP calls are paused by the circuit breaker.</summary>
    public TimeSpan CircuitOpenDelay { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>An action in Executing for longer than this is considered abandoned (executor crashed) and retried.</summary>
    public TimeSpan ExecutionLease { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Exponential backoff with ±20% jitter, so retries of many actions do not hit SAP at the same moment.</summary>
    public TimeSpan RetryDelayAfter(int attempt)
    {
        var exponential = RetryBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Max(0, attempt - 1));
        var capped = Math.Min(exponential, MaxRetryDelay.TotalMilliseconds);
        var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromMilliseconds(capped * jitter);
    }
}
