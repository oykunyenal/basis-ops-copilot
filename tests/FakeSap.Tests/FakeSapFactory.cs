using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FakeSap.Tests;

/// <summary>FakeSap host with the background event generator disabled, so tests see only seeded data.</summary>
public class FakeSapFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Simulation:EventIntervalSeconds", "0");
}
