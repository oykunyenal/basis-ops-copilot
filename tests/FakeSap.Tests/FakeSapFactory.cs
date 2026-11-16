using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FakeSap.Tests;

/// <summary>
/// FakeSap host with the background event generator and chaos disabled, so tests see only seeded data
/// regardless of what appsettings.json says while someone is experimenting locally.
/// </summary>
public class FakeSapFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Simulation:EventIntervalSeconds", "0");
        builder.UseSetting("Chaos:FailurePercent", "0");
        builder.UseSetting("Chaos:RateLimitPercent", "0");
        builder.UseSetting("Chaos:LatencyMs", "0");
    }
}
