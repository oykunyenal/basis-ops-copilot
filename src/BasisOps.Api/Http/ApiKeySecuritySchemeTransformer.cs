using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BasisOps.Api.Http;

/// <summary>Documents the X-Api-Key header so Scalar can send it.</summary>
internal sealed class ApiKeySecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeName = "ApiKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyEndpointFilter.HeaderName,
            Description = "Shared API key for local callers (n8n).",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
        });

        return Task.CompletedTask;
    }
}
