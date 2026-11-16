using BasisOps.Domain.Actions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BasisOps.Infrastructure.Persistence.Configurations;

internal sealed class ActionRequestConfiguration : IEntityTypeConfiguration<ActionRequest>
{
    public const string VersionProperty = "Version";

    public void Configure(EntityTypeBuilder<ActionRequest> builder)
    {
        builder.ToTable("action_requests");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Sid).HasMaxLength(3);
        builder.Property(a => a.Target).HasMaxLength(64);
        builder.Property(a => a.RequestedBy).HasMaxLength(100);
        builder.Property(a => a.Reason).HasMaxLength(500);
        builder.Property(a => a.DecidedBy).HasMaxLength(100);
        builder.Property(a => a.DecisionComment).HasMaxLength(500);
        builder.Property(a => a.LastError).HasMaxLength(2000);
        builder.Property(a => a.ResultMessage).HasMaxLength(2000);
        builder.Property(a => a.IdempotencyKey).HasMaxLength(128);
        builder.Property(a => a.Fingerprint).HasMaxLength(64).IsFixedLength();
        builder.Ignore(a => a.IsTerminal);
        builder.Ignore(a => a.AuditTarget);

        // Optimistic concurrency on the PostgreSQL xmin system column: two approvers racing get a 409.
        builder.Property<uint>(VersionProperty).IsRowVersion();

        builder.HasIndex(a => a.IdempotencyKey).IsUnique();

        // The executor picks due approved actions (step 4).
        builder.HasIndex(a => new { a.Status, a.NextAttemptAt });
    }
}
