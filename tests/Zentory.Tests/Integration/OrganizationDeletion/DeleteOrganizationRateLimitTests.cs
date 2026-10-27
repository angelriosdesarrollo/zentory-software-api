using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zentory.API.Authorization;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 CA-T11-3 (parte de CA-111; RF-81 f, g) sobre el host real con el límite reauth:{sub} por defecto y el reloj
// simulado: tras 5 intentos de DELETE /organization con contraseña incorrecta, el 6.º con la correcta responde 429 sin
// efecto; pasados 15 min responde 204. El contador es por sub, y la contraseña no aparece en logs, AuditLog ni respuestas.
public sealed class DeleteOrganizationRateLimitTests : IClassFixture<ReauthRateLimitApiFactory>, IDisposable
{
    private const string OwnerPassword = "Owner-Correcta-Limite-2026!";
    private const string WrongPassword = "Incorrecta-Limite-Trampa-2026!";
    private const int    Attempts      = 5;

    private readonly ReauthRateLimitApiFactory      _factory;
    private readonly FakeTimeProvider               _time;
    private readonly CapturingLoggerProvider        _logs = new();
    private readonly WebApplicationFactory<Program> _host;

    public DeleteOrganizationRateLimitTests(ReauthRateLimitApiFactory factory)
    {
        _factory = factory;
        _time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(_logs))
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_time);
            }));
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task PLAN0018_CA111_DeleteOrganization_TrasCincoFallosEn15Min_ElSextoCorrectoResponde429YPasados15Min204()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithOwnerPassword(OwnerPassword))
            .ForB(b => b.WithOwnerPassword(OwnerPassword))
            .BuildAsync();
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);
        var bodies = new List<string>();

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            using var failed = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, WrongPassword);
            Assert.Equal(HttpStatusCode.Forbidden, failed.StatusCode);
            bodies.Add(await failed.Content.ReadAsStringAsync());
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        using (var limited = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, OwnerPassword))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            var text = await limited.Content.ReadAsStringAsync();
            bodies.Add(text);
            Assert.Contains(AuthRateLimitAttribute.RateLimitedCode, text, StringComparison.Ordinal);
            Assert.True(int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After"))) > 0);
        }
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).DeletedAt);

        // El contador es por sub: otro owner no queda limitado.
        using (var other = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.B))
        using (var otherResponse = await OrganizationDeletionApi.DeleteAsync(other, tenants.B.Organization.Name, OwnerPassword))
            Assert.Equal(HttpStatusCode.NoContent, otherResponse.StatusCode);

        // 15 min después del primer intento la ventana deja salir uno.
        _time.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromMinutes(Attempts) + TimeSpan.FromSeconds(1));
        using (var allowed = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, OwnerPassword))
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.NotNull((await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).DeletedAt);

        // RF-81 g: ni logs, ni AuditLog, ni respuestas contienen la contraseña; cada fallo dejó su fila con motivo genérico.
        foreach (var secret in new[] { WrongPassword, OwnerPassword })
        {
            Assert.DoesNotContain(_logs.Entries, entry => entry.Message.Contains(secret, StringComparison.Ordinal));
            Assert.DoesNotContain(bodies, body => body.Contains(secret, StringComparison.Ordinal));
        }
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var audit = await db.AuditLogs.Where(a => a.UserId == tenants.A.Owner.UserId).ToListAsync();
        Assert.Equal(Attempts, audit.Count(a => a.Action == ReauthenticationVerifier.AuditAction));
        Assert.All(audit, row =>
        {
            Assert.DoesNotContain(WrongPassword, row.NewValues ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(OwnerPassword, row.NewValues ?? string.Empty, StringComparison.Ordinal);
        });
        Assert.All(audit.Where(a => a.Action == ReauthenticationVerifier.AuditAction),
            row => Assert.Contains(ReauthenticationVerifier.AuditReason, row.NewValues, StringComparison.Ordinal));
    }
}
