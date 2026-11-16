using System.Diagnostics;

namespace BasisOps.Application;

/// <summary>Trace source for scans and action executions. Hosts register it with OpenTelemetry.</summary>
public static class Telemetry
{
    public const string SourceName = "BasisOps.Application";

    internal static readonly ActivitySource Source = new(SourceName);
}
