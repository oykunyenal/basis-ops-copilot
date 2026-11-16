using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace BasisOps.Api.Http;

public sealed class ApiKeyAuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Shared key for local callers such as n8n. Set via user-secrets or the Aspire parameter, never in appsettings.</summary>
    [Required]
    [MinLength(16)]
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Simple shared-key check for the local setup. In production this is replaced by Entra ID / OAuth2
/// with role-based approval rights.
/// </summary>
internal sealed class ApiKeyEndpointFilter(IOptionsMonitor<ApiKeyAuthOptions> options) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (provided.Length == 0 || !KeysMatch(provided, options.CurrentValue.ApiKey))
        {
            return ValueTask.FromResult<object?>(TypedResults.Problem(
                title: "Unauthorized",
                detail: $"Missing or invalid {HeaderName} header.",
                statusCode: StatusCodes.Status401Unauthorized));
        }

        return next(context);
    }

    // Hashing first gives equal-length inputs, so the comparison time does not reveal the key length.
    private static bool KeysMatch(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
