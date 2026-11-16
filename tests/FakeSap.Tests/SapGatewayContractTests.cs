using BasisOps.Application.Ports;
using BasisOps.Domain.Observations;
using BasisOps.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FakeSap.Tests;

/// <summary>
/// The BasisOps SAP gateway (anti-corruption layer) against the simulator, through the same
/// resilience setup the hosts use (ServiceDefaults adds a default handler to every client).
/// </summary>
public class SapGatewayContractTests(FakeSapFactory fakeSap) : IClassFixture<FakeSapFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetJobRuns_Prd_TranslatesCanceledBillingJob()
    {
        using var sap = CreateGateway(fakeSap);

        var runs = await sap.Gateway.GetJobRunsAsync("PRD", Ct);

        var billing = Assert.Single(runs, r => r.JobName == "Z_BILLING_DAILY" && r.Status == JobRunStatus.Canceled);
        Assert.Equal(8, billing.RunId.Length);
        Assert.Equal(TimeSpan.Zero, billing.StartedAt.Offset);
    }

    [Fact]
    public async Task GetLockedUsers_Prd_TranslatesLockReasons()
    {
        using var sap = CreateGateway(fakeSap);

        var users = await sap.Gateway.GetLockedUsersAsync("PRD", Ct);

        Assert.Contains(users, u => u is { UserName: "MEHMET", Reason: LockReason.FailedLogons });
        Assert.Contains(users, u => u is { UserName: "JDOE", Reason: LockReason.Administrator });
    }

    [Fact]
    public async Task GetObservations_Prd_TranslatesDumpsTransportsCertificatesAndWorkProcesses()
    {
        using var sap = CreateGateway(fakeSap);

        var dumps = await sap.Gateway.GetShortDumpsAsync("PRD", DateTimeOffset.UtcNow.AddHours(-24), Ct);
        var transports = await sap.Gateway.GetTransportQueueAsync("PRD", Ct);
        var certificates = await sap.Gateway.GetCertificatesAsync("PRD", Ct);
        var workProcesses = await sap.Gateway.GetWorkProcessUsageAsync("PRD", Ct);

        Assert.True(dumps.Count(d => d.RuntimeError == "TIME_OUT") >= 6);
        Assert.Contains(transports, t => t.ReturnCode == 8);
        Assert.Contains(certificates, c => c.ValidTo < DateTimeOffset.UtcNow.AddDays(7));
        Assert.Contains(workProcesses, w => w.Type == WorkProcessType.Dialog);
    }

    [Fact]
    public async Task GetShortDump_ExistingAndUnknown_ReturnsDetailOrNull()
    {
        using var sap = CreateGateway(fakeSap);
        var dumpId = (await sap.Gateway.GetShortDumpsAsync("PRD", DateTimeOffset.UtcNow.AddHours(-24), Ct))[0].Id;

        var detail = await sap.Gateway.GetShortDumpAsync("PRD", dumpId, Ct);
        var unknown = await sap.Gateway.GetShortDumpAsync("PRD", "PRD-UNKNOWN-0000", Ct);

        Assert.NotNull(detail);
        Assert.False(string.IsNullOrWhiteSpace(detail.WhatHappened));
        Assert.Null(unknown);
    }

    [Fact]
    public async Task UnlockUser_CalledTwice_BothSucceed()
    {
        using var sap = CreateGateway(fakeSap);

        var first = await sap.Gateway.UnlockUserAsync("QAS", "MEHMET", Ct);
        var second = await sap.Gateway.UnlockUserAsync("QAS", "MEHMET", Ct);

        // The second call answers TYPE = W ("not locked"): the change is in place, so it is a success.
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task Actions_UnknownObjectOrWrongState_AreRejected()
    {
        using var sap = CreateGateway(fakeSap);
        var finished = (await sap.Gateway.GetJobRunsAsync("DEV", Ct)).First(r => r.Status == JobRunStatus.Finished);

        var unknownUser = await sap.Gateway.UnlockUserAsync("DEV", "NOBODY", Ct);
        var notCanceled = await sap.Gateway.RestartJobAsync("DEV", finished.JobName, finished.RunId, Ct);
        var unknownTransport = await sap.Gateway.ImportTransportAsync("DEV", "DEVK999999", Ct);

        Assert.Equal(SapActionStatus.Rejected, unknownUser.Status);
        Assert.Equal(SapActionStatus.Rejected, notCanceled.Status);
        Assert.Equal(SapActionStatus.Rejected, unknownTransport.Status);
    }

    [Fact]
    public async Task GetJobRuns_SapAlwaysFailing_RetriesOncePerAttemptThenThrowsUnavailable()
    {
        using var failing = fakeSap.WithWebHostBuilder(b => b.UseSetting("Chaos:FailurePercent", "100"));
        using var sap = CreateGateway(failing, maxRetryAttempts: 2);

        var ex = await Assert.ThrowsAsync<SapUnavailableException>(() => sap.Gateway.GetJobRunsAsync("PRD", Ct));

        // One pipeline only: 1 call + 2 retries. Two nested pipelines would send 9 requests.
        Assert.Equal(3, sap.Requests);
        Assert.False(ex.IsCircuitOpen);
    }

    [Fact]
    public async Task GetJobRuns_RepeatedFailures_OpensCircuit()
    {
        using var failing = fakeSap.WithWebHostBuilder(b => b.UseSetting("Chaos:FailurePercent", "100"));
        using var sap = CreateGateway(failing, maxRetryAttempts: 1, circuitMinimumThroughput: 2);

        await Assert.ThrowsAsync<SapUnavailableException>(() => sap.Gateway.GetJobRunsAsync("PRD", Ct));
        var requestsBeforeOpen = sap.Requests;
        var ex = await Assert.ThrowsAsync<SapUnavailableException>(() => sap.Gateway.GetJobRunsAsync("PRD", Ct));

        Assert.True(ex.IsCircuitOpen);
        Assert.Equal(requestsBeforeOpen, sap.Requests);
    }

    private static GatewayUnderTest CreateGateway(
        WebApplicationFactory<Program> server, int maxRetryAttempts = 3, int circuitMinimumThroughput = 10)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sap:BaseAddress"] = "http://localhost",
                ["Sap:MaxRetryAttempts"] = maxRetryAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Sap:RetryBaseDelay"] = "00:00:00.010",
                ["Sap:CircuitMinimumThroughput"] = circuitMinimumThroughput.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .Build();

        var counter = new CountingHandler(server.Server.CreateHandler());
        var services = new ServiceCollection();
        services.AddLogging();

        // Same as ServiceDefaults: a standard resilience handler on every client.
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddSapGateway(configuration).ConfigurePrimaryHttpMessageHandler(() => counter);

        var provider = services.BuildServiceProvider();
        return new GatewayUnderTest(provider, provider.GetRequiredService<ISapGateway>(), counter);
    }

    private sealed class GatewayUnderTest(ServiceProvider provider, ISapGateway gateway, CountingHandler counter) : IDisposable
    {
        public ISapGateway Gateway { get; } = gateway;

        public int Requests => counter.Count;

        public void Dispose() => provider.Dispose();
    }

    private sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
