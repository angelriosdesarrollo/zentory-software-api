using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class CollaboratorConfiguration : IEntityTypeConfiguration<Collaborator>
{
    public void Configure(EntityTypeBuilder<Collaborator> builder)
    {
        builder.ToTable("collaborators", "core");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(c => c.UserId).HasColumnName("user_id");
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(50);
        builder.Property(c => c.Type).HasColumnName("type").HasMaxLength(30).IsRequired();
        builder.Property(c => c.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(c => c.Role).HasColumnName("role").HasMaxLength(100);
        builder.Property(c => c.HourlyRate).HasColumnName("hourly_rate").HasColumnType("decimal(10,2)");
        builder.Property(c => c.MonthlyRate).HasColumnName("monthly_rate").HasColumnType("decimal(12,2)");
        builder.Property(c => c.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(c => c.PilaStatus).HasColumnName("pila_status").HasMaxLength(20).IsRequired();
        builder.Property(c => c.PilaLastVerifiedPeriod).HasColumnName("pila_last_verified_period").HasMaxLength(7);
        builder.Property(c => c.ArlRiskLevel).HasColumnName("arl_risk_level");
        builder.Property(c => c.IdNumber).HasColumnName("id_number").HasMaxLength(50);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(c => c.OrganizationId).HasDatabaseName("ix_collaborators_org_id");
        builder.HasIndex(c => new { c.OrganizationId, c.Status }).HasDatabaseName("ix_collaborators_org_status");
    }
}
