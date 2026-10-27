using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zentory.Infrastructure.Persistence;

/// <summary>
/// Used only by EF Core design-time tools (dotnet ef migrations add / migrations script).
/// Not part of the production DI container.
/// </summary>
// ADR-0007 §3 (PLAN-0018 RF-2, SR-0018.3): la cadena es un marcador sin credenciales. La herramienta solo necesita el
// proveedor para generar migraciones y scripts, nunca conecta; las migraciones se aplican arrancando la API en
// Development o con `db init`. No lee configuración, variables de entorno ni archivos de ajustes.
public sealed class ZentoryDbContextFactory : IDesignTimeDbContextFactory<ZentoryDbContext>
{
    public const string DesignTimeConnectionString = "Host=design-time-only;Database=zentory";

    public ZentoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseNpgsql(
                DesignTimeConnectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(ZentoryDbContext).Assembly.FullName))
            .Options;

        return new ZentoryDbContext(options);
    }
}
