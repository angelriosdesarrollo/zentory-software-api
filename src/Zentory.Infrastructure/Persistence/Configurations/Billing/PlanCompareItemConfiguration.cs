using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class PlanCompareItemConfiguration : IEntityTypeConfiguration<PlanCompareItem>
{
    public void Configure(EntityTypeBuilder<PlanCompareItem> builder)
    {
        builder.ToTable("plan_compare_items", "billing");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.FeatureName).HasColumnName("feature_name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.LegalType).HasColumnName("legal_type").HasMaxLength(20);
        builder.Property(c => c.IsEmpresaOnly).HasColumnName("is_empresa_only").HasDefaultValue(false);
        builder.Property(c => c.FreeValue).HasColumnName("free_value").HasMaxLength(100);
        builder.Property(c => c.ProValue).HasColumnName("pro_value").HasMaxLength(100);
        builder.Property(c => c.StudioValue).HasColumnName("studio_value").HasMaxLength(100);
        builder.Property(c => c.SortOrder).HasColumnName("sort_order").HasDefaultValue((short)0);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(c => new { c.LegalType, c.SortOrder }).HasDatabaseName("ix_plan_compare_lookup");
    }
}
