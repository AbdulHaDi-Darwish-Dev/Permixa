using Permixa.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Permixa.Infrastructure.Persistence.Configurations;

public sealed class IamAuditLogConfiguration : IEntityTypeConfiguration<IamAuditLog>
{
    public void Configure(EntityTypeBuilder<IamAuditLog> builder)
    {
        builder.ToTable("IamAuditLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.OccurredAtUtc).IsRequired();

        builder.Property(l => l.EventType)
            .IsRequired()
            .HasMaxLength(IamAuditLog.MaxEventTypeLength);

        builder.Property(l => l.Outcome)
            .IsRequired()
            .HasMaxLength(IamAuditLog.MaxOutcomeLength);

        builder.Property(l => l.CorrelationId)
            .HasMaxLength(IamAuditLog.MaxCorrelationIdLength);

        builder.Property(l => l.MetadataJson)
            .HasMaxLength(IamAuditLog.MaxMetadataJsonLength)
            .HasColumnType("nvarchar(max)");

        builder.Property(l => l.ActorUserId);
        builder.Property(l => l.TargetUserId);
        builder.Property(l => l.TargetRoleId);
        builder.Property(l => l.TargetPermissionId);

        builder.HasIndex(l => new { l.OccurredAtUtc, l.Id })
            .HasDatabaseName("IX_IamAuditLogs_OccurredAtUtc_Id");

        builder.HasIndex(l => l.ActorUserId)
            .HasDatabaseName("IX_IamAuditLogs_ActorUserId");

        builder.HasIndex(l => l.TargetUserId)
            .HasDatabaseName("IX_IamAuditLogs_TargetUserId");

        builder.HasIndex(l => l.EventType)
            .HasDatabaseName("IX_IamAuditLogs_EventType");

        // No foreign keys: audit history must survive deletion of referenced IAM resources.
    }
}
