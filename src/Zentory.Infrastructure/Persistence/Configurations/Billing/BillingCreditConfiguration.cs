using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class BillingCreditConfiguration : IEntityTypeConfiguration<BillingCredit>
{
    public void Configure(EntityTypeBuilder<BillingCredit> builder)
    {
        builder.ToTable("credits", "billing");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(c => c.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(c => c.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(c => c.Type).HasColumnName("type").HasMaxLength(30).IsRequired();
        builder.Property(c => c.Reason).HasColumnName("reason");
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.AppliedAt).HasColumnName("applied_at");
        builder.Property(c => c.AppliedToInvoiceId).HasColumnName("applied_to_invoice_id");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
    }
}
