using System.Linq.Expressions;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Security.Idor;

// Aserciones IDOR sobre el host real (PLAN-0018 RF-13; ADR-0001 §6; SR-1.2): la petición sale con el token de A y
// apunta a datos de B. Cubre las cuatro formas de SR-1.2: lectura por id y acción (CannotAccessAsync), listado
// (ListExcludesAsync) y agregado (AggregateUnaffectedAsync). Para las acciones que resuelven el recurso desde la
// organización activa (no reciben un id de B, p. ej. PATCH /organization/settings), LeavesBUnchangedAsync exige que las
// filas de B queden iguales. Las respuestas pasan por los filtros de tenant de la API; las únicas lecturas con
// IgnoreQueryFilters son las instantáneas de B, que prueban "sin efecto".
public static class IdorAssert
{
    private static readonly JsonSerializerOptions SnapshotJson = new() { WriteIndented = false };

    // Lectura por id o acción de A sobre un recurso de B: exige 403 o 404 y que el recurso de B quede igual que antes.
    // Devuelve el código para que el caso exija uno concreto cuando el criterio lo fija (p. ej. 404).
    public static async Task<HttpStatusCode> CannotAccessAsync<TEntity>(
        ZentoryApiFactory  factory,
        HttpClient         clientOfA,
        HttpRequestMessage request,
        Guid               resourceIdOfB)
        where TEntity : class
    {
        var before = await SnapshotAsync<TEntity>(factory, resourceIdOfB);
        Assert.True(before is not null, $"El recurso {typeof(TEntity).Name} {resourceIdOfB} de B no existe: el caso no prueba nada.");

        using var response = await clientOfA.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"{request.Method} {request.RequestUri} con el token de A sobre {typeof(TEntity).Name} de B respondió " +
            $"{(int)response.StatusCode}; se esperaba 403 o 404.");

        var after = await SnapshotAsync<TEntity>(factory, resourceIdOfB);
        Assert.True(before == after,
            $"{request.Method} {request.RequestUri} cambió {typeof(TEntity).Name} {resourceIdOfB} de B.");

        return response.StatusCode;
    }

    // Listado de A: responde 200 y no contiene ningún id ni marca de B. Devuelve el cuerpo para el control positivo.
    public static async Task<string> ListExcludesAsync(
        HttpClient            clientOfA,
        string                path,
        IEnumerable<Guid>     idsOfB,
        params string[]       markersOfB)
    {
        using var response = await clientOfA.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        foreach (var id in idsOfB)
            Assert.False(body.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase),
                $"GET {path} con el token de A incluye el id {id} de B.");
        foreach (var marker in markersOfB)
            Assert.False(body.Contains(marker, StringComparison.OrdinalIgnoreCase),
                $"GET {path} con el token de A incluye datos de B.");

        return body;
    }

    // Agregado de A: el resultado no cambia cuando B gana datos. Devuelve el cuerpo para el control positivo.
    public static async Task<string> AggregateUnaffectedAsync(HttpClient clientOfA, string path, Func<Task> addDataToB)
    {
        var before = await GetOkAsync(clientOfA, path);
        await addDataToB();
        var after = await GetOkAsync(clientOfA, path);

        Assert.True(before == after, $"GET {path} con el token de A cambió al agregar datos a B.");
        return after;
    }

    // Acción de A que resuelve su recurso desde la organización activa: la API puede aceptarla sobre lo de A, pero las
    // filas de B que cumplen el predicado (las de su organización) quedan iguales. Devuelve el código para que el caso
    // exija el esperado (p. ej. 204 sobre lo propio, 404 ante un id de B en el cuerpo o la query).
    public static async Task<HttpStatusCode> LeavesBUnchangedAsync<TEntity>(
        IServiceProvider                  services,
        HttpClient                        clientOfA,
        HttpRequestMessage                request,
        Expression<Func<TEntity, bool>>   rowsOfB)
        where TEntity : class
    {
        var before = await SnapshotWhereAsync(services, rowsOfB);
        Assert.True(before != "[]", $"B no tiene filas de {typeof(TEntity).Name}: el caso no prueba nada.");

        using var response = await clientOfA.SendAsync(request);

        var after = await SnapshotWhereAsync(services, rowsOfB);
        Assert.True(before == after,
            $"{request.Method} {request.RequestUri} con el token de A cambió filas de {typeof(TEntity).Name} de B.");
        return response.StatusCode;
    }

    public static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // Instantánea de las filas que cumplen el predicado (propiedades escalares, en orden estable), leídas sin filtros.
    public static async Task<string> SnapshotWhereAsync<TEntity>(IServiceProvider services, Expression<Func<TEntity, bool>> predicate)
        where TEntity : class
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var rows = await db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().Where(predicate).ToListAsync();

        var snapshots = rows
            .Select(row => JsonSerializer.Serialize(db.Entry(row).Properties
                .OrderBy(p => p.Metadata.Name, StringComparer.Ordinal)
                .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue), SnapshotJson))
            .Order(StringComparer.Ordinal)
            .ToList();
        return "[" + string.Join(",", snapshots) + "]";
    }

    // Instantánea de las propiedades escalares del recurso, leído sin filtros fuera de una petición. null si no existe.
    private static async Task<string?> SnapshotAsync<TEntity>(ZentoryApiFactory factory, Guid id)
        where TEntity : class
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var key = db.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()?.Properties.Single().Name
               ?? throw new InvalidOperationException($"{typeof(TEntity).Name} no es una entidad con clave simple.");

        var entity = await db.Set<TEntity>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => EF.Property<Guid>(e, key) == id);
        if (entity is null)
            return null;

        var values = db.Entry(entity).Properties
            .OrderBy(p => p.Metadata.Name, StringComparer.Ordinal)
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
        return JsonSerializer.Serialize(values, SnapshotJson);
    }
}

// Marca un test como el caso IDOR de una ruta: IdorRouteCatalogTests lee estas marcas como el catálogo. La ruta se
// escribe como la plantilla de ASP.NET (p. ej. "api/v1/invoices/{id:guid}/send").
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class IdorCaseAttribute(string httpMethod, string routeTemplate) : Attribute
{
    public string HttpMethod    { get; } = httpMethod;
    public string RouteTemplate { get; } = routeTemplate;

    public string Route => $"{HttpMethod} {RouteTemplate}";
}
