using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class CashFlowEntryConfiguration : IEntityTypeConfiguration<CashFlowEntry>
{
    public void Configure(EntityTypeBuilder<CashFlowEntry> builder)
    {
        builder.ToTable("cash_flow_entries", "core");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(c => c.ProjectId).HasColumnName("project_id");
        builder.Property(c => c.InvoiceId).HasColumnName("invoice_id");
        builder.Property(c => c.CategoryId).HasColumnName("category_id");
        builder.Property(c => c.Type).HasColumnName("type").HasMaxLength(10).IsRequired();
        builder.Property(c => c.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(c => c.Amount).HasColumnName("amount").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(c => c.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(c => c.ExchangeRate).HasColumnName("exchange_rate").HasColumnType("decimal(16,8)");
        builder.Property(c => c.AmountBase).HasColumnName("amount_base").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(c => c.TransactionDate).HasColumnName("transaction_date");
        builder.Property(c => c.IsRecurring).HasColumnName("is_recurring").HasDefaultValue(false);
        builder.Property(c => c.RecurrenceRule).HasColumnName("recurrence_rule");
        builder.Property(c => c.CreatedBy).HasColumnName("created_by");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(c => new { c.OrganizationId, c.TransactionDate }).HasDatabaseName("ix_cash_flow_entries_org_date");
    }
}
