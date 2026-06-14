using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class OrganizationIntegrationConfiguration : IEntityTypeConfiguration<OrganizationIntegration>
{
    public void Configure(EntityTypeBuilder<OrganizationIntegration> builder)
    {
        builder.ToTable("organization_integrations", "core");
        builder.HasKey(oi => oi.Id);
        builder.Property(oi => oi.Id).HasColumnName("id");
        builder.Property(oi => oi.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(oi => oi.IntegrationId).HasColumnName("integration_id").HasMaxLength(100).IsRequired();
        builder.Property(oi => oi.IsActive).HasColumnName("is_active").HasDefaultValue(false);
        builder.Property(oi => oi.ConnectedAt).HasColumnName("connected_at");
        builder.Property(oi => oi.ConnectedByUserId).HasColumnName("connected_by_user_id");
        builder.Property(oi => oi.ConnectedAs).HasColumnName("connected_as").HasMaxLength(255);
        builder.Property(oi => oi.ExternalAccountId).HasColumnName("external_account_id").HasMaxLength(255);
        builder.Property(oi => oi.ExternalWorkspaceId).HasColumnName("external_workspace_id").HasMaxLength(255);
        builder.Property(oi => oi.AccessTokenEncrypted).HasColumnName("access_token_encrypted");
        builder.Property(oi => oi.RefreshTokenEncrypted).HasColumnName("refresh_token_encrypted");
        builder.Property(oi => oi.TokenExpiresAt).HasColumnName("token_expires_at");
        builder.Property(oi => oi.Scopes).HasColumnName("scopes").HasMaxLength(1000);
        builder.Property(oi => oi.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(oi => oi.CreatedAt).HasColumnName("created_at");
        builder.Property(oi => oi.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(oi => new { oi.OrganizationId, oi.IntegrationId })
            .IsUnique()
            .HasDatabaseName("ix_org_integrations_org_integration");

        builder.HasIndex(oi => oi.OrganizationId)
            .HasDatabaseName("ix_org_integrations_org_id");
    }
}
