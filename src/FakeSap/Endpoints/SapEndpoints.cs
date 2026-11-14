using FakeSap.Contracts;
using FakeSap.Simulation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FakeSap.Endpoints;

internal static partial class SapEndpoints
{
    private static readonly Dictionary<string, char> JobStatusFilter = new(StringComparer.OrdinalIgnoreCase)
    {
        ["canceled"] = JobStatus.Canceled,
        ["finished"] = JobStatus.Finished,
        ["active"] = JobStatus.Active,
        ["released"] = JobStatus.Released,
    };

    public static IEndpointRouteBuilder MapSapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sap/systems", (SapLandscape landscape) =>
            TypedResults.Ok(landscape.Systems.Select(s => s.ToSystemDto()).ToList()));

        var system = app.MapGroup("/sap/{sid}");

        system.MapGet("/jobs", GetJobs);
        system.MapGet("/dumps", GetDumps);
        system.MapGet("/dumps/{dumpId}", GetDump);
        system.MapGet("/users/locked", (string sid, SapLandscape landscape) => Read(landscape, sid, s => s.GetLockedUsers()));
        system.MapGet("/transports/queue", (string sid, SapLandscape landscape) => Read(landscape, sid, s => s.GetTransportQueue()));
        system.MapGet("/certificates", (string sid, SapLandscape landscape) => Read(landscape, sid, s => s.GetCertificates()));
        system.MapGet("/workprocesses", (string sid, SapLandscape landscape) => Read(landscape, sid, s => s.GetWorkProcesses()));

        system.MapPost("/users/{bname}/unlock", UnlockUser);
        system.MapPost("/jobs/{jobname}/{jobcount}/restart", RestartJob);
        system.MapPost("/transports/{trkorr}/import", ImportTransport);

        return app;
    }

    // SM37
    private static Results<Ok<IReadOnlyList<JobDto>>, ValidationProblem, ProblemHttpResult> GetJobs(
        string sid, string? status, SapLandscape landscape)
    {
        char? filter = null;
        if (status is not null)
        {
            if (!JobStatusFilter.TryGetValue(status, out var code))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Unknown status '{status}'. Allowed: {string.Join(", ", JobStatusFilter.Keys)}."],
                });
            }

            filter = code;
        }

        return landscape.Find(sid) is { } state
            ? TypedResults.Ok(state.GetJobs(filter))
            : SystemNotFound(sid);
    }

    // ST22. Without "since", returns the last 24 hours.
    private static Results<Ok<IReadOnlyList<DumpDto>>, ProblemHttpResult> GetDumps(
        string sid, DateTimeOffset? since, SapLandscape landscape, TimeProvider timeProvider)
    {
        var from = since ?? timeProvider.GetUtcNow().AddHours(-24);
        return landscape.Find(sid) is { } state
            ? TypedResults.Ok(state.GetDumps(from))
            : SystemNotFound(sid);
    }

    private static Results<Ok<DumpDetailDto>, ProblemHttpResult> GetDump(string sid, string dumpId, SapLandscape landscape)
    {
        if (landscape.Find(sid) is not { } state)
        {
            return SystemNotFound(sid);
        }

        return state.GetDump(dumpId) is { } dump
            ? TypedResults.Ok(dump)
            : NotFound("Dump not found", $"Dump '{dumpId}' does not exist in {state.Sid}.");
    }

    // BAPI_USER_UNLOCK
    private static Results<Ok<UserUnlockResponse>, ProblemHttpResult> UnlockUser(
        string sid, string bname, SapLandscape landscape, ILoggerFactory loggerFactory)
    {
        if (landscape.Find(sid) is not { } state)
        {
            return SystemNotFound(sid);
        }

        var userName = bname.ToUpperInvariant();
        var outcome = state.UnlockUser(userName);
        LogAction(loggerFactory.CreateLogger(nameof(SapEndpoints)), "unlock", state.Sid, userName, outcome.ToString());

        return outcome switch
        {
            UnlockOutcome.Unlocked => TypedResults.Ok(new UserUnlockResponse(new BapiReturn("S", "01", "190", $"User {userName} unlocked"))),
            UnlockOutcome.AlreadyUnlocked => TypedResults.Ok(new UserUnlockResponse(new BapiReturn("W", "01", "191", $"User {userName} is not locked"))),
            _ => NotFound("User not found", $"User '{userName}' does not exist in {state.Sid}."),
        };
    }

    // SM37 repeat scheduling (BAPI_XBP_JOB_COPY + BAPI_XBP_JOB_START_IMMEDIATELY)
    private static Results<Ok<JobRestartResponse>, ProblemHttpResult> RestartJob(
        string sid, string jobname, string jobcount, SapLandscape landscape, TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        if (landscape.Find(sid) is not { } state)
        {
            return SystemNotFound(sid);
        }

        var (outcome, newJobCount) = state.RestartJob(jobname, jobcount, timeProvider.GetUtcNow());
        LogAction(loggerFactory.CreateLogger(nameof(SapEndpoints)), "restart", state.Sid, $"{jobname}/{jobcount}", outcome.ToString());

        return outcome switch
        {
            RestartOutcome.Restarted => TypedResults.Ok(new JobRestartResponse(
                new BapiReturn("S", "XM", "023", $"Job {jobname} restarted as {newJobCount}"), newJobCount!)),
            RestartOutcome.AlreadyRestarted => TypedResults.Ok(new JobRestartResponse(
                new BapiReturn("W", "XM", "024", $"Job {jobname}/{jobcount} was already restarted as {newJobCount}"), newJobCount!)),
            RestartOutcome.NotCanceled => TypedResults.Problem(
                title: "Job is not canceled",
                detail: $"Only canceled jobs can be restarted. Job {jobname}/{jobcount} is not canceled.",
                statusCode: StatusCodes.Status409Conflict),
            _ => NotFound("Job not found", $"Job {jobname}/{jobcount} does not exist in {state.Sid}."),
        };
    }

    // STMS import (TMS_MGR_IMPORT_TR_REQUEST)
    private static Results<Ok<TransportImportResponse>, ProblemHttpResult> ImportTransport(
        string sid, string trkorr, SapLandscape landscape, ILoggerFactory loggerFactory)
    {
        if (landscape.Find(sid) is not { } state)
        {
            return SystemNotFound(sid);
        }

        var transport = trkorr.ToUpperInvariant();
        var (outcome, returnCode) = state.ImportTransport(transport);
        LogAction(loggerFactory.CreateLogger(nameof(SapEndpoints)), "import", state.Sid, transport, outcome.ToString());

        return outcome switch
        {
            ImportOutcome.Imported => TypedResults.Ok(new TransportImportResponse(
                new BapiReturn("S", "TP", "000", $"Transport {transport} imported into {state.Sid}"), returnCode)),
            ImportOutcome.AlreadyImported => TypedResults.Ok(new TransportImportResponse(
                new BapiReturn("W", "TP", "001", $"Transport {transport} is already imported into {state.Sid}"), returnCode)),
            _ => NotFound("Transport not found", $"Transport {transport} is not in the import queue of {state.Sid}."),
        };
    }

    private static Results<Ok<IReadOnlyList<T>>, ProblemHttpResult> Read<T>(
        SapLandscape landscape, string sid, Func<SapSystemState, IReadOnlyList<T>> read) =>
        landscape.Find(sid) is { } state
            ? TypedResults.Ok(read(state))
            : SystemNotFound(sid);

    private static ProblemHttpResult SystemNotFound(string sid) =>
        NotFound("System not found", $"System '{sid}' is not part of the landscape. Known systems: DEV, QAS, PRD.");

    private static ProblemHttpResult NotFound(string title, string detail) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status404NotFound);

    [LoggerMessage(Level = LogLevel.Information, Message = "SAP action {Action} on {Sid} for {Target}: {Outcome}")]
    private static partial void LogAction(ILogger logger, string action, string sid, string target, string outcome);
}
