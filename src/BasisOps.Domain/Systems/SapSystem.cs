namespace BasisOps.Domain.Systems;

public enum SystemRole
{
    Development,
    Quality,
    Production,
}

/// <summary>An SAP system identified by its three-character SID (e.g. PRD).</summary>
public sealed record SapSystem
{
    public SapSystem(string sid, SystemRole role)
    {
        if (!IsValidSid(sid))
        {
            throw new ArgumentException($"'{sid}' is not a valid SID (three uppercase letters or digits, starting with a letter).", nameof(sid));
        }

        Sid = sid;
        Role = role;
    }

    public string Sid { get; }

    public SystemRole Role { get; }

    public bool IsProduction => Role == SystemRole.Production;

    public static bool IsValidSid(string? sid) =>
        sid is { Length: 3 }
        && char.IsAsciiLetterUpper(sid[0])
        && sid.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c));
}
