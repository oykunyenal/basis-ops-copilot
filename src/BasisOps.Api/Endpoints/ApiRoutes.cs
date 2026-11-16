using BasisOps.Api.Http;

namespace BasisOps.Api.Endpoints;

internal static class ApiRoutes
{
    public const string Prefix = "/api/v1";

    public static IEndpointRouteBuilder MapApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Prefix)
            .AddEndpointFilter<ApiKeyEndpointFilter>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        api.MapSystemEndpoints();
        api.MapFindingEndpoints();
        api.MapActionEndpoints();
        api.MapAuditEndpoints();
        api.MapDumpEndpoints();
        api.MapReportEndpoints();

        return app;
    }

    /// <summary>SIDs are upper case in SAP; accept "prd" from callers such as an LLM agent.</summary>
    public static string NormalizeSid(string sid) => sid.Trim().ToUpperInvariant();
}
