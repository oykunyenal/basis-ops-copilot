using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FakeSap.Tests;

public class SapEndpointTests(FakeSapFactory factory) : IClassFixture<FakeSapFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetSystems_Default_ReturnsThreeSystemsWithProductionCategory()
    {
        var systems = await GetArray("/sap/systems");

        Assert.Equal(["DEV", "QAS", "PRD"], systems.Select(s => s.GetProperty("SID").GetString()));
        Assert.Equal("P", systems.Single(s => s.GetProperty("SID").GetString() == "PRD").GetProperty("CCCATEGORY").GetString());
    }

    [Fact]
    public async Task GetJobs_UnknownSystem_ReturnsNotFoundProblem()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sap/XYZ/jobs", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetJobs_InvalidStatus_ReturnsBadRequest()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sap/PRD/jobs?status=broken", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetJobs_CanceledInProduction_ContainsBillingJobWithSapFieldNames()
    {
        var jobs = await GetArray("/sap/PRD/jobs?status=canceled");

        Assert.All(jobs, j => Assert.Equal("A", j.GetProperty("STATUS").GetString()));
        Assert.Contains(jobs, j => j.GetProperty("JOBNAME").GetString() == "Z_BILLING_DAILY");
    }

    [Fact]
    public async Task GetDumps_SinceOneHourAgo_ContainsTimeoutBurstInProduction()
    {
        var since = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));

        var dumps = await GetArray($"/sap/PRD/dumps?since={since}");

        Assert.True(dumps.Count(d => d.GetProperty("ERRORID").GetString() == "TIME_OUT") >= 5);
    }

    [Fact]
    public async Task GetDumpDetail_ExistingDump_ReturnsAnalysisText()
    {
        var dumpId = (await GetArray("/sap/PRD/dumps"))[0].GetProperty("DUMPID").GetString();
        using var client = factory.CreateClient();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/sap/PRD/dumps/{dumpId}", Ct);

        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("WHAT_HAPPENED").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("ERROR_ANALYSIS").GetString()));
    }

    [Fact]
    public async Task UnlockUser_CalledTwice_SecondCallReturnsWarningAndUserIsUnlocked()
    {
        using var client = factory.CreateClient();
        // QAS is used so this test does not change the PRD data other tests rely on.
        var locked = await GetArray("/sap/QAS/users/locked");
        var userName = locked.Length > 0
            ? locked[0].GetProperty("BNAME").GetString()!
            : "CAN";

        var first = await client.PostAsync($"/sap/QAS/users/{userName}/unlock", null, Ct);
        var second = await client.PostAsync($"/sap/QAS/users/{userName}/unlock", null, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("W", secondBody.GetProperty("RETURN").GetProperty("TYPE").GetString());
        Assert.DoesNotContain(await GetArray("/sap/QAS/users/locked"), u => u.GetProperty("BNAME").GetString() == userName);
    }

    [Fact]
    public async Task UnlockUser_UnknownUser_ReturnsNotFound()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/sap/PRD/users/NOBODY/unlock", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RestartJob_CalledTwice_ReturnsSameNewJobCount()
    {
        using var client = factory.CreateClient();
        // PRD is avoided so this test does not change the data other tests rely on.
        var (sid, job) = await FirstCanceledJob("DEV", "QAS");
        var url = RestartUrl(sid, job);

        var first = await client.PostAsync(url, null, Ct);
        var second = await client.PostAsync(url, null, Ct);

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("S", firstBody.GetProperty("RETURN").GetProperty("TYPE").GetString());
        Assert.Equal(firstBody.GetProperty("NEW_JOBCOUNT").GetString(), secondBody.GetProperty("NEW_JOBCOUNT").GetString());
    }

    [Fact]
    public async Task RestartJob_FinishedJob_ReturnsConflict()
    {
        using var client = factory.CreateClient();
        var job = (await GetArray("/sap/PRD/jobs?status=finished"))[0];

        var response = await client.PostAsync(RestartUrl("PRD", job), null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ImportTransport_FailedTransport_ImportsWithReturnCodeZero()
    {
        using var client = factory.CreateClient();
        var failed = (await GetArray("/sap/QAS/transports/queue")).First(t => t.GetProperty("RETCODE").ValueKind == JsonValueKind.Number && t.GetProperty("RETCODE").GetInt32() >= 8);
        var trkorr = failed.GetProperty("TRKORR").GetString();

        var response = await client.PostAsync($"/sap/QAS/transports/{trkorr}/import", null, Ct);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(0, body.GetProperty("RETCODE").GetInt32());
    }

    [Fact]
    public async Task GetCertificates_Production_HasCertificateExpiringWithinSevenDays()
    {
        var certificates = await GetArray("/sap/PRD/certificates");
        var limit = DateTimeOffset.UtcNow.AddDays(7).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains(certificates, c => string.CompareOrdinal(c.GetProperty("VALID_TO").GetString(), limit) <= 0);
    }

    [Fact]
    public async Task GetWorkProcesses_Production_BusyNeverExceedsTotal()
    {
        var groups = await GetArray("/sap/PRD/workprocesses");

        Assert.Contains(groups, g => g.GetProperty("TYP").GetString() == "DIA");
        Assert.All(groups, g => Assert.InRange(g.GetProperty("BUSY").GetInt32(), 0, g.GetProperty("TOTAL").GetInt32()));
    }

    private static string RestartUrl(string sid, JsonElement job) =>
        $"/sap/{sid}/jobs/{job.GetProperty("JOBNAME").GetString()}/{job.GetProperty("JOBCOUNT").GetString()}/restart";

    private async Task<(string Sid, JsonElement Job)> FirstCanceledJob(params string[] sids)
    {
        foreach (var sid in sids)
        {
            var jobs = await GetArray($"/sap/{sid}/jobs?status=canceled");
            if (jobs.Length > 0)
            {
                return (sid, jobs[0]);
            }
        }

        throw new InvalidOperationException("Seed data contains no canceled job outside PRD.");
    }

    private async Task<JsonElement[]> GetArray(string url)
    {
        using var client = factory.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>(url, Ct);
        return [.. json.EnumerateArray()];
    }
}
