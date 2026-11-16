namespace BasisOps.Domain.Rules;

/// <summary>SAP-style name pattern with a trailing '*' (e.g. Z_BILLING*). Without '*' the name must match exactly.</summary>
public static class WildcardPattern
{
    public static bool IsMatch(string pattern, string value) =>
        pattern.EndsWith('*')
            ? value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
}
