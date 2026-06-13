using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zentory.Infrastructure.Persistence;

/// <summary>
/// Used only by EF Core design-time tools (dotnet ef migrations add, update-database).
/// Not part of the production DI container.
/// </summary>
public sealed class ZentoryDbContextFactory : IDesignTimeDbContextFactory<ZentoryDbContext>
{
    public ZentoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=zentory_dev;Username=postgres;Password=postgres",
                npgsql => npgsql.MigrationsAssembly(typeof(ZentoryDbContext).Assembly.FullName))
            .Options;

        return new ZentoryDbContext(options);
    }
}
