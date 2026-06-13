using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class CouponCodeConfiguration : IEntityTypeConfiguration<CouponCode>
{
    public void Configure(EntityTypeBuilder<CouponCode> builder)
    {
        builder.ToTable("coupon_codes", "billing");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(c => c.Description).HasColumnName("description");
        builder.Property(c => c.DiscountType).HasColumnName("discount_type").HasMaxLength(10).IsRequired();
        builder.Property(c => c.DiscountValue).HasColumnName("discount_value").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(c => c.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(c => c.AppliesToPlan).HasColumnName("applies_to_plan").HasMaxLength(20);
        builder.Property(c => c.MaxRedemptions).HasColumnName("max_redemptions");
        builder.Property(c => c.CurrentRedemptions).HasColumnName("current_redemptions").HasDefaultValue(0);
        builder.Property(c => c.ValidFrom).HasColumnName("valid_from");
        builder.Property(c => c.ValidUntil).HasColumnName("valid_until");
        builder.Property(c => c.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(c => c.Code).IsUnique().HasDatabaseName("ix_coupon_codes_code");
    }
}
