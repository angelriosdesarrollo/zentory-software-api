using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Authorization;
using Zentory.API.Middleware;
using Zentory.Domain.Constants;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 T25 (RF-40, RF-73; D-26; parte de CA-96): el claim plan del access token es informativo y nada en src/ lo lee
// para autorizar; el plan sale de IMembershipContext.EffectivePlan, resuelto de la suscripción del owner en cada petición.
//
// - Recorrido de src/: toda aparición del literal "plan" está en la lista blanca con su motivo (la emisión del claim en
//   JwtService y dos usos que no son el claim), y ninguna llamada de lectura de claims (FindFirst, FindFirstValue,
//   FindAll, HasClaim, RequireClaim, Claim.Type ==) recibe "plan".
// - MinimumPlanHandler y TenantContext.Plan siguen a EffectivePlan aunque el claim diga otra cosa, y fallan cerrados sin
//   membresía resuelta.
public sealed partial class PlanClaimPolicyTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string PlanClaim = "plan";

    private sealed record AllowedLiteral(string Path, string Fragment, string Reason);

    private static readonly AllowedLiteral[] Allowlist =
    [
        new("src/Zentory.Infrastructure/Services/JwtService.cs", "new Claim(\"plan\"",
            "Emisión del claim informativo para el frontend (RF-40); no es una lectura."),
        new("src/Zentory.API/Controllers/V1/OrganizationController.cs", "[HttpGet(\"plan\")]",
            "Segmento de la ruta GET /organization/plan, no un claim."),
    ];

    [Fact]
    public void PLAN0018_CA96_Src_NingunaLecturaDelClaimPlan()
    {
        var offending = SourceFiles()
            .SelectMany(file => ClaimReadLines(Read(file)).Select(line => $"{file}:{line}"))
            .ToList();

        Assert.True(offending.Count == 0,
            "Lecturas del claim plan en src/ (el plan para autorizar es IMembershipContext.EffectivePlan, D-26): " +
            string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA96_Src_ElLiteralPlanSoloApareceEnLaListaBlanca()
    {
        var offending = SourceFiles()
            .SelectMany(file => LiteralLines(Read(file))
                .Where(item => !Allowlist.Any(entry => entry.Path == file && item.Text.Contains(entry.Fragment, StringComparison.Ordinal)))
                .Select(item => $"{file}:{item.Number}"))
            .ToList();

        Assert.True(offending.Count == 0,
            "Literal \"plan\" fuera de la lista blanca (si no es una lectura del claim, agrega la entrada con su motivo): " +
            string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA96_ListaBlanca_CadaEntradaExisteYNombraSuMotivo()
    {
        foreach (var entry in Allowlist)
        {
            var full = Path.Combine(RepoRoot.Path, entry.Path);
            Assert.True(File.Exists(full), $"Entrada sin archivo: {entry.Path}");
            Assert.Contains(entry.Fragment, File.ReadAllText(full), StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(entry.Reason), $"Entrada sin motivo: {entry.Path}");
        }
    }

    [Fact]
    public void PLAN0018_CA96_Detector_EncuentraLecturasDelClaimPlanEIgnoraComentarios()
    {
        const string sample = """
            var a = context.User.FindFirst("plan")?.Value;
            var b = User?.FindFirstValue( "plan" ) ?? Plan.Free;
            policy.RequireClaim("plan", Plan.Pro);
            if (principal.HasClaim("plan", Plan.Studio)) { }
            var c = user.Claims.First(x => x.Type == "plan");
            // context.User.FindFirst("plan") quedó atrás.
            var d = context.User.FindFirst("legal_type")?.Value;
            """;

        Assert.Equal([1, 2, 3, 4, 5], ClaimReadLines(sample));
    }

    [Fact]
    public async Task PLAN0018_CA96_MinimumPlanHandler_AutorizaConEffectivePlanYNoConElClaim()
    {
        var userId = Guid.NewGuid();

        // Claim studio con plan efectivo free: no alcanza pro.
        Assert.False(await EvaluateAsync(userId, claimPlan: Plan.Studio, effectivePlan: Plan.Free, Plan.Pro));
        // Claim free con plan efectivo studio: alcanza studio.
        Assert.True(await EvaluateAsync(userId, claimPlan: Plan.Free, effectivePlan: Plan.Studio, Plan.Studio));
        // Sin claim plan, con plan efectivo pro: alcanza pro y no studio.
        Assert.True(await EvaluateAsync(userId, claimPlan: null, effectivePlan: Plan.Pro, Plan.Pro));
        Assert.False(await EvaluateAsync(userId, claimPlan: null, effectivePlan: Plan.Pro, Plan.Studio));
    }

    [Fact]
    public async Task PLAN0018_CA96_MinimumPlanHandler_SinMembresiaResuelta_NoAutorizaAunqueElClaimDigaStudio()
    {
        var userId = Guid.NewGuid();
        var handler = new MinimumPlanHandler(new MembershipContext());
        var context = new AuthorizationHandlerContext(
            [new MinimumPlanRequirement(Plan.Free)], Principal(userId, Plan.Studio), resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task PLAN0018_CA96_MinimumPlanHandler_PrincipalDistintoDelDeLaMembresia_NoAutoriza()
    {
        var membership = new MembershipContext();
        membership.Resolve(Guid.NewGuid(), Guid.NewGuid(), OrgRole.Owner, ownerUserId: null, effectivePlan: Plan.Studio);
        var handler = new MinimumPlanHandler(membership);
        var context = new AuthorizationHandlerContext(
            [new MinimumPlanRequirement(Plan.Free)], Principal(Guid.NewGuid(), Plan.Studio), resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public void PLAN0018_CA96_TenantContextPlan_EsElPlanEfectivo_YFreeSinMembresia()
    {
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext { User = Principal(userId, Plan.Studio) };
        var accessor = new HttpContextAccessor { HttpContext = http };

        var unresolved = new TenantContext(accessor, new MembershipContext());
        Assert.Equal(Plan.Free, unresolved.Plan);

        var membership = new MembershipContext();
        membership.Resolve(Guid.NewGuid(), userId, OrgRole.Member, Guid.NewGuid(), effectivePlan: Plan.Pro);
        var resolved = new TenantContext(accessor, membership);
        Assert.Equal(Plan.Pro, resolved.Plan);
    }

    [Fact]
    public void PLAN0018_CA96_MinimumPlanHandler_EstaRegistradoComoScopedEnElHost()
    {
        List<ServiceDescriptor>? captured = null;
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            captured = services.ToList()));
        _ = host.Services;

        var handler = Assert.Single(captured!, d =>
            d.ServiceType == typeof(IAuthorizationHandler) && d.ImplementationType == typeof(MinimumPlanHandler));
        Assert.Equal(ServiceLifetime.Scoped, handler.Lifetime);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Lectura de un claim con el tipo "plan": métodos de ClaimsPrincipal/AuthorizationPolicyBuilder o comparación de Type.
    [GeneratedRegex(@"\b(FindFirst|FindFirstValue|FindAll|HasClaim|RequireClaim)\s*\(\s*""plan""|\.Type\s*==\s*""plan""|""plan""\s*==\s*\w+\.Type")]
    private static partial Regex ClaimRead();

    [GeneratedRegex(@"""plan""")]
    private static partial Regex PlanLiteral();

    private static async Task<bool> EvaluateAsync(Guid userId, string? claimPlan, string effectivePlan, string requiredPlan)
    {
        var membership = new MembershipContext();
        membership.Resolve(Guid.NewGuid(), userId, OrgRole.Owner, userId, effectivePlan);
        var handler = new MinimumPlanHandler(membership);
        var context = new AuthorizationHandlerContext(
            [new MinimumPlanRequirement(requiredPlan)], Principal(userId, claimPlan), resource: null);

        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal Principal(Guid userId, string? claimPlan)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (claimPlan is not null)
            claims.Add(new Claim(PlanClaim, claimPlan));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static List<int> ClaimReadLines(string content)
        => CodeLines(content).Where(item => ClaimRead().IsMatch(item.Text)).Select(item => item.Number).ToList();

    private static List<(int Number, string Text)> LiteralLines(string content)
        => CodeLines(content).Where(item => PlanLiteral().IsMatch(item.Text)).ToList();

    // Líneas con su número, sin el comentario de línea. Los literales de cadena se conservan: son lo que se busca.
    private static IEnumerable<(int Number, string Text)> CodeLines(string content)
        => content.Split('\n').Select((text, index) =>
        {
            var trimmed = text.TrimStart();
            var code = trimmed.StartsWith("//", StringComparison.Ordinal) ? string.Empty : text;
            var comment = code.IndexOf(" // ", StringComparison.Ordinal);
            return (index + 1, comment >= 0 ? code[..comment] : code);
        });

    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(full => Path.GetRelativePath(RepoRoot.Path, full).Replace('\\', '/'))
            .Where(file => !file.Contains("/bin/", StringComparison.Ordinal) && !file.Contains("/obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static string Read(string file) => File.ReadAllText(Path.Combine(RepoRoot.Path, file));
}
