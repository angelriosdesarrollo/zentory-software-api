using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(p => p.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status")
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.BillingType).HasColumnName("billing_type")
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.ContractValue).HasColumnName("contract_value")
            .HasColumnType("decimal(14,2)").IsRequired();
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(p => p.HoursTotal).HasColumnName("hours_total").HasDefaultValue(0);
        builder.Property(p => p.HoursUsed).HasColumnName("hours_used").HasDefaultValue(0);
        builder.Property(p => p.StartDate).HasColumnName("start_date");
        builder.Property(p => p.EndDate).HasColumnName("end_date");
        builder.Property(p => p.ProposalId).HasColumnName("proposal_id");
        builder.Property(p => p.Type).HasColumnName("type").HasMaxLength(20);
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(p => p.OrganizationId).HasDatabaseName("ix_projects_org_id");
        builder.HasIndex(p => p.ClientId).HasDatabaseName("ix_projects_client_id");
    }
}
