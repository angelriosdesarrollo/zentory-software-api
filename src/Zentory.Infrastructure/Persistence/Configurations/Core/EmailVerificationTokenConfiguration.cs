using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class EmailVerificationTokenConfiguration : IEntityTypeConfiguration<EmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
    {
        builder.ToTable("email_verification_tokens", "core");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.UsedAt).HasColumnName("used_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_email_verification_tokens_hash");
    }
}
