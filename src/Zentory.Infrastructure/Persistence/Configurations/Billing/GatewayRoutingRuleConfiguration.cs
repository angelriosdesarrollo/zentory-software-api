using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class GatewayRoutingRuleConfiguration : IEntityTypeConfiguration<GatewayRoutingRule>
{
    public void Configure(EntityTypeBuilder<GatewayRoutingRule> builder)
    {
        builder.ToTable("gateway_routing_rules", "billing");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.GatewayKey).HasColumnName("gateway_key").HasMaxLength(30).IsRequired();
        builder.Property(r => r.Priority).HasColumnName("priority").HasDefaultValue((short)0);
        builder.Property(r => r.CountryCode).HasColumnName("country_code").HasMaxLength(2);
        builder.Property(r => r.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(r => r.LegalType).HasColumnName("legal_type").HasMaxLength(20);
        builder.Property(r => r.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(r => r.ValidFrom).HasColumnName("valid_from");
        builder.Property(r => r.ValidUntil).HasColumnName("valid_until");
        builder.Property(r => r.Reason).HasColumnName("reason").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(r => r.Priority).HasDatabaseName("ix_gateway_routing_active");
    }
}
