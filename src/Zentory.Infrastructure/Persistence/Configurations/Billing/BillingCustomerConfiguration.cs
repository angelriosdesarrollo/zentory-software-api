using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(EntityTypeBuilder<BillingCustomer> builder)
    {
        builder.ToTable("customers", "billing");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(c => c.PaymentGateway).HasColumnName("payment_gateway").HasMaxLength(30).IsRequired();
        builder.Property(c => c.GatewayCustomerId).HasColumnName("gateway_customer_id").HasMaxLength(100).IsRequired();
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(c => c.CountryCode).HasColumnName("country_code").HasMaxLength(2);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(c => c.OrganizationId).IsUnique().HasDatabaseName("ix_billing_customers_org_id");
        builder.HasIndex(c => c.GatewayCustomerId).IsUnique().HasDatabaseName("ix_billing_customers_gateway_id");
    }
}
