using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

// core.organization_invitations (PLAN-0018 RF-49 b; migración M4). El filtro de tenant y de borrado lógico está en
// ZentoryDbContext.OnModelCreating, con los demás.
public class OrganizationInvitationConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    public const string Table                  = "organization_invitations";
    public const string TokenHashIndex         = "ix_org_invitations_token_hash";
    public const string OrganizationEmailIndex = "ix_org_invitations_org_email";

    public void Configure(EntityTypeBuilder<OrganizationInvitation> builder)
    {
        builder.ToTable(Table, "core");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(i => i.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(i => i.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
        builder.Property(i => i.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(i => i.ExpiresAt).HasColumnName("expires_at").IsRequired();
        // Token de concurrencia (RF-49 b, RF-50): el canje es UPDATE ... WHERE accepted_at IS NULL, así que de dos
        // aceptaciones de la misma invitación solo una afecta la fila.
        builder.Property(i => i.AcceptedAt).HasColumnName("accepted_at").IsConcurrencyToken();
        builder.Property(i => i.InvitedBy).HasColumnName("invited_by").IsRequired();
        builder.Property(i => i.RevokedAt).HasColumnName("revoked_at");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        builder.Property(i => i.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(i => i.TokenHash).IsUnique().HasDatabaseName(TokenHashIndex);
        builder.HasIndex(i => new { i.OrganizationId, i.Email }).HasDatabaseName(OrganizationEmailIndex);
    }
}
