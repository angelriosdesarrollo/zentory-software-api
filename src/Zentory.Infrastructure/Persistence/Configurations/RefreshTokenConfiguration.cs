using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.Id).HasColumnName("id");

        builder.Property(rt => rt.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(rt => rt.Token).HasColumnName("token").HasMaxLength(200).IsRequired();
        builder.Property(rt => rt.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(rt => rt.IsRevoked).HasColumnName("is_revoked").HasDefaultValue(false);
        builder.Property(rt => rt.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(rt => rt.Token).IsUnique().HasDatabaseName("ix_refresh_tokens_token");
        builder.HasIndex(rt => rt.UserId).HasDatabaseName("ix_refresh_tokens_user_id");
    }
}
