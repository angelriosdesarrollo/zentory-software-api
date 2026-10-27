using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

// core.database_marker (PLAN-0018 RF-63 h, M3a; D-42). Sin organization_id, sin FK y sin filtro global: es una tabla sin
// tenant (ADR-0008). Sin HasData: ninguna migración escribe la marca (SR-0018.33).
public class DatabaseMarkerConfiguration : IEntityTypeConfiguration<DatabaseMarker>
{
    public const string TableName = "database_marker";
    public const string Schema    = "core";

    public void Configure(EntityTypeBuilder<DatabaseMarker> builder)
    {
        builder.ToTable(TableName, Schema);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.Environment).HasColumnName("environment").HasMaxLength(20).IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Ignore(m => m.IsLocal);
    }
}
