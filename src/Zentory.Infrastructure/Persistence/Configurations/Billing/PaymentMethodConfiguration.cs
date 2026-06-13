using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("payment_methods", "billing");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(p => p.GatewayPmId).HasColumnName("gateway_pm_id").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Type).HasColumnName("type").HasMaxLength(30).IsRequired();
        builder.Property(p => p.Brand).HasColumnName("brand").HasMaxLength(30);
        builder.Property(p => p.LastFour).HasColumnName("last_four").HasMaxLength(4);
        builder.Property(p => p.ExpMonth).HasColumnName("exp_month");
        builder.Property(p => p.ExpYear).HasColumnName("exp_year");
        builder.Property(p => p.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(p => p.GatewayPmId).IsUnique().HasDatabaseName("ix_payment_methods_gateway_pm_id");
    }
}
