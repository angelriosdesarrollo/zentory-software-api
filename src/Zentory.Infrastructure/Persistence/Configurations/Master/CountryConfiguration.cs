using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Configurations.Master;

public class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries", "master");
        builder.HasKey(c => c.Code);
        builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(2).IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        builder.Property(c => c.Timezone).HasColumnName("timezone").HasMaxLength(60);
        builder.Property(c => c.Locale).HasColumnName("locale").HasMaxLength(10);
        builder.Property(c => c.Active).HasColumnName("active").HasDefaultValue(true);
    }
}
