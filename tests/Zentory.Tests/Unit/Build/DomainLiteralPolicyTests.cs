using System.Reflection;
using System.Text.RegularExpressions;
using Zentory.Domain.Constants;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 (RNF-7; partes de CA-72 y CA-94, que se cierran en T33a): ningún test fuera de Support/ escribe a mano un
// valor de dominio (LegalType, Plan, OrgRole), y src/ no escribe un rol fuera de OrgRole (RF-45, D-18); se usan las
// constantes. Los valores salen por reflexión de las clases de constantes, así que este archivo no contiene ninguno y un
// valor nuevo queda cubierto sin tocarlo.
//
// El detector busca literales de cadena cuyo contenido es exactamente un valor: el rol entre comillas sí; OwnerId, un
// "ownerId" de JSON, "Propietario" o un texto de error que menciona el rol, no.
public sealed class DomainLiteralPolicyTests
{
    private const string TestsFolder   = "tests/Zentory.Tests";
    private const string SupportFolder = TestsFolder + "/Support/";
    private const string SrcFolder     = "src";
    private const string OrgRoleFile   = SrcFolder + "/Zentory.Domain/Constants/OrgRole.cs";

    // Clases cuyos valores no pueden aparecer como literal en los tests.
    private static readonly Type[] GuardedConstants = [typeof(LegalType), typeof(Plan), typeof(OrgRole)];

    // ── CA-72: tests fuera de Support/ ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA72_TestsFueraDeSupport_SinLiteralesDeLegalTypePlanNiOrgRole()
    {
        var detector = LiteralDetector(GuardedValues());
        var offending = SourceFiles(TestsFolder)
            .Where(file => !file.StartsWith(SupportFolder, StringComparison.Ordinal))
            .SelectMany(file => OffendingLines(detector, File.ReadAllText(Path.Combine(RepoRoot.Path, file)))
                .Select(line => $"{file}:{line}"))
            .ToList();

        Assert.True(offending.Count == 0,
            "Literales de LegalType, Plan u OrgRole en tests (usa las constantes de Zentory.Domain.Constants): " +
            string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA72_ValoresVigilados_SonTodosLosDeLegalTypePlanYOrgRole()
    {
        var values = GuardedValues();

        Assert.Equal(
            new[]
            {
                LegalType.Empresa, LegalType.Freelance, Plan.Free, Plan.Pro, Plan.Studio,
                OrgRole.Owner, OrgRole.Admin, OrgRole.Member,
            }.Order(StringComparer.Ordinal),
            values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PLAN0018_CA72_Detector_EncuentraCadaValorComoLiteralExacto()
    {
        var detector = LiteralDetector(GuardedValues());

        foreach (var value in GuardedValues())
        {
            Assert.NotEmpty(OffendingLines(detector, $"var x = \"{value}\";"));
            Assert.NotEmpty(OffendingLines(detector, $"Assert.Equal(\"{value}\", body.Plan);"));
            Assert.NotEmpty(OffendingLines(detector, $"[InlineData(\"{value}\")]"));
            Assert.NotEmpty(OffendingLines(detector, $"var y = @\"{value}\";"));
        }
    }

    [Fact]
    public void PLAN0018_CA72_Detector_EncuentraCadaRolEnLasFormasDeUnTest()
    {
        // Con el rol entre comillas el test falla: como argumento, en una aserción, dentro de un JSON y en una constante.
        var detector = LiteralDetector(GuardedValues());

        foreach (var role in OrgRoleValues())
        {
            Assert.NotEmpty(OffendingLines(detector, $"var user = tenants.A.Member(\"{role}\");"));
            Assert.NotEmpty(OffendingLines(detector, $"Assert.Equal(\"{role}\", body.Role);"));
            Assert.NotEmpty(OffendingLines(detector, "var json = \"\"\"{\"roleId\":\"" + role + "\"}\"\"\";"));
            Assert.NotEmpty(OffendingLines(detector, $"private const string OwnerRole = \"{role}\";"));
        }
    }

    [Fact]
    public void PLAN0018_CA72_Detector_IgnoraConstantesYCadenasQueSoloContienenElValor()
    {
        var detector = LiteralDetector(GuardedValues());

        foreach (var value in GuardedValues())
        {
            Assert.Empty(OffendingLines(detector, "var x = Plan.Pro; var y = LegalType.Empresa; var z = OrgRole.Admin;"));
            Assert.Empty(OffendingLines(detector, $"var x = \"{value}-x\";"));
            Assert.Empty(OffendingLines(detector, $"var x = \"x {value}\";"));
            Assert.Empty(OffendingLines(detector, $"var x = \"{value.ToUpperInvariant()}\";"));
        }
    }

    [Fact]
    public void PLAN0018_CA72_Escaneo_ExcluyeSupportYIncluyeElRestoDeLaSuite()
    {
        var files = SourceFiles(TestsFolder).ToList();

        Assert.Contains(files, file => file.StartsWith(SupportFolder, StringComparison.Ordinal));
        Assert.Contains($"{TestsFolder}/Unit/Build/DomainLiteralPolicyTests.cs", files);
        Assert.DoesNotContain(files, file => file.Split('/').Any(part => part is "bin" or "obj"));
    }

    // ── CA-94: src/ sin literales de rol fuera de OrgRole ───────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA94_SrcFueraDeOrgRole_SinLiteralesDeRol()
    {
        var detector = LiteralDetector(OrgRoleValues());
        var offending = SourceFiles(SrcFolder)
            .Where(file => file != OrgRoleFile)
            .SelectMany(file => OffendingLines(detector, File.ReadAllText(Path.Combine(RepoRoot.Path, file)))
                .Select(line => $"{file}:{line}"))
            .ToList();

        Assert.True(offending.Count == 0,
            "Literales de rol en src/ fuera de OrgRole (usa OrgRole.Owner, OrgRole.Admin u OrgRole.Member): " +
            string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA94_Escaneo_RecorreSrcYSoloExceptuaOrgRole()
    {
        var files = SourceFiles(SrcFolder).ToList();
        var detector = LiteralDetector(OrgRoleValues());

        // Control: OrgRole sí tiene los literales (una línea por rol), así que el detector y la ruta son los correctos; y
        // el recorrido incluye el dominio, la API y los escenarios demo.
        Assert.Contains(OrgRoleFile, files);
        Assert.Equal(OrgRoleValues().Count,
            OffendingLines(detector, File.ReadAllText(Path.Combine(RepoRoot.Path, OrgRoleFile))).Count);
        Assert.Contains($"{SrcFolder}/Zentory.Domain/Entities/OrganizationMember.cs", files);
        Assert.Contains($"{SrcFolder}/Zentory.API/Controllers/V1/OrganizationRolesController.cs", files);
        Assert.Contains($"{SrcFolder}/Zentory.Infrastructure/Persistence/Seeding/Demo/AgencyScenario.cs", files);
        Assert.DoesNotContain(files, file => file.Split('/').Any(part => part is "bin" or "obj"));
    }

    [Fact]
    public void PLAN0018_CA94_Detector_IgnoraNombresYTextosQueSoloMencionanElRol()
    {
        var detector = LiteralDetector(OrgRoleValues());

        Assert.Empty(OffendingLines(detector, "if (organization.OwnerId == sub) return OrgRole.Owner;"));
        Assert.Empty(OffendingLines(detector, "var json = \"\"\"{\"ownerId\":1,\"memberCount\":2}\"\"\";"));
        Assert.Empty(OffendingLines(detector, "new OrgRoleDto(OrgRole.Admin, \"Administrador\", true, null, null);"));
        Assert.Empty(OffendingLines(detector, "throw new ForbiddenException(\"Only the owner can do this.\");"));
        Assert.Empty(OffendingLines(detector, "var key = \"members.manage\"; var claim = \"active_org_role\";"));
        foreach (var role in OrgRoleValues())
        {
            Assert.Empty(OffendingLines(detector, $"var x = \"{role}s\";"));
            Assert.Empty(OffendingLines(detector, $"var x = \"{char.ToUpperInvariant(role[0])}{role[1..]}\";"));
        }
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static List<string> GuardedValues() => GuardedConstants.SelectMany(ConstantValues).ToList();

    private static List<string> OrgRoleValues() => ConstantValues(typeof(OrgRole)).ToList();

    private static IEnumerable<string> ConstantValues(Type type)
        => type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    // Literal de cadena cuyo contenido es exactamente uno de los valores (sensible a mayúsculas, como el dominio).
    private static Regex LiteralDetector(IEnumerable<string> values)
        => new("\"(?:" + string.Join("|", values.Select(Regex.Escape)) + ")\"", RegexOptions.CultureInvariant);

    private static List<int> OffendingLines(Regex detector, string content)
        => content.Split('\n')
            .Select((line, index) => (line, number: index + 1))
            .Where(item => detector.IsMatch(item.line))
            .Select(item => item.number)
            .ToList();

    // Archivos .cs de la carpeta (relativa al repo, con /), relativos al repo y sin bin/ ni obj/.
    private static IEnumerable<string> SourceFiles(string folder)
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, folder), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"));
}
