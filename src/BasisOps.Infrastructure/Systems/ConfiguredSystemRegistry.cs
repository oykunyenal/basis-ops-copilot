using BasisOps.Application.Ports;
using BasisOps.Domain.Systems;
using Microsoft.Extensions.Options;

namespace BasisOps.Infrastructure.Systems;

public sealed class LandscapeOptions
{
    public const string SectionName = "Landscape";

    public List<SystemEntry> Systems { get; set; } = [];

    public sealed class SystemEntry
    {
        public string Sid { get; set; } = string.Empty;

        public SystemRole Role { get; set; }
    }

    internal bool IsValid() =>
        Systems.Count > 0
        && Systems.All(s => SapSystem.IsValidSid(s.Sid))
        && Systems.Select(s => s.Sid).Distinct(StringComparer.Ordinal).Count() == Systems.Count;
}

/// <summary>
/// The managed systems and their roles come from configuration, not from SAP: the four-eyes rule must keep
/// working while SAP is unreachable.
/// </summary>
internal sealed class ConfiguredSystemRegistry(IOptions<LandscapeOptions> options) : ISystemRegistry
{
    private readonly IReadOnlyList<SapSystem> _systems = [.. options.Value.Systems.Select(s => new SapSystem(s.Sid, s.Role))];

    public Task<IReadOnlyList<SapSystem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(_systems);

    public Task<SapSystem?> FindAsync(string sid, CancellationToken cancellationToken) =>
        Task.FromResult(_systems.FirstOrDefault(s => string.Equals(s.Sid, sid, StringComparison.Ordinal)));
}
