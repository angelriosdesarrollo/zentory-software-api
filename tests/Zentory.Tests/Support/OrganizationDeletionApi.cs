using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Apoyo de los tests de eliminación de organizaciones (PLAN-0018 RF-65, RF-81, RF-84): la petición DELETE /organization
// con cuerpo, el nonce de GET /auth/reauth-nonce, lecturas de comprobación que ven también las organizaciones eliminadas
// y la foto de conteos por tabla para probar que el borrado lógico no quita filas.
public static class OrganizationDeletionApi
{
    public const string Endpoint      = "/api/v1/organization";
    public const string NonceEndpoint = "/api/v1/auth/reauth-nonce";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    // Cuerpo tal como lo manda el frontend: solo las propiedades presentes.
    public static async Task<HttpResponseMessage> DeleteAsync(
        HttpClient http, string? confirmName, string? currentPassword = null, string? googleIdToken = null)
    {
        var body = new Dictionary<string, string>();
        if (confirmName is not null)     body["confirmName"]     = confirmName;
        if (currentPassword is not null) body["currentPassword"] = currentPassword;
        if (googleIdToken is not null)   body["googleIdToken"]   = googleIdToken;

        using var request = new HttpRequestMessage(HttpMethod.Delete, Endpoint) { Content = JsonContent.Create(body) };
        return await http.SendAsync(request);
    }

    // Cliente de un host derivado con el JWT real de ese usuario en esa organización (TenantPair.ClientFor usa la base).
    public static HttpClient ClientFor(WebApplicationFactory<Program> host, TenantPair tenants, SeededTenant tenant, User? user = null)
    {
        var http = host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenants.TokenFor(tenant, user));
        return http;
    }

    public static async Task<string> NonceAsync(HttpClient http, string action)
    {
        using var response = await http.GetAsync($"{NonceEndpoint}?action={action}");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("nonce").GetString()!;
    }

    // Lectura de comprobación sin filtros: la organización eliminada ya no la ve ninguna lectura normal.
    public static async Task<Organization> OrganizationAsync(IServiceProvider services, Guid organizationId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Organizations.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId);
    }

    // Filas por tipo del modelo (todas, también las de borrado lógico), sin los registros de auditoría y actividad, que
    // crecen a propósito con cada intento.
    public static async Task<IReadOnlyDictionary<string, int>> RowCountsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var type in db.Model.GetEntityTypes().Where(t => !t.IsOwned()))
        {
            if (type.ClrType == typeof(AuditLog) || type.ClrType == typeof(ActivityLog))
                continue;
            var count = CountMethod.MakeGenericMethod(type.ClrType);
            counts[type.Name] = await (Task<int>)count.Invoke(null, [db])!;
        }
        return counts;
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < WaitTimeout, because);
            await Task.Delay(25);
        }
    }

    private static readonly MethodInfo CountMethod =
        typeof(OrganizationDeletionApi).GetMethod(nameof(CountAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task<int> CountAsync<TEntity>(DbContext db) where TEntity : class
        => db.Set<TEntity>().IgnoreQueryFilters().CountAsync();
}
