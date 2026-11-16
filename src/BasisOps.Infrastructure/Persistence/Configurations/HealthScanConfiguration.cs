using BasisOps.Domain.Scans;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BasisOps.Infrastructure.Persistence.Configurations;

internal sealed class HealthScanConfiguration : IEntityTypeConfiguration<HealthScan>
{
    public void Configure(EntityTypeBuilder<HealthScan> builder)
    {
        builder.ToTable("health_scans");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Sid).HasMaxLength(3);
        builder.Property(s => s.Error).HasMaxLength(2000);
        builder.HasIndex(s => new { s.Sid, s.StartedAt }).IsDescending(false, true);
    }
}
