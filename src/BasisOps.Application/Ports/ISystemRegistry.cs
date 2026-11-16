using BasisOps.Domain.Systems;

namespace BasisOps.Application.Ports;

/// <summary>The SAP systems this installation manages, with their roles (used for the four-eyes rule).</summary>
public interface ISystemRegistry
{
    Task<IReadOnlyList<SapSystem>> ListAsync(CancellationToken cancellationToken);

    Task<SapSystem?> FindAsync(string sid, CancellationToken cancellationToken);
}
