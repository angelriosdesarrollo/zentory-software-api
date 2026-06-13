using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Ai;

namespace Zentory.Infrastructure.Persistence.Configurations.Ai;

public class AiQuotaOverrideConfiguration : IEntityTypeConfiguration<AiQuotaOverride>
{
    public void Configure(EntityTypeBuilder<AiQuotaOverride> builder)
    {
        builder.ToTable("quota_overrides", "ai");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).HasColumnName("id");
        builder.Property(q => q.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(q => q.FeatureId).HasColumnName("feature_id");
        builder.Property(q => q.MonthlyRequestLimit).HasColumnName("monthly_request_limit");
        builder.Property(q => q.MonthlyTokenLimit).HasColumnName("monthly_token_limit");
        builder.Property(q => q.ValidFrom).HasColumnName("valid_from");
        builder.Property(q => q.ValidUntil).HasColumnName("valid_until");
        builder.Property(q => q.Reason).HasColumnName("reason");
        builder.Property(q => q.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        builder.Property(q => q.CreatedAt).HasColumnName("created_at");
        builder.Property(q => q.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(q => q.OrganizationId).HasDatabaseName("ix_ai_quota_overrides_org_id");
    }
}
