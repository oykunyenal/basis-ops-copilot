using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;
using BasisOps.Domain.Audit;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Scans;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BasisOps.Infrastructure.Persistence;

public sealed class BasisOpsDbContext(DbContextOptions<BasisOpsDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Finding> Findings => Set<Finding>();

    public DbSet<ActionRequest> ActionRequests => Set<ActionRequest>();

    public DbSet<HealthScan> HealthScans => Set<HealthScan>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>Options shared by the runtime registration and the design-time factory.</summary>
    internal static void Configure(DbContextOptionsBuilder options) => options.UseSnakeCaseNamingConvention();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new PersistenceConflictException("The resource was changed by another request. Reload it and try again.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new PersistenceConflictException($"A parallel request created the same resource ({pg.ConstraintName}). Retry the request.", ex);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as text: readable in SQL and safe against reordering.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BasisOpsDbContext).Assembly);
}
