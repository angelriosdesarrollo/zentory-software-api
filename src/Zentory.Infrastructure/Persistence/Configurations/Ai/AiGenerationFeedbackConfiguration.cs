using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Ai;

namespace Zentory.Infrastructure.Persistence.Configurations.Ai;

public class AiGenerationFeedbackConfiguration : IEntityTypeConfiguration<AiGenerationFeedback>
{
    public void Configure(EntityTypeBuilder<AiGenerationFeedback> builder)
    {
        builder.ToTable("generation_feedback", "ai");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.UsageLogId).HasColumnName("usage_log_id").IsRequired();
        builder.Property(f => f.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(f => f.UserId).HasColumnName("user_id");
        builder.Property(f => f.Action).HasColumnName("action").HasMaxLength(20).IsRequired();
        builder.Property(f => f.EditDistancePct).HasColumnName("edit_distance_pct").HasColumnType("decimal(5,2)");
        builder.Property(f => f.Rating).HasColumnName("rating");
        builder.Property(f => f.Comment).HasColumnName("comment");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(f => f.UsageLogId).HasDatabaseName("ix_ai_generation_feedback_usage_log_id");
    }
}
