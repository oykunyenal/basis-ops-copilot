using System.Net;

namespace BasisOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ApiKeyTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApiRequest_MissingKey_Returns401()
    {
        using var client = factory.CreateApiClient(apiKey: null);

        using var response = await client.GetAsync("/api/v1/systems", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiRequest_WrongKey_Returns401()
    {
        using var client = factory.CreateApiClient(apiKey: "wrong-key-wrong-key");

        using var response = await client.GetAsync("/api/v1/systems", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Root_NoKey_ReturnsOk()
    {
        using var client = factory.CreateApiClient(apiKey: null);

        using var response = await client.GetAsync("/", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_Development_DescribesApiKeyScheme()
    {
        using var client = factory.CreateApiClient(apiKey: null);

        var document = await client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.Contains("X-Api-Key", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/actions", document, StringComparison.Ordinal);
    }
}
