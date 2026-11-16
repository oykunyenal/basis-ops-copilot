using System.Text.RegularExpressions;

namespace BasisOps.Infrastructure.Ai;

/// <summary>
/// Replaces user names, host names, IP and e-mail addresses with placeholders (USER_1, HOST_1, IP_1, EMAIL_1)
/// before text is sent to the LLM. One instance per prompt: the same value always gets the same placeholder,
/// so the model can still tell that two lines are about the same user. Placeholders are not translated back.
/// </summary>
internal sealed partial class SensitiveDataMasker
{
    private const string User = "USER";
    private const string Host = "HOST";
    private const string Ip = "IP";
    private const string Email = "EMAIL";

    // Characters that may appear in SAP user names (SU01 BNAME) and host names.
    private const string NameChar = "A-Za-z0-9_";

    private readonly Dictionary<string, string> _placeholders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);
    private readonly List<(string Value, string Kind)> _known = [];

    /// <summary>A user name taken from a structured field (e.g. the dump's user). Masked wherever it appears.</summary>
    public SensitiveDataMasker AddUser(string? userName) => AddKnown(userName, User);

    /// <summary>A host name taken from a structured field (e.g. the dump's application server).</summary>
    public SensitiveDataMasker AddHost(string? hostName) => AddKnown(hostName, Host);

    public string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var masked = EmailPattern().Replace(text, m => Placeholder(m.Value, Email));
        masked = IPv4Pattern().Replace(masked, m => Placeholder(m.Value, Ip));
        masked = IPv6Pattern().Replace(masked, m => Placeholder(m.Value, Ip));
        masked = FqdnPattern().Replace(masked, m => Placeholder(m.Value, Host));

        // Known values: case-sensitive and whole words only, so the user "CAN" does not break "canceled".
        foreach (var (value, kind) in _known.OrderByDescending(k => k.Value.Length))
        {
            var pattern = $"(?<![{NameChar}]){Regex.Escape(value)}(?![{NameChar}])";
            masked = Regex.Replace(masked, pattern, _ => Placeholder(value, kind), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        // Free text such as "User MEHMET is locked", "(owner BATCH_ADM)", "application server prdapp01".
        masked = UserKeywordPattern().Replace(masked, m => MaskGroup(m, User));
        masked = HostKeywordPattern().Replace(masked, m => MaskGroup(m, Host));
        return masked;
    }

    private SensitiveDataMasker AddKnown(string? value, string kind)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _known.Add((value.Trim(), kind));
        }

        return this;
    }

    private string MaskGroup(Match match, string kind)
    {
        var value = match.Groups["value"];
        if (IsPlaceholder(value.Value))
        {
            return match.Value;
        }

        var start = value.Index - match.Index;
        return string.Concat(match.Value.AsSpan(0, start), Placeholder(value.Value, kind), match.Value.AsSpan(start + value.Length));
    }

    private string Placeholder(string value, string kind)
    {
        if (_placeholders.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var next = _counters.GetValueOrDefault(kind) + 1;
        _counters[kind] = next;
        return _placeholders[value] = $"{kind}_{next}";
    }

    private static bool IsPlaceholder(string value) => PlaceholderPattern().IsMatch(value);

    [GeneratedRegex(@"^(?:USER|HOST|IP|EMAIL)_\d+$")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\d.])", RegexOptions.CultureInvariant)]
    private static partial Regex IPv4Pattern();

    // Full or compressed IPv6 with at least three groups; "12:30:00" has no hex letters or "::" and stays.
    [GeneratedRegex(@"(?<![0-9A-Fa-f:])(?=[0-9A-Fa-f:]*(?:::|[A-Fa-f]))(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?![0-9A-Fa-f:])", RegexOptions.CultureInvariant)]
    private static partial Regex IPv6Pattern();

    // At least two dots and a letter-only last label: prdapp01.example.internal (not "7.5" or "e.g.").
    [GeneratedRegex(@"(?<![A-Za-z0-9.-])[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+\.[A-Za-z]{2,24}(?![A-Za-z0-9-])",
        RegexOptions.CultureInvariant)]
    private static partial Regex FqdnPattern();

    // The value must look like an SAP user name (upper case, at most 12 characters), so "the user who..." stays.
    [GeneratedRegex(@"\b(?:user|owner|uname|bname|created by|changed by)\s*[:=]?\s+(?<value>(?-i:[A-Z0-9](?:[A-Z0-9_.\-]{0,10}[A-Z0-9_])?))(?![A-Za-z0-9_])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserKeywordPattern();

    // Host names contain a digit (prdapp01); this keeps "server is down" readable.
    [GeneratedRegex(@"\b(?:host|server|hostname)\s*[:=]?\s+(?<value>(?-i:[A-Za-z0-9][A-Za-z0-9_\-]*\d[A-Za-z0-9_\-]*))(?![A-Za-z0-9_])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostKeywordPattern();
}
