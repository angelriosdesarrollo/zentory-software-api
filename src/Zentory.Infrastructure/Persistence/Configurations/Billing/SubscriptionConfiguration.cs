using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public const string UserIdIndex     = "ix_subscriptions_user_id";
    public const string ActiveUserIndex = "ux_subscriptions_user_active";

    // Filtro del índice único de M3 (RF-38, C-1) con los valores de SubscriptionStatus.Current.
    public static readonly string ActiveStatusFilter =
        $"status IN ({string.Join(',', SubscriptionStatus.Current.Select(status => $"'{status}'"))})";

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
        // Token de concurrencia: cancelar o reasignar solo afecta a la fila si sigue en el estado leído (UPDATE ... WHERE
        // status = <leído>). Dos seed assign-plan simultáneos sobre el mismo owner no pierden una operación: el que pierde
        // la carrera falla sin efecto (DbUpdateConcurrencyException o el índice único de M3).
        builder.Property(s => s.Status).HasColumnName("status").HasMaxLength(20).IsRequired().IsConcurrencyToken();
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
        // Historial de suscripciones por usuario (no único) y como máximo una vigente por usuario (RF-38, C-1): la
        // reasignación de seed assign-plan cancela la anterior y crea otra sin chocar con el índice (D-23).
        builder.HasIndex(s => s.UserId, UserIdIndex);
        builder.HasIndex(s => s.UserId, ActiveUserIndex).IsUnique().HasFilter(ActiveStatusFilter);
    }
}
