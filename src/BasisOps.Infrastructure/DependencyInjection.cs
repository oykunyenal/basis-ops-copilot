using BasisOps.Application.Ports;
using BasisOps.Infrastructure.Ai;
using BasisOps.Infrastructure.Persistence;
using BasisOps.Infrastructure.Sap;
using BasisOps.Infrastructure.Systems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace BasisOps.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the Aspire database resource and of the connection string.</summary>
    public const string DatabaseName = "basisops";

    /// <summary>Activity source of the LLM calls (matched by the "BasisOps.*" tracing source in ServiceDefaults).</summary>
    public const string AiTelemetrySource = "BasisOps.Ai";

    private const string OllamaHttpClientName = "ollama";

    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<BasisOpsDbContext>(DatabaseName, configureDbContextOptions: BasisOpsDbContext.Configure);

        var services = builder.Services;
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BasisOpsDbContext>());
        services.AddScoped<IFindingRepository, FindingRepository>();
        services.AddScoped<IActionRequestRepository, ActionRequestRepository>();
        services.AddScoped<IHealthScanRepository, HealthScanRepository>();
        services.AddScoped<IAuditLog, AuditLog>();

        services.AddOptions<LandscapeOptions>()
            .Bind(builder.Configuration.GetSection(LandscapeOptions.SectionName))
            .Validate(o => o.IsValid(), "Landscape:Systems must list at least one system with a unique, valid SID.")
            .ValidateOnStart();
        services.AddSingleton<ISystemRegistry, ConfiguredSystemRegistry>();

        services.AddSapGateway(builder.Configuration);

        services.AddAiAnalyzer(builder.Configuration);

        return builder;
    }

    /// <summary>
    /// Typed client for SAP. ServiceDefaults adds a standard resilience handler to every client;
    /// it is replaced here by one pipeline tuned for SAP (retry with jitter, circuit breaker, timeouts).
    /// </summary>
    public static IHttpClientBuilder AddSapGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SapGatewayOptions>()
            .Bind(configuration.GetSection(SapGatewayOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddTransient<SapAttemptCounter>();
        var client = services.AddHttpClient<ISapGateway, FakeSapGateway>((sp, http) =>
            http.BaseAddress = sp.GetRequiredService<IOptions<SapGatewayOptions>>().Value.BaseAddress);

        // Without removing the default handler there would be two nested pipelines (retries multiplied).
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is marked experimental; it is the documented way to override the default handler.
        client.RemoveAllResilienceHandlers()
#pragma warning restore EXTEXP0001
            .AddStandardResilienceHandler()
            .Configure((resilience, sp) =>
            {
                var sap = sp.GetRequiredService<IOptions<SapGatewayOptions>>().Value;
                resilience.Retry.MaxRetryAttempts = sap.MaxRetryAttempts;
                resilience.Retry.Delay = sap.RetryBaseDelay;
                resilience.AttemptTimeout.Timeout = sap.AttemptTimeout;
                resilience.TotalRequestTimeout.Timeout = sap.TotalTimeout;
                resilience.CircuitBreaker.BreakDuration = sap.CircuitBreakDuration;
                resilience.CircuitBreaker.MinimumThroughput = sap.CircuitMinimumThroughput;
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(30).Ticks, sap.AttemptTimeout.Ticks * 2));
            });

        // Added after the resilience handler, so it runs once per attempt that is actually sent.
        client.AddHttpMessageHandler<SapAttemptCounter>();

        return client;
    }

    /// <summary>
    /// LLM analysis on the local Ollama server when <c>Ai:Enabled</c> is true; otherwise a disabled analyzer,
    /// so reports are rule-based. One attempt per call with its own timeout: an LLM call is slow and not worth
    /// retrying, and the default resilience handler (10 s per attempt) would cut every generation short.
    /// </summary>
    public static IServiceCollection AddAiAnalyzer(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AiOptions.SectionName);
        services.AddOptions<AiOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (!section.GetValue<bool>(nameof(AiOptions.Enabled)))
        {
            services.TryAddSingleton<IAiAnalyzer, DisabledAiAnalyzer>();
            return services;
        }

        var client = services.AddHttpClient(OllamaHttpClientName, (sp, http) =>
        {
            http.BaseAddress = sp.GetRequiredService<IOptions<AiOptions>>().Value.Endpoint;
            http.Timeout = Timeout.InfiniteTimeSpan; // AiOptions.Timeout applies per call
        });
#pragma warning disable EXTEXP0001 // See AddSapGateway.
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        services.AddChatClient(
                sp => new OllamaApiClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(OllamaHttpClientName),
                    sp.GetRequiredService<IOptions<AiOptions>>().Value.Model),
                ServiceLifetime.Scoped)
            .UseOpenTelemetry(sourceName: AiTelemetrySource); // prompts and answers are not recorded

        services.AddScoped<IAiAnalyzer, OllamaAiAnalyzer>();
        return services;
    }

    /// <summary>Applies pending EF Core migrations. Meant for local development; production runs a migration bundle.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BasisOpsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
