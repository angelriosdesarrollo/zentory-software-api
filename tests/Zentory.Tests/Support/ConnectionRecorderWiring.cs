using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Zentory.Tests.Support;

// Conecta un ConnectionOpenRecorder al host real y comprueba que el DbContext lo usa, para los tests de arranque que
// prueban "0 intentos de conexión" (PLAN-0018 CA-10, CA-39). Extraído de DatabaseStartupTests.
internal static class ConnectionRecorderWiring
{
    // El contador de conexiones entra como un IInterceptor más (D-3): el DbContext lo recoge de DI junto al de auditoría.
    public static WebApplicationFactory<Program> WithConnectionRecorder(
        this WebApplicationFactory<Program> host, ConnectionOpenRecorder recorder)
        => host.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddSingleton<IInterceptor>(recorder)));

    // Sin esta comprobación, 0 intentos podría significar solo que el contador nunca llegó al pipeline del DbContext.
    public static void AssertWired(DbContext db, ConnectionOpenRecorder recorder)
    {
        var interceptors = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        Assert.Contains(recorder, interceptors);
    }
}
