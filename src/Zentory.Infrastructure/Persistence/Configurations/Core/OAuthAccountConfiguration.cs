using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class OAuthAccountConfiguration : IEntityTypeConfiguration<OAuthAccount>
{
    public void Configure(EntityTypeBuilder<OAuthAccount> builder)
    {
        builder.ToTable("oauth_accounts", "core");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(o => o.Provider).HasColumnName("provider").HasMaxLength(30).IsRequired();
        builder.Property(o => o.ProviderUserId).HasColumnName("provider_user_id").HasMaxLength(255).IsRequired();
        builder.Property(o => o.AccessToken).HasColumnName("access_token");
        builder.Property(o => o.RefreshToken).HasColumnName("refresh_token");
        builder.Property(o => o.ExpiresAt).HasColumnName("expires_at");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(o => new { o.Provider, o.ProviderUserId }).IsUnique().HasDatabaseName("ix_oauth_accounts_provider_user");
    }
}
