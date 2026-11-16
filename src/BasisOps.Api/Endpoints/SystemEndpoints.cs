using BasisOps.Api.Http;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Health;
using BasisOps.Application.Users;

namespace BasisOps.Api.Endpoints;

internal static class SystemEndpoints
{
    public static void MapSystemEndpoints(this RouteGroupBuilder api)
    {
        var systems = api.MapGroup("/systems").WithTags("Systems");

        systems.MapGet("/", async (IQueryHandler<ListSystemsQuery, IReadOnlyList<SystemSummaryDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListSystemsQuery(), ct)).ToHttpResult())
            .WithSummary("Managed SAP systems with their current health status.")
            .Produces<IReadOnlyList<SystemSummaryDto>>();

        systems.MapGet("/{sid}/health", async (string sid, IQueryHandler<GetSystemHealthQuery, SystemHealthDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetSystemHealthQuery(ApiRoutes.NormalizeSid(sid)), ct)).ToHttpResult())
            .WithSummary("Health of one system: status, open findings and the last scan.")
            .Produces<SystemHealthDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        systems.MapPost("/{sid}/scans", async (string sid, ICommandHandler<RunHealthScanCommand, HealthScanResult> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RunHealthScanCommand(ApiRoutes.NormalizeSid(sid)), ct)).ToHttpResult())
            .WithSummary("Runs a health scan now (the worker also scans periodically).")
            .Produces<HealthScanResult>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        systems.MapGet("/{sid}/locked-users", async (string sid, IQueryHandler<ListLockedUsersQuery, IReadOnlyList<LockedUserDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListLockedUsersQuery(ApiRoutes.NormalizeSid(sid)), ct)).ToHttpResult())
            .WithSummary("Locked users (SU01), read live from the system, most recently locked first.")
            .Produces<IReadOnlyList<LockedUserDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}
