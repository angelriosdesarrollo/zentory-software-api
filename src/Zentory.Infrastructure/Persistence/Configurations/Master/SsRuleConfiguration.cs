using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Configurations.Master;

public class SsRuleConfiguration : IEntityTypeConfiguration<SsRule>
{
    public void Configure(EntityTypeBuilder<SsRule> builder)
    {
        builder.ToTable("ss_rules", "master");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        builder.Property(s => s.EffectiveYear).HasColumnName("effective_year").IsRequired();
        builder.Property(s => s.FundType).HasColumnName("fund_type").HasMaxLength(20).IsRequired();
        builder.Property(s => s.ContributorType).HasColumnName("contributor_type").HasMaxLength(30).IsRequired();
        builder.Property(s => s.EmployeePct).HasColumnName("employee_pct").HasColumnType("decimal(6,5)").IsRequired();
        builder.Property(s => s.EmployerPct).HasColumnName("employer_pct").HasColumnType("decimal(6,5)").IsRequired();
        builder.Property(s => s.TotalPct).HasColumnName("total_pct").HasColumnType("decimal(6,5)").IsRequired();
        builder.Property(s => s.MinBaseSmlv).HasColumnName("min_base_smlv").HasColumnType("decimal(6,4)").IsRequired();
        builder.Property(s => s.MaxBaseSmlv).HasColumnName("max_base_smlv").HasColumnType("decimal(6,4)");
        builder.Property(s => s.SmlvCop).HasColumnName("smlv_cop").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(s => s.ArlLevel).HasColumnName("arl_level");
        builder.Property(s => s.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(s => s.Notes).HasColumnName("notes");
    }
}
