using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasisOps.Application.Ports;
using BasisOps.Domain.Observations;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BasisOps.Infrastructure.Sap;

/// <summary>
/// <see cref="ISapGateway"/> over the FakeSap HTTP API. Retries, timeouts and the circuit breaker come from the
/// resilience pipeline of the typed client; this class only translates results and failures.
/// </summary>
internal sealed class FakeSapGateway(HttpClient http) : ISapGateway
{
    public Task<IReadOnlyList<JobRun>> GetJobRunsAsync(string sid, CancellationToken cancellationToken) =>
        GetListAsync<SapJob, JobRun>($"sap/{Segment(sid)}/jobs", SapTranslator.ToJobRun, cancellationToken);

    public Task<IReadOnlyList<ShortDump>> GetShortDumpsAsync(string sid, DateTimeOffset since, CancellationToken cancellationToken) =>
        GetListAsync<SapDump, ShortDump>(
            $"sap/{Segment(sid)}/dumps?since={Uri.EscapeDataString(since.UtcDateTime.ToString("O"))}",
            SapTranslator.ToShortDump,
            cancellationToken);

    public async Task<ShortDumpDetail?> GetShortDumpAsync(string sid, string dumpId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"sap/{Segment(sid)}/dumps/{Segment(dumpId)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var dump = await response.Content.ReadFromJsonAsync<SapDump>(cancellationToken)
            ?? throw new InvalidOperationException("SAP returned an empty dump.");
        return SapTranslator.ToShortDumpDetail(dump);
    }

    public Task<IReadOnlyList<LockedUser>> GetLockedUsersAsync(string sid, CancellationToken cancellationToken) =>
        GetListAsync<SapLockedUser, LockedUser>($"sap/{Segment(sid)}/users/locked", SapTranslator.ToLockedUser, cancellationToken);

    public Task<IReadOnlyList<TransportRequest>> GetTransportQueueAsync(string sid, CancellationToken cancellationToken) =>
        GetListAsync<SapTransport, TransportRequest>($"sap/{Segment(sid)}/transports/queue", SapTranslator.ToTransportRequest, cancellationToken);

    public Task<IReadOnlyList<Certificate>> GetCertificatesAsync(string sid, CancellationToken cancellationToken) =>
        GetListAsync<SapCertificate, Certificate>($"sap/{Segment(sid)}/certificates", SapTranslator.ToCertificate, cancellationToken);

    public async Task<IReadOnlyList<WorkProcessUsage>> GetWorkProcessUsageAsync(string sid, CancellationToken cancellationToken)
    {
        var all = await GetListAsync<SapWorkProcess, WorkProcessUsage?>($"sap/{Segment(sid)}/workprocesses", SapTranslator.ToWorkProcessUsage, cancellationToken);
        return [.. all.OfType<WorkProcessUsage>()];
    }

    public Task<SapActionOutcome> UnlockUserAsync(string sid, string userName, CancellationToken cancellationToken) =>
        PostActionAsync($"sap/{Segment(sid)}/users/{Segment(userName)}/unlock", cancellationToken);

    public Task<SapActionOutcome> RestartJobAsync(string sid, string jobName, string runId, CancellationToken cancellationToken) =>
        PostActionAsync($"sap/{Segment(sid)}/jobs/{Segment(jobName)}/{Segment(runId)}/restart", cancellationToken);

    public Task<SapActionOutcome> ImportTransportAsync(string sid, string transportNumber, CancellationToken cancellationToken) =>
        PostActionAsync($"sap/{Segment(sid)}/transports/{Segment(transportNumber)}/import", cancellationToken);

    private async Task<IReadOnlyList<TDomain>> GetListAsync<TSap, TDomain>(
        string path, Func<TSap, TDomain> translate, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<TSap>>(cancellationToken) ?? [];
        return [.. items.Select(translate)];
    }

    // The SAP actions are idempotent (a repeated unlock answers TYPE = W), so retrying a POST is safe.
    private async Task<SapActionOutcome> PostActionAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, path, cancellationToken);
        ThrowIfTransient(response);

        if (!response.IsSuccessStatusCode)
        {
            // 404 (unknown object) or 409 (wrong state): retrying will not help.
            return new SapActionOutcome(SapActionStatus.Rejected, await ReadProblemAsync(response, cancellationToken));
        }

        var body = await response.Content.ReadFromJsonAsync<SapActionResponse>(cancellationToken)
            ?? throw new InvalidOperationException("SAP returned an empty action response.");
        return SapTranslator.ToOutcome(body.Return);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        var sent = SapAttemptCounter.Begin();
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (BrokenCircuitException ex) when (sent.Value == 0)
        {
            throw new SapUnavailableException("SAP calls are paused because the circuit breaker is open.", isCircuitOpen: true, ex);
        }
        catch (BrokenCircuitException ex)
        {
            // A request reached SAP and failed; the breaker opened before the retries were used up.
            throw new SapUnavailableException("SAP failed and the circuit breaker opened.", innerException: ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new SapUnavailableException("SAP did not answer in time.", innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SapUnavailableException($"SAP could not be reached: {ex.Message}", innerException: ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SapUnavailableException("SAP did not answer in time.", innerException: ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ThrowIfTransient(response);
        if (!response.IsSuccessStatusCode)
        {
            // Not transient: a wrong SID or path is a configuration error, not an outage.
            throw new InvalidOperationException(
                $"SAP request {response.RequestMessage?.RequestUri?.PathAndQuery} failed with {(int)response.StatusCode}: {await ReadProblemAsync(response, cancellationToken)}");
        }
    }

    // Still failing after the retries of the resilience pipeline.
    private static void ThrowIfTransient(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500)
        {
            throw new SapUnavailableException($"SAP answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // Not a ProblemDetails body; fall back to the status code.
        }

        return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
    }

    private static string Segment(string value) => Uri.EscapeDataString(value);
}
