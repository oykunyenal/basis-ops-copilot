using System.Net;
using System.Net.Http.Json;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Findings;
using BasisOps.Application.Health;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SystemsApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListSystems_ConfiguredLandscape_ReturnsThreeSystems()
    {
        using var client = factory.CreateApiClient();

        var systems = await client.GetFromJsonAsync<List<SystemSummaryDto>>("/api/v1/systems", Json.Options, Ct);

        Assert.Equal(["DEV", "QAS", "PRD"], systems!.Select(s => s.Sid));
    }

    [Fact]
    public async Task ScanThenHealth_CanceledCriticalJobAndLockedUser_PersistsFindingsAndReportsCritical()
    {
        var now = DateTimeOffset.UtcNow;
        factory.Sap.Jobs.Add(new JobRun("Z_BILLING_DAILY", "00000001", JobRunStatus.Canceled, now.AddMinutes(-30), "BATCH_ADM"));
        factory.Sap.LockedUsers.Add(new LockedUser("MEHMET", LockReason.FailedLogons, now.AddMinutes(-10)));
        using var client = factory.CreateApiClient();

        using var scan = await client.PostAsync("/api/v1/systems/prd/scans", null, Ct);
        var health = await client.GetFromJsonAsync<SystemHealthDto>("/api/v1/systems/PRD/health", Json.Options, Ct);
        var critical = await client.GetFromJsonAsync<PagedResult<FindingDto>>("/api/v1/findings?sid=PRD&severity=Critical", Json.Options, Ct);

        // Scanning again must not duplicate open findings (partial unique index + reconciler).
        using var rescan = await client.PostAsync("/api/v1/systems/PRD/scans", null, Ct);
        var afterRescan = await client.GetFromJsonAsync<SystemHealthDto>("/api/v1/systems/PRD/health", Json.Options, Ct);

        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rescan.StatusCode);
        Assert.Equal(HealthStatus.Critical, health!.Status);
        Assert.NotNull(health.LastScan);
        Assert.Contains(health.OpenFindings, f => f.Category == FindingCategory.User);
        Assert.Single(critical!.Items, f => f.Category == FindingCategory.Job);
        Assert.Equal(health.OpenFindings.Count, afterRescan!.OpenFindings.Count);
    }

    [Fact]
    public async Task Health_UnknownSystem_Returns404()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/v1/systems/XYZ/health", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListFindings_PageSizeTooLarge_Returns400()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/v1/findings?pageSize=1000", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
