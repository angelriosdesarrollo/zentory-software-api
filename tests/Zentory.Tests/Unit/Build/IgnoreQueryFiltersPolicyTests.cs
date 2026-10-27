using System.Text.RegularExpressions;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 RF-12 (parte de CA-75, que se cierra en T30c; ADR-0008 §3): saltarse los filtros globales deja de ser
// invisible. Un recorrido de src/ falla si IgnoreQueryFilters() aparece fuera de la lista blanca, y cada entrada nombra
// su motivo. Una entrada con miembro solo admite el uso dentro de ese método. La amplían T11, T11c, T12b, T29b (canje de invitación) y T30c (lecturas del destino de la
// transferencia y uso del aviso previo).
// Los tests no cuentan (lecturas de comprobación, IdorAssert): el recorrido es solo de src/.
public sealed partial class IgnoreQueryFiltersPolicyTests
{
    private sealed record AllowedUse(string Path, string? Member, string Reason);

    private static readonly AllowedUse[] Allowlist =
    [
        new("src/Zentory.Application/Auth/Services/IUserMembershipReader.cs", null,
            "Contrato de la única lectura de membresías del propio usuario entre organizaciones (RF-12)."),
        new("src/Zentory.Infrastructure/Services/UserMembershipReader.cs", null,
            "Membresías del propio usuario en todas sus organizaciones: listado, cambio de organización y emisión de " +
            "sesión. Predicado explícito por UserId y DeletedAt == null, y organización no eliminada."),
        new("src/Zentory.Infrastructure/Persistence/Repositories/CollaboratorRepository.cs", "ListByEmailAsync",
            "Portal de colaboradores: colaboraciones del email en todas las organizaciones (canje, cambio y solicitud " +
            "de enlace). Email normalizado, Status = active, DeletedAt == null y organización no eliminada."),
        new("src/Zentory.Application/ProjectShares/Queries/GetProjectSharePublicQuery.cs", null,
            "Lectura pública de un ProjectShare por token, sin sesión ni tenant."),
        new("src/Zentory.Infrastructure/Persistence/Repositories/ProjectShareRepository.cs", "GetByTokenAsync",
            "Búsqueda pública de un ProjectShare por token, sin sesión ni tenant."),
        new("src/Zentory.API/Middleware/MembershipValidationMiddleware.cs", "IsOrganizationDeletedForMemberAsync",
            "Middleware de membresía: distingue ORGANIZATION_DELETED de MEMBERSHIP_REVOKED (RF-65 c, D-31). Solo la " +
            "organización del claim, con DeletedAt != null y una membresía vigente del que llama."),
        new("src/Zentory.API/Middleware/MembershipValidationMiddleware.cs", "IsOrganizationDeletedAsync",
            "Middleware de membresía, sesión de colaborador: ORGANIZATION_DELETED en el portal (RF-65 d, D-31). Una " +
            "lectura por clave de la organización del claim, con DeletedAt != null."),
        new("src/Zentory.Infrastructure/Services/DeletedOrganizationReader.cs", "Restorable",
            "Organizaciones eliminadas del propio owner (RF-72, RF-85): listado, restauración y sesión sin organización. " +
            "Predicado explícito OwnerId == sub, DeletedAt != null, dentro del plazo y PurgeStartedAt == null."),
        new("src/Zentory.Infrastructure/Persistence/Purge/", null,
            "Purgador de organizaciones (RF-78, ADR purga §3): ve las filas con borrado lógico y la organización " +
            "eliminada. Cada lectura y borrado lleva el predicado explícito OrganizationId == orgId (o la clave de un " +
            "padre de esa organización), y la organización con DeletedAt != null."),
        new("src/Zentory.API/Cli/DbCli.cs", "HasUsersOrOrganizationsAsync",
            "db init (RF-63 d): corre sin tenant y decide si la base tiene datos; una organización con borrado lógico " +
            "también cuenta. Solo AnyAsync, sin leer ni devolver filas."),
        new("src/Zentory.Infrastructure/Persistence/Seeding/", null,
            "Seeders: corren sin tenant (el filtro ya está apagado) y comprueban la idempotencia por id o clave " +
            "natural, que debe ver también las filas con borrado lógico."),
        new("src/Zentory.Application/Auth/Commands/AcceptInvitationCommand.cs", "FindRedeemableAsync",
            "Canje de invitación (RF-50 c): quien acepta no está en esa organización y, con JWT, el filtro de tenant la " +
            "limitaría a su organización activa. Búsqueda por TokenHash == SHA-256 del token; IsPendingAt descarta las " +
            "aceptadas, revocadas, borradas y vencidas, y la organización debe seguir vigente."),
        new("src/Zentory.Application/Organization/Queries/ListMyOwnershipTransfersQuery.cs", "PendingForTarget",
            "Transferencias de propiedad cuyo destino es el que llama (RF-83 a, b, f, i): el destino puede tener activa " +
            "otra organización o ninguna. Predicado explícito ToUserId == sub y Status == pending; la vigencia la " +
            "comprueba quien llama con IsPendingAt."),
        new("src/Zentory.Application/Organization/Queries/ListMyOwnershipTransfersQuery.cs", "ActiveMembershipsAsync",
            "Aceptación de la transferencia (RF-83 c, e): membresías del destino y del proponente en la organización de " +
            "la propuesta, que puede no ser la activa del destino. Predicado explícito OrganizationId == la de la " +
            "propuesta ya validada para el sub y DeletedAt == null."),
        new("src/Zentory.Application/Organization/Services/OwnershipTransferPreview.cs", "UsageAsync",
            "Aviso previo de la transferencia (RF-77, RF-83 a, c): el uso de la organización debe ser el mismo, y el " +
            "previewHash también, desde la sesión del owner y desde la del destino con otra organización activa. " +
            "Predicado explícito OrganizationId == la de la propuesta y el borrado lógico de cada entidad."),
    ];

    [Fact]
    public void PLAN0018_CA75_Src_IgnoreQueryFiltersSoloEnLaListaBlanca()
    {
        var offending = SourceFiles()
            .SelectMany(file => UsageLines(Read(file)).Where(line => !IsAllowed(file, line)).Select(line => $"{file}:{line}"))
            .ToList();

        Assert.True(offending.Count == 0,
            "IgnoreQueryFilters() fuera de la lista blanca de ADR-0008 §3 (agrega la entrada con su motivo o usa los " +
            "filtros): " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA75_ListaBlanca_CadaEntradaExisteYNombraSuMotivo()
    {
        foreach (var entry in Allowlist)
        {
            var full = Path.Combine(RepoRoot.Path, entry.Path);
            Assert.True(File.Exists(full) || Directory.Exists(full), $"Entrada sin archivo: {entry.Path}");
            Assert.False(string.IsNullOrWhiteSpace(entry.Reason), $"Entrada sin motivo: {entry.Path}");
            if (entry.Member is not null)
                Assert.True(MemberRange(Read(entry.Path), entry.Member) is not null,
                    $"{entry.Path} no declara {entry.Member}.");
        }
    }

    [Fact]
    public void PLAN0018_CA75_ListaBlanca_IncluyeLasEntradasInicialesDeADR0008()
    {
        var paths = Allowlist.Select(entry => (entry.Path, entry.Member)).ToList();

        Assert.Contains(("src/Zentory.Application/Auth/Services/IUserMembershipReader.cs", (string?)null), paths);
        Assert.Contains(("src/Zentory.Infrastructure/Services/UserMembershipReader.cs", (string?)null), paths);
        Assert.Contains(("src/Zentory.Infrastructure/Persistence/Repositories/CollaboratorRepository.cs", "ListByEmailAsync"), paths);
        Assert.Contains(("src/Zentory.Infrastructure/Persistence/Repositories/ProjectShareRepository.cs", "GetByTokenAsync"), paths);
    }

    [Fact]
    public void PLAN0018_CA75_ListaBlanca_SumaLaComprobacionDeOrganizationDeletedDelMiddlewareConSuMotivo()
    {
        // T11 (CA-T11-4): las dos lecturas del middleware de membresía, cada una acotada a su método.
        const string middleware = "src/Zentory.API/Middleware/MembershipValidationMiddleware.cs";
        var entries = Allowlist.Where(entry => entry.Path == middleware).ToList();

        Assert.Equal(
            ["IsOrganizationDeletedAsync", "IsOrganizationDeletedForMemberAsync"],
            entries.Select(entry => entry.Member).Order(StringComparer.Ordinal));
        Assert.All(entries, entry => Assert.Contains("ORGANIZATION_DELETED", entry.Reason, StringComparison.Ordinal));

        // Fuera de esos dos métodos, el middleware no puede saltarse los filtros.
        var usages = UsageLines(Read(middleware));
        Assert.Equal(2, usages.Count);
        Assert.All(usages, line => Assert.True(IsAllowed(middleware, line)));
    }

    [Fact]
    public void PLAN0018_CA106_ListaBlanca_SumaElLectorDeEliminadasAcotadoASuPredicadoConSuMotivo()
    {
        // T11c: la única lectura de organizaciones eliminadas, acotada al método que arma el predicado de RF-72 d.
        const string reader = "src/Zentory.Infrastructure/Services/DeletedOrganizationReader.cs";
        var entry = Assert.Single(Allowlist, e => e.Path == reader);

        Assert.Equal("Restorable", entry.Member);
        Assert.Contains("OwnerId == sub", entry.Reason, StringComparison.Ordinal);
        Assert.Contains("PurgeStartedAt == null", entry.Reason, StringComparison.Ordinal);
        var usages = UsageLines(Read(reader));
        Assert.Single(usages);
        Assert.All(usages, line => Assert.True(IsAllowed(reader, line)));
    }

    [Fact]
    public void PLAN0018_CA107_ListaBlanca_SumaElPurgadorConSuPredicadoExplicitoPorOrganizationId()
    {
        // T12b (RF-12, RF-78): la carpeta del purgador es la única entrada nueva; sus usos quedan dentro de ella.
        const string purge = "src/Zentory.Infrastructure/Persistence/Purge/";
        var entry = Assert.Single(Allowlist, e => e.Path == purge);

        Assert.Null(entry.Member);
        Assert.Contains("OrganizationId == orgId", entry.Reason, StringComparison.Ordinal);
        var usages = SourceFiles()
            .Where(file => file.StartsWith(purge, StringComparison.Ordinal))
            .SelectMany(file => UsageLines(Read(file)).Select(line => (file, line)))
            .ToList();
        Assert.NotEmpty(usages);
        Assert.All(usages, usage => Assert.True(IsAllowed(usage.file, usage.line)));
        // Fuera de la carpeta, la entrada no cubre nada (p. ej. el resto de Persistence/).
        Assert.False(IsAllowed("src/Zentory.Infrastructure/Persistence/ZentoryDbContext.cs", 1));
    }

    [Fact]
    public void PLAN0018_CA75_ListaBlanca_SumaElCanjeDeInvitacionAcotadoALaBusquedaPorTokenHashConSuMotivo()
    {
        // T29b (RF-50 c): una sola lectura sin filtros, dentro del método que busca la invitación por el hash del token.
        const string command = "src/Zentory.Application/Auth/Commands/AcceptInvitationCommand.cs";
        var entry = Assert.Single(Allowlist, e => e.Path == command);

        Assert.Equal("FindRedeemableAsync", entry.Member);
        Assert.Contains("TokenHash", entry.Reason, StringComparison.Ordinal);
        Assert.Contains("IsPendingAt", entry.Reason, StringComparison.Ordinal);
        var usages = UsageLines(Read(command));
        Assert.Single(usages);
        Assert.All(usages, line => Assert.True(IsAllowed(command, line)));
    }

    [Fact]
    public void PLAN0018_CA75_ListaBlanca_SumaLaLecturaDeTransferenciasPorToUserIdAcotadaASusMetodosConSuMotivo()
    {
        // T30c (RF-83 i): la lectura de propuestas por ToUserId == sub y las membresías de la organización de la propuesta,
        // cada una acotada a su método; fuera de ellos, el archivo no se salta los filtros.
        const string inbox = "src/Zentory.Application/Organization/Queries/ListMyOwnershipTransfersQuery.cs";
        var entries = Allowlist.Where(e => e.Path == inbox).ToList();

        Assert.Equal(["ActiveMembershipsAsync", "PendingForTarget"], entries.Select(e => e.Member).Order(StringComparer.Ordinal));
        Assert.Contains("ToUserId == sub", Assert.Single(entries, e => e.Member == "PendingForTarget").Reason, StringComparison.Ordinal);
        Assert.Contains("DeletedAt == null", Assert.Single(entries, e => e.Member == "ActiveMembershipsAsync").Reason, StringComparison.Ordinal);
        var usages = UsageLines(Read(inbox));
        Assert.Equal(2, usages.Count);
        Assert.All(usages, line => Assert.True(IsAllowed(inbox, line)));

        // Las acciones del destino leen solo a través de esa clase: ni los comandos ni el controlador se saltan filtros.
        foreach (var file in new[]
                 {
                     "src/Zentory.Application/Organization/Commands/AcceptOwnershipTransferCommand.cs",
                     "src/Zentory.Application/Organization/Commands/DeclineOwnershipTransferCommand.cs",
                     "src/Zentory.API/Controllers/V1/OwnershipTransfersController.cs",
                 })
            Assert.Empty(UsageLines(Read(file)));
    }

    [Fact]
    public void PLAN0018_CA75_ListaBlanca_ElUsoDelAvisoPrevioQuedaAcotadoAUsageAsync()
    {
        // T30c: el cálculo del uso no depende de la organización activa (mismo previewHash para owner y destino).
        const string preview = "src/Zentory.Application/Organization/Services/OwnershipTransferPreview.cs";
        var entry = Assert.Single(Allowlist, e => e.Path == preview);

        Assert.Equal("UsageAsync", entry.Member);
        var usages = UsageLines(Read(preview));
        Assert.NotEmpty(usages);
        Assert.All(usages, line => Assert.True(IsAllowed(preview, line)));
    }

    [Fact]
    public void PLAN0018_CA75_Detector_EncuentraLlamadasEIgnoraComentarios()
    {
        const string sample = """
            var a = _db.Clients.IgnoreQueryFilters().ToList();
                .IgnoreQueryFilters ()
            // IgnoreQueryFilters() quita también el borrado lógico.
            /// <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters"/>
            var b = "IgnoreQueryFilters";
            """;

        Assert.Equal([1, 2], UsageLines(sample));
    }

    [Fact]
    public void PLAN0018_CA75_EntradaConMiembro_RechazaElUsoEnOtroMetodo()
    {
        const string sample = """
            public sealed class Repo
            {
                public Task<X> GetByIdAsync(Guid id)
                    => _db.Xs.IgnoreQueryFilters().FirstAsync(x => x.Id == id);

                public Task<List<X>> ListByEmailAsync(string email)
                    => _db.Xs.IgnoreQueryFilters().Where(x => x.Email == email).ToListAsync();
            }
            """;

        var range = MemberRange(sample, "ListByEmailAsync");

        Assert.NotNull(range);
        Assert.Equal([4, 7], UsageLines(sample));
        Assert.False(range.Value.Start <= 4 && 4 <= range.Value.End);
        Assert.True(range.Value.Start <= 7 && 7 <= range.Value.End);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Llamada a IgnoreQueryFilters con paréntesis, fuera de comentarios y de literales de cadena simples.
    [GeneratedRegex(@"\bIgnoreQueryFilters\s*\(")]
    private static partial Regex Usage();

    // Declaración de un miembro de tipo (cuatro espacios de sangría), que cierra el rango del miembro anterior.
    [GeneratedRegex(@"^ {4}(public|private|internal|protected)\b")]
    private static partial Regex MemberDeclaration();

    private bool IsAllowed(string file, int line)
        => Allowlist.Any(entry =>
            (file == entry.Path || (entry.Path.EndsWith('/') && file.StartsWith(entry.Path, StringComparison.Ordinal)))
            && (entry.Member is null || MemberRange(Read(file), entry.Member) is { } range && range.Start <= line && line <= range.End));

    private static List<int> UsageLines(string content)
        => content.Split('\n')
            .Select((text, index) => (text, number: index + 1))
            .Where(item => Usage().IsMatch(StripCommentsAndStrings(item.text)))
            .Select(item => item.number)
            .ToList();

    private static string StripCommentsAndStrings(string line)
    {
        var code = Regex.Replace(line, "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"");
        var comment = code.IndexOf("//", StringComparison.Ordinal);
        return comment >= 0 ? code[..comment] : code;
    }

    // Líneas (1-based) desde la declaración del miembro hasta antes de la siguiente declaración del tipo.
    private static (int Start, int End)? MemberRange(string content, string member)
    {
        var lines = content.Split('\n');
        var start = Array.FindIndex(lines, line => MemberDeclaration().IsMatch(line) && line.Contains($" {member}(", StringComparison.Ordinal));
        if (start < 0)
            return null;
        var next = Array.FindIndex(lines, start + 1, line => MemberDeclaration().IsMatch(line));
        return (start + 1, next < 0 ? lines.Length : next);
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(RepoRoot.Path, file));

    // Archivos .cs de src/, relativos al repo y sin bin/ ni obj/.
    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"));
}
