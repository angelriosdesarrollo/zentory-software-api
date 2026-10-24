using Microsoft.EntityFrameworkCore;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// ZentoryDbContext sobre EF InMemory, sin tenant (los filtros globales quedan solo por
// borrado lógico). Un nombre de base único por llamada aísla cada test de los demás.
public static class TestDbContextFactory
{
    public static ZentoryDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseInMemoryDatabase($"zentory-tests-{Guid.NewGuid()}")
            .Options;
        return new ZentoryDbContext(options);
    }
}
