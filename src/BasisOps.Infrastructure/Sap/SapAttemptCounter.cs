using System.Runtime.CompilerServices;

namespace BasisOps.Infrastructure.Sap;

/// <summary>
/// Sits inside the resilience pipeline and counts the requests that actually left for SAP during one gateway call.
/// A call can end with "circuit open" after a probe already reached SAP and failed; that attempt must count.
/// </summary>
internal sealed class SapAttemptCounter : DelegatingHandler
{
    private static readonly AsyncLocal<StrongBox<int>?> Current = new();

    /// <summary>Starts counting for the current gateway call (flows into the handler pipeline via the async context).</summary>
    public static StrongBox<int> Begin()
    {
        var sent = new StrongBox<int>();
        Current.Value = sent;
        return sent;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Current.Value is { } sent)
        {
            Interlocked.Increment(ref sent.Value);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
