using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BasisOps.IntegrationTests;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(Options, cancellationToken)
        ?? throw new InvalidOperationException("Empty response body.");

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
