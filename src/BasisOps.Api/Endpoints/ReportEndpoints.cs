using BasisOps.Api.Http;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Reports;

namespace BasisOps.Api.Endpoints;

internal static class ReportEndpoints
{
    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/reports").WithTags("Reports");

        reports.MapGet("/daily", async (string sid, IQueryHandler<GetDailyReportQuery, DailyReportDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetDailyReportQuery(ApiRoutes.NormalizeSid(sid)), ct)).ToHttpResult())
            .WithSummary("Morning report: open findings by severity plus an LLM summary. "
                + "The report never depends on the LLM: without it aiSummary is null and aiAvailable is false.")
            .Produces<DailyReportDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
