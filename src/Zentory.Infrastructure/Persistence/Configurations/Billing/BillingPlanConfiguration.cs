using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class BillingPlanConfiguration : IEntityTypeConfiguration<BillingPlan>
{
    public void Configure(EntityTypeBuilder<BillingPlan> builder)
    {
        builder.ToTable("plans", "billing");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.PriceMonthlyUsd).HasColumnName("price_monthly_usd").HasColumnType("decimal(10,2)");
        builder.Property(p => p.PriceAnnualUsd).HasColumnName("price_annual_usd").HasColumnType("decimal(10,2)");
        builder.Property(p => p.GatewayPriceIdMonthly).HasColumnName("gateway_price_id_monthly").HasMaxLength(100);
        builder.Property(p => p.GatewayPriceIdAnnual).HasColumnName("gateway_price_id_annual").HasMaxLength(100);
        builder.Property(p => p.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(p => p.SortOrder).HasColumnName("sort_order").HasDefaultValue((short)0);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(p => p.Name).IsUnique().HasDatabaseName("ix_billing_plans_name");
    }
}
