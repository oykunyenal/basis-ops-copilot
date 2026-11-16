using BasisOps.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace BasisOps.IntegrationTests;

/// <summary>The real Api on a throwaway PostgreSQL container; SAP is replaced by <see cref="StubSapGateway"/>.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ApiKey = "integration-test-key-0123456789";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public StubSapGateway Sap { get; } = new();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public HttpClient CreateApiClient(string? apiKey = ApiKey)
    {
        var client = CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:basisops", _postgres.GetConnectionString());
        builder.UseSetting("Auth:ApiKey", ApiKey);
        builder.UseSetting("Database:MigrateOnStartup", "true");

        // Tests never depend on a local Ollama; the fallback test points at a closed port instead.
        builder.UseSetting("Ai:Enabled", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISapGateway>();
            services.AddSingleton<ISapGateway>(Sap);
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
