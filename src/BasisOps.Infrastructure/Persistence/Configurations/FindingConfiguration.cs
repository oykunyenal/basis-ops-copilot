using BasisOps.Domain.Findings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BasisOps.Infrastructure.Persistence.Configurations;

internal sealed class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("findings");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Sid).HasMaxLength(3);
        builder.Property(f => f.Key).HasMaxLength(200);
        builder.Property(f => f.Title).HasMaxLength(300);
        builder.Property(f => f.Detail).HasMaxLength(2000);
        builder.Ignore(f => f.IsResolved);

        // One open finding per problem key; resolved findings are kept as history.
        builder.HasIndex(f => new { f.Sid, f.Key })
            .IsUnique()
            .HasFilter("resolved_at IS NULL")
            .HasDatabaseName("ux_findings_open_key");

        builder.HasIndex(f => new { f.Sid, f.ResolvedAt });
    }
}
