using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class SsCalculationLogConfiguration : IEntityTypeConfiguration<SsCalculationLog>
{
    public void Configure(EntityTypeBuilder<SsCalculationLog> builder)
    {
        builder.ToTable("ss_calculation_logs", "core");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.CollaboratorId).HasColumnName("collaborator_id");
        builder.Property(s => s.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        builder.Property(s => s.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
        builder.Property(s => s.Income).HasColumnName("income").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(s => s.Result).HasColumnName("result").HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.TotalContribution).HasColumnName("total_contribution").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(s => s.SmlvUsed).HasColumnName("smlv_used").HasColumnType("decimal(12,2)");
        builder.Property(s => s.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(s => s.FiledAt).HasColumnName("filed_at");
        builder.Property(s => s.DocumentUrl).HasColumnName("document_url");
        builder.Property(s => s.DocumentFileName).HasColumnName("document_file_name").HasMaxLength(255);
        builder.Property(s => s.DocumentFileSize).HasColumnName("document_file_size");
        builder.Property(s => s.DocumentContentType).HasColumnName("document_content_type").HasMaxLength(100);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(s => new { s.OrganizationId, s.Period }).HasDatabaseName("ix_ss_calc_logs_org_period");
    }
}
