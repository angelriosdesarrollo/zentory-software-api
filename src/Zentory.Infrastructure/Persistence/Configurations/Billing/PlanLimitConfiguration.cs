using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class PlanLimitConfiguration : IEntityTypeConfiguration<PlanLimit>
{
    public void Configure(EntityTypeBuilder<PlanLimit> builder)
    {
        builder.ToTable("plan_limits", "billing");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(l => l.LegalType).HasColumnName("legal_type").HasMaxLength(20).IsRequired();
        builder.Property(l => l.FeatureKey).HasColumnName("feature_key").HasMaxLength(100).IsRequired();
        builder.Property(l => l.LimitValue).HasColumnName("limit_value");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(l => new { l.PlanId, l.LegalType, l.FeatureKey }).IsUnique().HasDatabaseName("ix_plan_limits_plan_account_feature");
    }
}
