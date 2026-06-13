using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Configurations.Master;

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rates", "master");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.FromCurrency).HasColumnName("from_currency").HasMaxLength(3).IsRequired();
        builder.Property(e => e.ToCurrency).HasColumnName("to_currency").HasMaxLength(3).IsRequired();
        builder.Property(e => e.Rate).HasColumnName("rate").HasColumnType("decimal(16,8)").IsRequired();
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(30).IsRequired();
        builder.Property(e => e.RateDate).HasColumnName("rate_date");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(e => new { e.FromCurrency, e.ToCurrency, e.RateDate })
               .HasDatabaseName("ix_master_exchange_rates_pair_date");
    }
}
