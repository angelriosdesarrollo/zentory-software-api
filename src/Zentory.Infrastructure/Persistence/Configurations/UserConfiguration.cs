using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public const string EmailNormalizedCheck = "ck_users_email_normalized";
    public const string GoogleIdIndex        = "ix_users_google_id";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        // El email se guarda normalizado (PLAN-0018 RF-27, D-10): con el check, el índice único ix_users_email ya no
        // distingue mayúsculas. La expresión es la misma que EmailAddress.Normalize para emails ASCII.
        builder.ToTable("users", table => table.HasCheckConstraint(
            EmailNormalizedCheck, "email = lower(btrim(email))"));

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
        // Sin FK (D-27): la purga de organizaciones la pone en null.
        builder.Property(u => u.LastActiveOrganizationId).HasColumnName("last_active_organization_id").IsRequired(false);
        // Token de concurrencia de max_owned_orgs (D-45). M0c lo crea con el defaultValue de relleno de la herramienta.
        builder.Property(u => u.OwnershipStamp).HasColumnName("ownership_stamp").IsConcurrencyToken();
        builder.Property(u => u.EmailVerifiedAt).HasColumnName("email_verified_at").IsRequired(false);
        // Sesión vigente (PLAN-0018 RF-90 a, D-47; M2c): sin FK, sin índice (el usuario se lee por clave primaria) y sin
        // relleno. No es token de concurrencia: de dos logins simultáneos gana el último que confirma (RF-90 g).
        builder.Property(u => u.CurrentSessionId).HasColumnName("current_session_id").IsRequired(false);

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ix_users_email");
        builder.HasIndex(u => u.OrganizationId).HasDatabaseName("ix_users_org_id");
        // Una identidad de Google vincula a lo sumo una cuenta (PLAN-0018 RF-28 d, SR-0018.15, M2b).
        builder.HasIndex(u => u.GoogleId).IsUnique().HasFilter("google_id IS NOT NULL").HasDatabaseName(GoogleIdIndex);
    }
}
