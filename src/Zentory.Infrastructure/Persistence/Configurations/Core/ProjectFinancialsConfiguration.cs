using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ProjectFinancialsConfiguration : IEntityTypeConfiguration<ProjectFinancials>
{
    public void Configure(EntityTypeBuilder<ProjectFinancials> builder)
    {
        builder.ToTable("project_financials", "core");
        builder.HasKey(p => p.ProjectId);
        builder.Property(p => p.ProjectId).HasColumnName("project_id");
        builder.Property(p => p.ContractValue).HasColumnName("contract_value").HasColumnType("decimal(12,2)");
        builder.Property(p => p.TotalInvoiced).HasColumnName("total_invoiced").HasColumnType("decimal(12,2)");
        builder.Property(p => p.TotalCollected).HasColumnName("total_collected").HasColumnType("decimal(12,2)");
        builder.Property(p => p.TotalCost).HasColumnName("total_cost").HasColumnType("decimal(12,2)");
        builder.Property(p => p.BillableAmount).HasColumnName("billable_amount").HasColumnType("decimal(12,2)");
        builder.Property(p => p.TotalHours).HasColumnName("total_hours").HasColumnType("decimal(8,1)");
        builder.Property(p => p.BillableHours).HasColumnName("billable_hours").HasColumnType("decimal(8,1)");
        builder.Property(p => p.BudgetHours).HasColumnName("budget_hours").HasColumnType("decimal(8,1)");
        builder.Property(p => p.GrossMargin).HasColumnName("gross_margin").HasColumnType("decimal(12,2)");
        builder.Property(p => p.GrossMarginPct).HasColumnName("gross_margin_pct").HasColumnType("decimal(5,2)");
        builder.Property(p => p.NetMargin).HasColumnName("net_margin").HasColumnType("decimal(12,2)");
        builder.Property(p => p.PctTimeElapsed).HasColumnName("pct_time_elapsed").HasColumnType("decimal(5,2)");
        builder.Property(p => p.PctHoursConsumed).HasColumnName("pct_hours_consumed").HasColumnType("decimal(5,2)");
        builder.Property(p => p.PctPaymentsReceived).HasColumnName("pct_payments_received").HasColumnType("decimal(5,2)");
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(p => p.LastCalculatedAt).HasColumnName("last_calculated_at");
    }
}
