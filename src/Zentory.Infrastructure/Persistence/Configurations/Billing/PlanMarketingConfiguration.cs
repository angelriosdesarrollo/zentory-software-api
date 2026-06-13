using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class PlanMarketingConfiguration : IEntityTypeConfiguration<PlanMarketing>
{
    public void Configure(EntityTypeBuilder<PlanMarketing> builder)
    {
        builder.ToTable("plan_marketing", "billing");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(m => m.AccountType).HasColumnName("account_type").HasMaxLength(20).IsRequired();
        builder.Property(m => m.Tagline).HasColumnName("tagline").HasMaxLength(300).IsRequired();
        builder.Property(m => m.CtaText).HasColumnName("cta_text").HasMaxLength(100).IsRequired();
        builder.Property(m => m.IsPopular).HasColumnName("is_popular").HasDefaultValue(false);
        builder.Property(m => m.FeaturesHeading).HasColumnName("features_heading").HasMaxLength(100);
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(m => new { m.PlanId, m.AccountType }).IsUnique().HasDatabaseName("ix_plan_marketing_plan_account");
    }
}
