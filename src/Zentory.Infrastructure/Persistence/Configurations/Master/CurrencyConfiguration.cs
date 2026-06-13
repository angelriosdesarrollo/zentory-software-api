using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Configurations.Master;

public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("currencies", "master");
        builder.HasKey(c => c.Code);
        builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(3).IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.Symbol).HasColumnName("symbol").HasMaxLength(5).IsRequired();
        builder.Property(c => c.Decimals).HasColumnName("decimals").HasDefaultValue((short)2);
        builder.Property(c => c.Active).HasColumnName("active").HasDefaultValue(true);
    }
}
