using System.Net;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Extensions;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Extensions;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Purge;

// PLAN-0018 CA-T12B-1 (parte de CA-108, que se cierra en T13b; RF-78, RF-79 c; SR-0018.28): el job OrganizationPurge solo
// queda registrado con Hangfire:ServerEnabled y con cron diario, y el controlador de almacenamiento local de desarrollo no
// sirve ni recibe claves de legal-archive/.
//
// Sin paralelismo, como HangfireFlagTests (R-6): un host con el servidor de Hangfire encendido no debe convivir con otro
// que inspecciona su storage.
[Collection(NonParallelCollection.Name)]
public sealed class OrganizationPurgeJobTests
{
    private const string DevStorageRoute = "/api/v1/public/dev/local-storage/";

    [Fact]
    public async Task PLAN0018_CA108_HangfireServerDisabled_OrganizationPurgeNoQuedaRegistrado()
    {
        await using var factory = new ZentoryApiFactory();
        using var client = factory.CreateClient();

        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        Assert.DoesNotContain(connection.GetRecurringJobs(), job => job.Id == RecurringJobIds.OrganizationPurge);
    }

    [Fact]
    public async Task PLAN0018_CA108_HangfireServerEnabled_OrganizationPurgeQuedaRegistradoConCronDiario()
    {
        await using var factory = new MinimalHostFactory(AppEnvironments.Testing, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "true",
            [DatabaseSettings.InMemoryNameKey]  = $"zentory-purge-job-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey] = "true",
            [DevSeedSettings.EnabledKey]        = "false",
        });
        using var client = factory.CreateClient();

        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        var job = Assert.Single(connection.GetRecurringJobs(), j => j.Id == RecurringJobIds.OrganizationPurge);
        Assert.Equal(RecurringJobSchedules.OrganizationPurge, job.Cron);
        Assert.Equal("0 3 * * *", job.Cron);
        Assert.Contains(connection.GetRecurringJobs(), j => j.Id == RecurringJobIds.MonthlyPilaAutoRequest);
    }

    [Fact]
    public async Task PLAN0018_CA108_DevLocalStorage_GetDeLegalArchiveResponde404SinContenidoYOtraClaveSeSirve()
    {
        await using var factory = DevelopmentHost();
        using var client = factory.CreateClient();
        var archiveKey = LegalDocumentArchive.NewPdfStorageKey();
        var otherKey   = $"{Guid.NewGuid()}/docs/permitido.pdf";
        WriteDevFile(factory, archiveKey, "%PDF-archivo-legal");
        WriteDevFile(factory, otherKey, "%PDF-permitido");

        using var archived = await client.GetAsync(DevStorageRoute + archiveKey);
        using var upperCase = await client.GetAsync(DevStorageRoute + archiveKey.Replace("legal-archive", "LEGAL-ARCHIVE"));
        using var allowed  = await client.GetAsync(DevStorageRoute + otherKey);

        Assert.True(archived.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"{(int)archived.StatusCode}");
        Assert.Empty(await archived.Content.ReadAsByteArrayAsync());
        Assert.True(upperCase.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"{(int)upperCase.StatusCode}");
        Assert.DoesNotContain("archivo-legal", await upperCase.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal("%PDF-permitido", await allowed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PLAN0018_CA108_DevLocalStorage_PutEnLegalArchiveResponde404YNoEscribeElArchivo()
    {
        await using var factory = DevelopmentHost();
        using var client = factory.CreateClient();
        var archiveKey = LegalDocumentArchive.NewPdfStorageKey();

        using var response = await client.PutAsync(DevStorageRoute + archiveKey, new ByteArrayContent("%PDF-x"u8.ToArray()));

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"{(int)response.StatusCode}");
        Assert.False(File.Exists(DevPath(factory, archiveKey)));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Development con InMemory y raíz de contenido temporal: el controlador solo responde en Development.
    private static MinimalHostFactory DevelopmentHost() => new("Development", new Dictionary<string, string?>
    {
        [DatabaseSettings.UseInMemoryKey]   = "true",
        [DatabaseSettings.InMemoryNameKey]  = $"zentory-dev-storage-{Guid.NewGuid():N}",
        [HangfireSettings.ServerEnabledKey] = "false",
        [DevSeedSettings.EnabledKey]        = "false",
    });

    private static string DevPath(MinimalHostFactory factory, string key)
        => Path.Combine([factory.ContentRootPath, "App_Data", "dev-storage", .. key.Split('/')]);

    private static void WriteDevFile(MinimalHostFactory factory, string key, string content)
    {
        var path = DevPath(factory, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
