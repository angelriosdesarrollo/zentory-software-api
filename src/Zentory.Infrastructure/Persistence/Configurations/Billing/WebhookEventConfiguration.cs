using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events", "billing");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.PaymentGateway).HasColumnName("payment_gateway").HasMaxLength(30).IsRequired();
        builder.Property(e => e.GatewayEventId).HasColumnName("gateway_event_id").HasMaxLength(100).IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Processed).HasColumnName("processed").HasDefaultValue(false);
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message");
        builder.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue((short)0);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(e => new { e.PaymentGateway, e.GatewayEventId }).IsUnique().HasDatabaseName("ix_webhook_events_gateway_event");
    }
}
