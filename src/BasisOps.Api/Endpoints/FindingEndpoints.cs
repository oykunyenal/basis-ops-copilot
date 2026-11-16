using BasisOps.Api.Http;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Findings;
using BasisOps.Domain.Findings;

namespace BasisOps.Api.Endpoints;

internal static class FindingEndpoints
{
    public static void MapFindingEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/findings", async (
                string? sid,
                Severity? severity,
                bool? open,
                int? page,
                int? pageSize,
                IQueryHandler<ListFindingsQuery, PagedResult<FindingDto>> handler,
                CancellationToken ct) =>
            {
                var query = new ListFindingsQuery(
                    sid is null ? null : ApiRoutes.NormalizeSid(sid), severity, open ?? true, page ?? 1, pageSize ?? 50);
                return (await handler.HandleAsync(query, ct)).ToHttpResult();
            })
            .WithTags("Findings")
            .WithSummary("Findings, most severe first. Open findings only unless open=false.")
            .Produces<PagedResult<FindingDto>>()
            .ProducesValidationProblem();
    }
}
