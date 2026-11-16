using BasisOps.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BasisOps.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Actor).HasMaxLength(100);
        builder.Property(e => e.Action).HasMaxLength(64);
        builder.Property(e => e.EntityType).HasMaxLength(64);
        builder.Property(e => e.Target).HasMaxLength(100);
        builder.Property(e => e.FromState).HasMaxLength(32);
        builder.Property(e => e.ToState).HasMaxLength(32);
        builder.Property(e => e.Detail).HasMaxLength(2000);
        builder.HasIndex(e => new { e.Target, e.Timestamp });
        builder.HasIndex(e => e.EntityId);
    }
}
