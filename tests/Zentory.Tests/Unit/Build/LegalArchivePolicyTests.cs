using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Options;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Common;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 CA-T12-3 (parte de CA-108, que se cierra en T13b; RF-79; SR-0018.28; ADR purga §5): LegalDocumentArchive no
// es de tenant (ni TenantEntity, ni OrganizationId, ni FK, ni filtro global), ningún DTO ni controlador la referencia y
// ningún código de src/ la lee con IgnoreQueryFilters.
public sealed partial class LegalArchivePolicyTests
{
    private const string SrcFolder         = "src";
    private const string ControllersFolder = "src/Zentory.API/Controllers/";

    [Fact]
    public void PLAN0018_CA108_LegalDocumentArchive_NoEsTenantEntityNiTieneOrganizationId()
    {
        Assert.False(typeof(TenantEntity).IsAssignableFrom(typeof(LegalDocumentArchive)));
        Assert.Null(typeof(LegalDocumentArchive).GetProperty("OrganizationId"));
    }

    [Fact]
    public void PLAN0018_CA108_LegalDocumentArchive_EnElModeloSinForeignKeysNiFiltroGlobalNiOrganizationId()
    {
        using var db = TestDbContextFactory.Create();
        var entity = db.Model.FindEntityType(typeof(LegalDocumentArchive));

        Assert.NotNull(entity);
        Assert.Empty(entity!.GetForeignKeys());
        Assert.Empty(entity.GetReferencingForeignKeys());
        Assert.Null(entity.GetQueryFilter());
        Assert.Null(entity.FindProperty("OrganizationId"));
        Assert.Equal("core", entity.GetSchema());
        Assert.Equal("legal_document_archive", entity.GetTableName());
    }

    [Fact]
    public void PLAN0018_CA108_Src_NingunDtoNiControladorReferenciaElArchivoLegal()
    {
        var offending = SourceFiles()
            .Where(file => IsDtoOrController(file) && ArchiveReference().IsMatch(StripComments(Read(file))))
            .ToList();

        Assert.True(offending.Count == 0,
            "DTO o controlador que referencia LegalDocumentArchive (SR-0018.28): " + string.Join(", ", offending));
    }

    // Por reflexión, porque muchos DTO viven dentro de archivos de Queries/Commands: todo tipo *Dto de Application y API,
    // y todo tipo que entra o sale de una acción de controlador (retorno, parámetros y ProducesResponseType), no puede
    // tener propiedades, campos ni parámetros de constructor cuyo tipo sea o contenga LegalDocumentArchive.
    [Fact]
    public void PLAN0018_CA108_Ensamblados_NingunDtoNiAccionDeControladorExponeElArchivoLegal()
    {
        var offending = DtoTypes(ApplicationAssembly).Concat(DtoTypes(ApiAssembly))
            .Select(type => (Origin: type.FullName ?? type.Name, Type: type))
            .Concat(ControllerActionTypes(ApiAssembly))
            .Where(item => ReferencesArchive(item.Type))
            .Select(item => item.Origin)
            .Distinct()
            .ToList();

        Assert.True(offending.Count == 0,
            "DTO o acción de controlador que expone LegalDocumentArchive (SR-0018.28): " + string.Join(", ", offending));
        Assert.NotEmpty(DtoTypes(ApplicationAssembly));
        Assert.NotEmpty(ControllerActionTypes(ApiAssembly));
    }

    [Fact]
    public void PLAN0018_CA108_DetectorPorReflexion_EncuentraDtosAnidadosYAccionesQueExponenElArchivo()
    {
        // Control del anterior sobre tipos de prueba anidados en esta clase (no son parte de src/).
        Assert.Contains(typeof(ProbeArchiveDto), DtoTypes(typeof(LegalArchivePolicyTests).Assembly));
        Assert.True(ReferencesArchive(typeof(ProbeArchiveDto)));
        Assert.True(ReferencesArchive(typeof(ProbeArchiveListDto)));
        Assert.True(ReferencesArchive(typeof(ProbeArchiveArrayDto)));
        Assert.True(ReferencesArchive(typeof(ProbeArchiveNestedDto)));
        Assert.True(ReferencesArchive(typeof(Task<ActionResult<Dictionary<string, LegalDocumentArchive?>>>)));
        Assert.False(ReferencesArchive(typeof(ProbeCleanDto)));

        var actionTypes = ControllerActionTypes(typeof(LegalArchivePolicyTests).Assembly)
            .Where(item => item.Origin.Contains(nameof(ProbeArchiveController), StringComparison.Ordinal))
            .ToList();
        Assert.Contains(actionTypes, item => ReferencesArchive(item.Type));
    }

    [Fact]
    public void PLAN0018_CA108_Src_NingunaLecturaDelArchivoLegalUsaIgnoreQueryFilters()
    {
        var offending = SourceFiles()
            .Where(file => ArchiveWithIgnoreQueryFilters().IsMatch(StripComments(Read(file))))
            .ToList();

        Assert.True(offending.Count == 0,
            "LegalDocumentArchive leída con IgnoreQueryFilters() (no tiene filtro que saltar): " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA108_Detectores_EncuentranReferenciasEIgnoreQueryFiltersSobreElArchivo()
    {
        // Control de los dos anteriores sobre textos de ejemplo, no sobre src/.
        Assert.Matches(ArchiveReference(), "public sealed record ArchiveDto(LegalDocumentArchive Item);");
        Assert.Matches(ArchiveReference(), "var t = LegalDocumentSourceType.Invoice;");
        Assert.DoesNotMatch(ArchiveReference(), StripComments("// LegalDocumentArchive en un comentario no cuenta"));
        Assert.Matches(ArchiveWithIgnoreQueryFilters(),
            "await db.LegalDocumentArchives\n    .IgnoreQueryFilters()\n    .ToListAsync(ct);");
        Assert.Matches(ArchiveWithIgnoreQueryFilters(), "db.Set<LegalDocumentArchive>().IgnoreQueryFilters().Where(x => true);");
        Assert.DoesNotMatch(ArchiveWithIgnoreQueryFilters(),
            "db.LegalDocumentArchives.Where(a => a.RetainUntil < now); db.Clients.IgnoreQueryFilters();");
        Assert.True(IsDtoOrController("src/Zentory.Application/Invoices/DTOs/InvoiceDto.cs"));
        Assert.True(IsDtoOrController("src/Zentory.API/Controllers/V1/InvoicesController.cs"));
        Assert.False(IsDtoOrController("src/Zentory.Infrastructure/Persistence/Purge/OrganizationDataCatalog.cs"));
    }

    // CA-T12B-1 (SR-0018.28 b): ninguna llamada a GeneratePresignedDownloadUrlAsync recibe claves de legal-archive/. Las
    // llamadas de src/ firman claves que salen de filas del tenant (StorageKey, DocumentUrl); un archivo que llame al
    // método no puede nombrar el archivo legal, su prefijo ni su PdfStorageKey en ninguna parte.
    [Fact]
    public void PLAN0018_CA108_Src_NingunaUrlFirmadaDeDescargaRecibeClavesDelArchivoLegal()
    {
        var callers = SourceFiles()
            .Where(file => !file.EndsWith("/IStorageService.cs", StringComparison.Ordinal))
            .Select(file => (File: file, Code: StripComments(Read(file))))
            .Where(item => PresignedDownloadCall().IsMatch(item.Code))
            .ToList();
        var offending = callers
            .Where(item => LegalArchiveKeyReference().IsMatch(item.Code) || ArchiveReference().IsMatch(item.Code))
            .Select(item => item.File)
            .ToList();

        Assert.NotEmpty(callers);
        Assert.True(offending.Count == 0,
            "GeneratePresignedDownloadUrlAsync en un archivo que referencia el archivo legal (SR-0018.28 b): " +
            string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA108_DetectorDeUrlFirmada_EncuentraLlamadasConClavesDelArchivoLegal()
    {
        // Control del anterior sobre textos de ejemplo.
        const string withArchive = "await _storage.GeneratePresignedDownloadUrlAsync(archive.PdfStorageKey!, ttl, ct);";
        const string withPrefix  = "await _storage.GeneratePresignedDownloadUrlAsync(\"legal-archive/\" + id, ttl, ct);";
        const string clean       = "await _storage.GeneratePresignedDownloadUrlAsync(file.StorageKey, ttl, ct);";

        Assert.Matches(PresignedDownloadCall(), withArchive);
        Assert.Matches(LegalArchiveKeyReference(), withArchive);
        Assert.Matches(LegalArchiveKeyReference(), withPrefix);
        Assert.Matches(PresignedDownloadCall(), clean);
        Assert.DoesNotMatch(LegalArchiveKeyReference(), clean);
        Assert.DoesNotMatch(ArchiveReference(), clean);
    }

    [Fact]
    public void PLAN0018_CA108_ClaveDelPdfArchivado_EsLegalArchiveGuidPdfSinOtrosDatos()
    {
        var key = LegalDocumentArchive.NewPdfStorageKey();

        Assert.Matches(PdfKeyShape(), key);
        Assert.NotEqual(key, LegalDocumentArchive.NewPdfStorageKey());
    }

    [Fact]
    public void PLAN0018_CA108_LegalDocumentArchive_RetainUntilEsIssuedAtMasLosAniosYValidaSusEntradas()
    {
        var issuedAt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var archived = LegalDocumentArchive.Create(LegalDocumentSourceType.Invoice, Guid.NewGuid(), "FE-1", issuedAt,
            "900123456", "Emisor SAS", null, "{}", null, issuedAt.AddDays(40), retentionYears: 5);

        Assert.Equal(issuedAt.AddYears(5), archived.RetainUntil);
        Assert.False(archived.IsExpired(issuedAt.AddYears(5)));
        Assert.True(archived.IsExpired(issuedAt.AddYears(5).AddTicks(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => LegalDocumentArchive.Create(LegalDocumentSourceType.Invoice,
            Guid.NewGuid(), "FE-1", issuedAt, "900123456", "Emisor SAS", null, "{}", null, issuedAt, retentionYears: 0));
        Assert.Throws<ArgumentException>(() => LegalDocumentArchive.Create(LegalDocumentSourceType.Invoice,
            Guid.Empty, "FE-1", issuedAt, "900123456", "Emisor SAS", null, "{}", null, issuedAt, retentionYears: 5));
        Assert.Throws<ArgumentException>(() => LegalDocumentArchive.Create("otro", Guid.NewGuid(), "FE-1", issuedAt,
            "900123456", "Emisor SAS", null, "{}", null, issuedAt, retentionYears: 5));
        Assert.Throws<ArgumentException>(() => LegalDocumentArchive.Create(LegalDocumentSourceType.CollaboratorPayoutInvoice,
            Guid.NewGuid(), "CC-1", issuedAt, "1020304050", "Colaborador", null, "{}", $"{Guid.NewGuid()}/cc.pdf",
            issuedAt, retentionYears: 5));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly Assembly ApplicationAssembly = typeof(RetentionOptions).Assembly;
    private static readonly Assembly ApiAssembly         = typeof(Program).Assembly;

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static List<Type> DtoTypes(Assembly assembly)
        => LoadableTypes(assembly).Where(type => type.Name.EndsWith("Dto", StringComparison.Ordinal)).ToList();

    // Tipos que entran o salen de cada acción de controlador: retorno, parámetros y [ProducesResponseType(typeof(...))].
    private static List<(string Origin, Type Type)> ControllerActionTypes(Assembly assembly)
        => LoadableTypes(assembly)
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(controller => controller.GetMethods(AllMembers | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .SelectMany(method =>
                    new[] { method.ReturnType }
                        .Concat(method.GetParameters().Select(parameter => parameter.ParameterType))
                        .Concat(method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(a => a.Type))
                        .Where(type => type is not null)
                        .Select(type => ($"{controller.FullName}.{method.Name}", type!))))
            .ToList();

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception) { return exception.Types.OfType<Type>(); }
    }

    // ¿El tipo es o contiene LegalDocumentArchive? Recorre arrays, nullable, argumentos genéricos (colecciones, Task,
    // ActionResult, diccionarios) y, para los tipos propios de Zentory, sus propiedades, campos y parámetros de
    // constructor (un DTO que anida otro DTO que lo expone también cuenta).
    private static bool ReferencesArchive(Type type) => ReferencesArchive(type, []);

    private static bool ReferencesArchive(Type type, HashSet<Type> visited)
    {
        if (type == typeof(LegalDocumentArchive))
            return true;
        if (!visited.Add(type))
            return false;
        if (type.HasElementType)
            return ReferencesArchive(type.GetElementType()!, visited);
        if (type.IsGenericType && type.GetGenericArguments().Any(argument => ReferencesArchive(argument, visited)))
            return true;
        if (type.Namespace?.StartsWith("Zentory", StringComparison.Ordinal) != true || type.IsEnum)
            return false;

        return type.GetProperties(AllMembers).Any(property => ReferencesArchive(property.PropertyType, visited))
            || type.GetFields(AllMembers).Any(field => ReferencesArchive(field.FieldType, visited))
            || type.GetConstructors(AllMembers).SelectMany(ctor => ctor.GetParameters())
                .Any(parameter => ReferencesArchive(parameter.ParameterType, visited));
    }

    private sealed record ProbeArchiveDto(LegalDocumentArchive Item);
    private sealed record ProbeArchiveListDto(IReadOnlyList<LegalDocumentArchive?> Items);
    private sealed record ProbeArchiveArrayDto(Guid Id, LegalDocumentArchive[] Items);
    private sealed record ProbeArchiveNestedDto(string Name, ProbeArchiveListDto Inner);
    private sealed record ProbeCleanDto(Guid Id, string Name, IReadOnlyList<DateTime?> Dates);

    private sealed class ProbeArchiveController : ControllerBase
    {
        public ActionResult<IEnumerable<LegalDocumentArchive>> List() => Ok();
    }

    [GeneratedRegex(@"\bLegalDocument(Archive|SourceType)\w*\b")]
    private static partial Regex ArchiveReference();

    // Una sentencia (hasta el siguiente ';') que parte del archivo legal y llama a IgnoreQueryFilters.
    [GeneratedRegex(@"(LegalDocumentArchives\b|Set<LegalDocumentArchive>\(\))[^;]*\.IgnoreQueryFilters\(", RegexOptions.Singleline)]
    private static partial Regex ArchiveWithIgnoreQueryFilters();

    [GeneratedRegex(@"^legal-archive/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.pdf$")]
    private static partial Regex PdfKeyShape();

    [GeneratedRegex(@"\bGeneratePresignedDownloadUrlAsync\s*\(")]
    private static partial Regex PresignedDownloadCall();

    [GeneratedRegex(@"legal-archive|\bPdfKeyPrefix\b|\bPdfStorageKey\b", RegexOptions.IgnoreCase)]
    private static partial Regex LegalArchiveKeyReference();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    private static string StripComments(string content) => LineComment().Replace(content, string.Empty);

    private static bool IsDtoOrController(string file)
        => file.StartsWith(ControllersFolder, StringComparison.Ordinal)
            || file.Contains("/DTOs/", StringComparison.Ordinal)
            || file.Contains("/Dtos/", StringComparison.Ordinal)
            || Path.GetFileNameWithoutExtension(file).EndsWith("Dto", StringComparison.Ordinal);

    private static string Read(string file) => File.ReadAllText(Path.Combine(RepoRoot.Path, file));

    // Archivos .cs de src/, relativos al repo con '/', sin bin/ ni obj/.
    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, SrcFolder), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepoRoot.Path, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal));
}
