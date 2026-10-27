using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

// core.organization_ownership_transfers (PLAN-0018 RF-64, RF-83; D-33; migración M4c). El filtro de tenant está en
// ZentoryDbContext.OnModelCreating, con los demás.
public class OrganizationOwnershipTransferConfiguration : IEntityTypeConfiguration<OrganizationOwnershipTransfer>
{
    public const string Table              = "organization_ownership_transfers";
    public const string PendingIndex       = "ix_org_ownership_transfers_org_pending";
    public const string ToUserStatusIndex  = "ix_org_ownership_transfers_to_user_status";
    public const string PendingFilter      = "status = 'pending'";

    public void Configure(EntityTypeBuilder<OrganizationOwnershipTransfer> builder)
    {
        builder.ToTable(Table, "core");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(t => t.FromUserId).HasColumnName("from_user_id").IsRequired();
        builder.Property(t => t.ToUserId).HasColumnName("to_user_id").IsRequired();
        builder.Property(t => t.PreviewHash).HasColumnName("preview_hash")
            .HasMaxLength(OrganizationOwnershipTransfer.PreviewHashLength).IsRequired();
        // Token de concurrencia (SR-0018.37): aceptar, rechazar, cancelar o eliminar la organización son
        // UPDATE ... WHERE status = 'pending', así que de dos resoluciones simultáneas solo una afecta la fila.
        builder.Property(t => t.Status).HasColumnName("status").HasMaxLength(20).IsRequired().IsConcurrencyToken();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        // Una sola propuesta pendiente por organización (RF-64 c2). El valor del filtro es OwnershipTransferStatus.Pending.
        builder.HasIndex(t => t.OrganizationId)
            .HasDatabaseName(PendingIndex)
            .IsUnique()
            .HasFilter(PendingFilter);
        // El aviso del destino (RF-83 a) lee sus propuestas por to_user_id y estado.
        builder.HasIndex(t => new { t.ToUserId, t.Status }).HasDatabaseName(ToUserStatusIndex);
    }
}
