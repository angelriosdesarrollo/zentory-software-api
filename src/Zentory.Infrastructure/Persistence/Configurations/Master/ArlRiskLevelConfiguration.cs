using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Configurations.Master;

public class ArlRiskLevelConfiguration : IEntityTypeConfiguration<ArlRiskLevel>
{
    public void Configure(EntityTypeBuilder<ArlRiskLevel> builder)
    {
        builder.ToTable("arl_risk_levels", "master");
        builder.HasKey(a => a.Level);
        builder.Property(a => a.Level).HasColumnName("level").ValueGeneratedNever();
        builder.Property(a => a.Description).HasColumnName("description").HasMaxLength(100).IsRequired();
        builder.Property(a => a.Rate).HasColumnName("rate").HasColumnType("decimal(6,5)").IsRequired();
        builder.Property(a => a.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
    }
}
