using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ProposalItemConfiguration : IEntityTypeConfiguration<ProposalItem>
{
    public void Configure(EntityTypeBuilder<ProposalItem> builder)
    {
        builder.ToTable("proposal_items", "core");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(i => i.ProposalId).HasColumnName("proposal_id").IsRequired();
        builder.Property(i => i.ServiceId).HasColumnName("service_id");
        builder.Property(i => i.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(i => i.Quantity).HasColumnName("quantity").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(i => i.UnitId).HasColumnName("unit_id");
        builder.Property(i => i.UnitPrice).HasColumnName("unit_price").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(i => i.DiscountPct).HasColumnName("discount_pct").HasColumnType("decimal(5,2)").HasDefaultValue(0m);
        builder.Property(i => i.Total).HasColumnName("total").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(i => i.SortOrder).HasColumnName("sort_order").HasDefaultValue((short)0);
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
    }
}
