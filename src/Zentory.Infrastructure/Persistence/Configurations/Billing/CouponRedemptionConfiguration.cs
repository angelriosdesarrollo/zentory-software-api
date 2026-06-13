using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ToTable("coupon_redemptions", "billing");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CouponId).HasColumnName("coupon_id").IsRequired();
        builder.Property(r => r.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(r => r.SubscriptionId).HasColumnName("subscription_id");
        builder.Property(r => r.DiscountAmount).HasColumnName("discount_amount").HasColumnType("decimal(10,2)");
        builder.Property(r => r.RedeemedAt).HasColumnName("redeemed_at");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(r => new { r.CouponId, r.OrganizationId }).IsUnique().HasDatabaseName("ix_coupon_redemptions_coupon_org");
    }
}
