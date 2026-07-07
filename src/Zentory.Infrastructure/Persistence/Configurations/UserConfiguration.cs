using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.UserId);
        builder.Property(u => u.UserId).HasColumnName("user_id");

        builder.Property(u => u.OrganizationId).HasColumnName("organization_id").IsRequired(false);
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(500);
        builder.Property(u => u.GoogleId).HasColumnName("google_id").HasMaxLength(64);
        builder.Property(u => u.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        builder.Property(u => u.Role).HasColumnName("role").HasMaxLength(30).IsRequired();
        builder.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.TermsAcceptedAt).HasColumnName("terms_accepted_at").IsRequired(false);
        builder.Property(u => u.TermsAcceptedVersion).HasColumnName("terms_accepted_version").HasMaxLength(20).IsRequired(false);

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ix_users_email");
        builder.HasIndex(u => u.OrganizationId).HasDatabaseName("ix_users_org_id");
        builder.HasIndex(u => u.GoogleId).HasDatabaseName("ix_users_google_id");
    }
}
