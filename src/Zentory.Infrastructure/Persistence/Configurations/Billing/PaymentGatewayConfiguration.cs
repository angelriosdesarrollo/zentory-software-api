using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class PaymentGatewayConfiguration : IEntityTypeConfiguration<PaymentGateway>
{
    public void Configure(EntityTypeBuilder<PaymentGateway> builder)
    {
        builder.ToTable("payment_gateways", "billing");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id");
        builder.Property(g => g.Key).HasColumnName("key").HasMaxLength(30).IsRequired();
        builder.Property(g => g.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        builder.Property(g => g.IsActive).HasColumnName("is_active").HasDefaultValue(false);
        builder.Property(g => g.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
        builder.Property(g => g.ApiKeyEnv).HasColumnName("api_key_env").HasMaxLength(100).IsRequired();
        builder.Property(g => g.WebhookSecretEnv).HasColumnName("webhook_secret_env").HasMaxLength(100).IsRequired();
        builder.Property(g => g.PublicKey).HasColumnName("public_key").HasMaxLength(255);
        builder.Property(g => g.SupportedCountries).HasColumnName("supported_countries").HasColumnType("text[]");
        builder.Property(g => g.SupportedCurrencies).HasColumnName("supported_currencies").HasColumnType("text[]");
        builder.Property(g => g.WebhookPath).HasColumnName("webhook_path").HasMaxLength(100).IsRequired();
        builder.Property(g => g.SortOrder).HasColumnName("sort_order").HasDefaultValue((short)0);
        builder.Property(g => g.CreatedAt).HasColumnName("created_at");
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(g => g.Key).IsUnique().HasDatabaseName("ix_payment_gateways_key");
    }
}
