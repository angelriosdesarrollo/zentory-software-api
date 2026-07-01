using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions", "billing");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(s => s.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(s => s.GatewaySubscriptionId).HasColumnName("gateway_subscription_id").HasMaxLength(100);
        builder.Property(s => s.GatewayPriceId).HasColumnName("gateway_price_id").HasMaxLength(100);
        builder.Property(s => s.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(s => s.BillingPeriod).HasColumnName("billing_period").HasMaxLength(10).IsRequired();
        builder.Property(s => s.CurrentPeriodStart).HasColumnName("current_period_start");
        builder.Property(s => s.CurrentPeriodEnd).HasColumnName("current_period_end");
        builder.Property(s => s.TrialEndsAt).HasColumnName("trial_ends_at");
        builder.Property(s => s.CancelAtPeriodEnd).HasColumnName("cancel_at_period_end").HasDefaultValue(false);
        builder.Property(s => s.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(s => s.CancellationReason).HasColumnName("cancellation_reason");
        builder.Property(s => s.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)");
        builder.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(s => s.DiscountPct).HasColumnName("discount_pct").HasColumnType("decimal(5,2)");
        builder.Property(s => s.DunningAttempt).HasColumnName("dunning_attempt").HasDefaultValue((short)0);
        builder.Property(s => s.NextDunningAt).HasColumnName("next_dunning_at");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(s => s.UserId).IsUnique().HasDatabaseName("ix_subscriptions_user_id");
    }
}
