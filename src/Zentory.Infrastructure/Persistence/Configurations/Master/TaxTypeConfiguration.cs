using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Configurations.Master;

public class TaxTypeConfiguration : IEntityTypeConfiguration<TaxType>
{
    public void Configure(EntityTypeBuilder<TaxType> builder)
    {
        builder.ToTable("tax_types", "master");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(t => t.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(t => t.Code).HasColumnName("code").HasMaxLength(10).IsRequired();
        builder.Property(t => t.Rate).HasColumnName("rate").HasColumnType("decimal(5,4)").IsRequired();
        builder.Property(t => t.AppliesTo).HasColumnName("applies_to").HasMaxLength(20);
        builder.Property(t => t.Active).HasColumnName("active").HasDefaultValue(true);
    }
}
