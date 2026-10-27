using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Storage;

// PLAN-0018 CA-T12-2 (parte de CA-107, que se cierra en T12b; RF-78 5; SR-0018.27; ADR purga §6): el borrado de los
// archivos de una organización solo acepta el prefijo exacto "{organizationId}/". "", "/" o el Guid sin barra lanzan
// antes de listar, sin borrar nada. ListKeysAsync y CopyAsync (para el archivo legal) se comportan igual en el doble y en
// el almacenamiento local en disco.
public sealed class StorageKeyTests
{
    private static readonly Guid A = Guid.Parse("6f1c2a52-9d4e-4f7a-8b3c-1e2d3f4a5b6c");
    private static readonly Guid B = Guid.Parse("0a9b8c7d-6e5f-4a3b-9c1d-2e3f4a5b6c7d");

    public static TheoryData<string> InvalidPrefixes => new()
    {
        "",
        "/",
        A.ToString(),                       // "{A}" sin barra: también casaría con "{A}x/..."
        $"{Guid.Empty}/",
        $"{A.ToString().ToUpperInvariant()}/",
        $"{A:N}/",
        $"{A}/project-files/",
        $"/{A}/",
        $"{A}//",
    };

    [Theory]
    [MemberData(nameof(InvalidPrefixes))]
    public async Task PLAN0018_CA107_BorradoPorPrefijo_ConPrefijoInvalido_LanzaSinBorrarNada(string prefix)
    {
        var storage = SeededStorage();
        var before  = storage.Objects.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        await Assert.ThrowsAsync<ArgumentException>(() => ((IStorageService)storage).DeleteOrganizationFilesAsync(prefix));

        Assert.Equal(before, storage.Objects.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task PLAN0018_CA107_BorradoPorPrefijo_ConGuidYBarra_BorraSoloLosDeEsaOrganizacionYEsReintentable()
    {
        var storage = SeededStorage();
        IStorageService service = storage;

        var deleted = await service.DeleteOrganizationFilesAsync(OrganizationStoragePrefix.For(A));

        Assert.Equal(3, deleted);
        Assert.DoesNotContain(storage.Objects.Keys, key => key.StartsWith($"{A}/", StringComparison.Ordinal));
        Assert.Contains($"{B}/otros/{A}.pdf", storage.Objects.Keys);
        Assert.Contains($"{B}/project-files/x.pdf", storage.Objects.Keys);
        Assert.Contains($"{A}.pdf", storage.Objects.Keys);
        Assert.Contains("legal-archive/archivado.pdf", storage.Objects.Keys);
        Assert.Equal(0, await service.DeleteOrganizationFilesAsync(OrganizationStoragePrefix.For(A)));
    }

    [Fact]
    public void PLAN0018_CA107_PrefijoDeOrganizacion_EsGuidCanonicoConBarraYRechazaGuidVacio()
    {
        Assert.Equal($"{A}/", OrganizationStoragePrefix.For(A));
        Assert.True(OrganizationStoragePrefix.IsValid(OrganizationStoragePrefix.For(A)));
        Assert.False(OrganizationStoragePrefix.IsValid(null));
        Assert.Throws<ArgumentException>(() => OrganizationStoragePrefix.For(Guid.Empty));
    }

    [Fact]
    public async Task PLAN0018_CA107_FakeStorage_ListaPorPrefijoOrdinalYCopiaConservandoElOrigen()
    {
        var storage = SeededStorage();

        var keys = await storage.ListKeysAsync($"{A}/");
        Assert.Equal(new[] { $"{A}/huerfano.bin", $"{A}/payouts/cc-1.pdf", $"{A}/project-files/plano.pdf" }, keys);
        Assert.Empty(await storage.ListKeysAsync($"{A.ToString().ToUpperInvariant()}/"));

        await storage.CopyAsync($"{A}/payouts/cc-1.pdf", "legal-archive/copia.pdf");
        Assert.Equal(storage.Objects[$"{A}/payouts/cc-1.pdf"].Content, storage.Objects["legal-archive/copia.pdf"].Content);
        Assert.Contains($"{A}/payouts/cc-1.pdf", storage.Objects.Keys);
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.CopyAsync($"{A}/no-existe.pdf", "legal-archive/x.pdf"));
    }

    [Fact]
    public async Task PLAN0018_CA107_LocalDiskStorage_ListaCopiaYBorraPorPrefijoComoElDoble()
    {
        var root = Directory.CreateTempSubdirectory("zentory-local-storage-").FullName;
        try
        {
            IStorageService storage = new LocalDiskStorageService(new TempWebHostEnvironment(root), new HttpContextAccessor());
            foreach (var key in new[] { $"{A}/payouts/cc-1.pdf", $"{A}/huerfano.bin", $"{B}/otros/{A}.pdf", $"{A}.pdf" })
                await storage.UploadAsync(key, new MemoryStream([1, 2, 3]), "application/pdf");

            Assert.Equal(new[] { $"{A}/huerfano.bin", $"{A}/payouts/cc-1.pdf" }, await storage.ListKeysAsync($"{A}/"));

            await storage.CopyAsync($"{A}/payouts/cc-1.pdf", "legal-archive/copia.pdf");
            Assert.Equal(new[] { "legal-archive/copia.pdf" }, await storage.ListKeysAsync("legal-archive/"));

            await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteOrganizationFilesAsync(A.ToString()));
            Assert.Equal(2, (await storage.ListKeysAsync($"{A}/")).Count);

            Assert.Equal(2, await storage.DeleteOrganizationFilesAsync($"{A}/"));
            Assert.Empty(await storage.ListKeysAsync($"{A}/"));
            Assert.Equal(new[] { $"{B}/otros/{A}.pdf" }, await storage.ListKeysAsync($"{B}/"));
            Assert.Single(await storage.ListKeysAsync($"{A}.pdf"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // El método por defecto de IStorageService es la única validación del prefijo: ninguna implementación de src/ ni de
    // tests/ puede declarar el suyo (ni implícito ni explícito), porque podría saltarse EnsureValid.
    [Fact]
    public void PLAN0018_CA107_ImplementacionesDeIStorageService_NoSobrescribenElBorradoPorPrefijo()
    {
        var implementations = StorageImplementations().ToList();
        var offending = implementations.Where(OverridesOrganizationDeletion).Select(type => type.FullName).ToList();

        Assert.Contains(typeof(FakeStorageService), implementations);
        Assert.Contains(typeof(LocalDiskStorageService), implementations);
        Assert.Contains(typeof(CloudflareR2StorageService), implementations);
        Assert.True(offending.Count == 0,
            "Implementación de IStorageService con su propio DeleteOrganizationFilesAsync: " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA107_DetectorDeSobrescritura_EncuentraImplementacionesImplicitasYExplicitas()
    {
        // Control del anterior sobre dos implementaciones de prueba anidadas en esta clase.
        Assert.True(OverridesOrganizationDeletion(typeof(ImplicitOverrideProbe)));
        Assert.True(OverridesOrganizationDeletion(typeof(ExplicitOverrideProbe)));
        Assert.False(OverridesOrganizationDeletion(typeof(FakeStorageService)));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Archivos de A (con fila y sin fila asociada), de B (uno con el Guid de A en el nombre), uno en la raíz que empieza
    // por el Guid de A sin barra y uno del archivo legal.
    private static FakeStorageService SeededStorage()
    {
        var storage = new FakeStorageService();
        storage.Seed($"{A}/project-files/plano.pdf", "application/pdf", [1]);
        storage.Seed($"{A}/payouts/cc-1.pdf", "application/pdf", [2, 3]);
        storage.Seed($"{A}/huerfano.bin");
        storage.Seed($"{B}/otros/{A}.pdf", "application/pdf", [4]);
        storage.Seed($"{B}/project-files/x.pdf", "application/pdf", [5]);
        storage.Seed($"{A}.pdf", "application/pdf", [6]);
        storage.Seed("legal-archive/archivado.pdf", "application/pdf", [7]);
        return storage;
    }

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static IEnumerable<Type> StorageImplementations()
        => new[]
            {
                typeof(LegalDocumentArchive).Assembly, typeof(IStorageService).Assembly,
                typeof(LocalDiskStorageService).Assembly, typeof(Program).Assembly, typeof(StorageKeyTests).Assembly,
            }
            .Distinct()
            .SelectMany(LoadableTypes)
            .Where(type => typeof(IStorageService).IsAssignableFrom(type) && !type.IsInterface)
            .Where(type => type.DeclaringType != typeof(StorageKeyTests));

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception) { return exception.Types.OfType<Type>(); }
    }

    private static bool OverridesOrganizationDeletion(Type type)
    {
        const string name = nameof(IStorageService.DeleteOrganizationFilesAsync);
        var declares = type.GetMethods(AllMembers | BindingFlags.DeclaredOnly)
            .Any(method => method.Name == name || method.Name.EndsWith("." + name, StringComparison.Ordinal));
        if (declares || type.IsAbstract)
            return declares;

        var map = type.GetInterfaceMap(typeof(IStorageService));
        return map.InterfaceMethods.Zip(map.TargetMethods)
            .Any(pair => pair.First.Name == name && pair.Second.DeclaringType != typeof(IStorageService));
    }

    // Implementaciones de prueba: solo importa que declaren su propio borrado por prefijo.
    private abstract class StorageProbeBase : IStorageService
    {
        public Task<PresignedUploadUrl> GeneratePresignedUploadUrlAsync(string key, string contentType, TimeSpan expiresIn,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GeneratePresignedDownloadUrlAsync(string key, TimeSpan expiresIn, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class ImplicitOverrideProbe : StorageProbeBase
    {
        public Task<int> DeleteOrganizationFilesAsync(string prefix, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class ExplicitOverrideProbe : StorageProbeBase, IStorageService
    {
        Task<int> IStorageService.DeleteOrganizationFilesAsync(string prefix, CancellationToken ct) => Task.FromResult(0);
    }

    // LocalDiskStorageService solo lee ContentRootPath.
    private sealed class TempWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string        ApplicationName         { get; set; } = "Zentory.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string        ContentRootPath         { get; set; } = contentRoot;
        public string        EnvironmentName         { get; set; } = AppEnvironments.Testing;
        public IFileProvider WebRootFileProvider     { get; set; } = new NullFileProvider();
        public string        WebRootPath             { get; set; } = contentRoot;
    }
}
