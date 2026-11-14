using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FakeSap.Tests;

public class ChaosTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SapRequest_FailurePercent100_Returns503()
    {
        using var factory = CreateFactory(("Chaos:FailurePercent", "100"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sap/PRD/jobs", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task SapRequest_RateLimitPercent100_Returns429WithRetryAfter()
    {
        using var factory = CreateFactory(("Chaos:RateLimitPercent", "100"), ("Chaos:RetryAfterSeconds", "7"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sap/PRD/jobs", Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(7), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task NonSapRequest_FailurePercent100_IsNotAffected()
    {
        using var factory = CreateFactory(("Chaos:FailurePercent", "100"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings) =>
        new FakeSapFactory().WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
}
