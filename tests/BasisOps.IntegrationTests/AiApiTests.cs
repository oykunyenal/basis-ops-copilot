using System.Net;
using System.Net.Http.Json;
using BasisOps.Application.Dumps;
using BasisOps.Application.Ports;
using BasisOps.Application.Reports;
using BasisOps.Domain.Observations;
using BasisOps.Infrastructure.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BasisOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AiApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DailyReport_OllamaUnreachable_ReturnsRuleBasedReportWithoutAi()
    {
        await using var withAi = WithUnreachableOllama();
        using var client = withAi.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ApiKey);

        using var response = await client.GetAsync("/api/v1/reports/daily?sid=prd", Ct);
        var report = await response.ReadAsync<DailyReportDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PRD", report.Sid);
        Assert.False(report.AiAvailable);
        Assert.Null(report.AiSummary);

        // The fallback comes from a failed Ollama call, not from the disabled analyzer.
        await using var scope = withAi.Services.CreateAsyncScope();
        Assert.IsType<OllamaAiAnalyzer>(scope.ServiceProvider.GetRequiredService<IAiAnalyzer>());
    }

    [Fact]
    public async Task AnalyzeDump_OllamaUnreachable_ReturnsDumpWithoutAnalysis()
    {
        var id = $"PRD-{Guid.NewGuid():N}"[..20];
        factory.Sap.Dumps[id] = new ShortDumpDetail(
            new ShortDump(id, "TIME_OUT", "Z_SALES_REPORT", "MEHMET", "prdapp01", DateTimeOffset.UtcNow.AddMinutes(-5), "Time limit exceeded."),
            "User MEHMET, application server prdapp01.",
            "Long running SELECT.");
        await using var withAi = WithUnreachableOllama();
        using var client = withAi.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ApiKey);

        using var response = await client.PostAsync($"/api/v1/systems/PRD/dumps/{id}/analyze", null, Ct);
        var result = await response.ReadAsync<DumpAnalysisDto>(Ct);
        var listed = await client.GetFromJsonAsync<List<DumpDto>>("/api/v1/systems/PRD/dumps?hours=1", Json.Options, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result.AiAvailable);
        Assert.Null(result.Analysis);
        Assert.Equal("TIME_OUT", result.Dump.RuntimeError);
        Assert.Contains(listed!, d => d.Id == id);
    }

    [Fact]
    public async Task AnalyzeDump_UnknownDump_Returns404()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsync("/api/v1/systems/PRD/dumps/NOPE/analyze", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("dump_not_found", await response.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task ListDumps_HoursOutOfRange_Returns400()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/v1/systems/PRD/dumps?hours=0", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Port 9 (discard) is closed on test machines: the connection is refused at once, like a stopped Ollama.
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithUnreachableOllama() =>
        factory.WithWebHostBuilder(builder => builder
            .UseSetting("Ai:Enabled", "true")
            .UseSetting("Ai:Endpoint", "http://127.0.0.1:9"));
}
