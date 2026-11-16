using BasisOps.Api.Http;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Dumps;

namespace BasisOps.Api.Endpoints;

internal static class DumpEndpoints
{
    public static void MapDumpEndpoints(this RouteGroupBuilder api)
    {
        // Dump ids are only unique within one system (ST22), so dumps live under the system.
        var dumps = api.MapGroup("/systems/{sid}/dumps").WithTags("Dumps");

        dumps.MapGet("/", async (string sid, int? hours, IQueryHandler<ListDumpsQuery, IReadOnlyList<DumpDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListDumpsQuery(ApiRoutes.NormalizeSid(sid), hours ?? 24), ct)).ToHttpResult())
            .WithSummary("Short dumps (ST22) of the last hours, newest first.")
            .Produces<IReadOnlyList<DumpDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        dumps.MapPost("/{dumpId}/analyze", async (string sid, string dumpId, ICommandHandler<AnalyzeDumpCommand, DumpAnalysisDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new AnalyzeDumpCommand(ApiRoutes.NormalizeSid(sid), dumpId), ct)).ToHttpResult())
            .WithSummary("Analyses one short dump with the local LLM. User names, hosts and IPs are masked first; "
                + "without the LLM the dump is returned with aiAvailable = false.")
            .Produces<DumpAnalysisDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}
