using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BasisOps.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only. Migrations are generated without a running database,
/// so the connection string is a placeholder and never used to connect.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BasisOpsDbContext>
{
    public BasisOpsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BasisOpsDbContext>()
            .UseNpgsql("Host=localhost;Database=basisops");
        BasisOpsDbContext.Configure(options);
        return new BasisOpsDbContext(options.Options);
    }
}
