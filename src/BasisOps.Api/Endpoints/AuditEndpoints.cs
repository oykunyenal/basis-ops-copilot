using BasisOps.Api.Http;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Audit;

namespace BasisOps.Api.Endpoints;

internal static class AuditEndpoints
{
    public static void MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/audit", async (string? target, int? limit, IQueryHandler<GetAuditLogQuery, IReadOnlyList<AuditEntryDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetAuditLogQuery(string.IsNullOrWhiteSpace(target) ? null : target.Trim().ToUpperInvariant(), limit ?? 100), ct)).ToHttpResult())
            .WithTags("Audit")
            .WithSummary("Audit log, newest first. target is \"SID:OBJECT\", e.g. PRD:MEHMET.")
            .Produces<IReadOnlyList<AuditEntryDto>>()
            .ProducesValidationProblem();
    }
}
