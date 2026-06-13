using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Configurations.Master;

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("unit_of_measure", "master");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(u => u.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(u => u.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
    }
}
