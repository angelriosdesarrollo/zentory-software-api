using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class CollaboratorAccessTokenConfiguration : IEntityTypeConfiguration<CollaboratorAccessToken>
{
    public void Configure(EntityTypeBuilder<CollaboratorAccessToken> builder)
    {
        builder.ToTable("collaborator_access_tokens", "core");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.UsedAt).HasColumnName("used_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_collaborator_access_tokens_hash");
        builder.HasIndex(t => t.Email).HasDatabaseName("ix_collaborator_access_tokens_email");
    }
}
