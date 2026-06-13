using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        builder.ToTable("plan_features", "billing");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(f => f.AccountType).HasColumnName("account_type").HasMaxLength(20).IsRequired();
        builder.Property(f => f.Text).HasColumnName("text").HasMaxLength(300).IsRequired();
        builder.Property(f => f.IsHighlight).HasColumnName("is_highlight").HasDefaultValue(false);
        builder.Property(f => f.BadgeText).HasColumnName("badge_text").HasMaxLength(50);
        builder.Property(f => f.SortOrder).HasColumnName("sort_order").HasDefaultValue((short)0);
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(f => new { f.PlanId, f.AccountType, f.SortOrder }).HasDatabaseName("ix_plan_features_lookup");
    }
}
