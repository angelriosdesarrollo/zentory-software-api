using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Zentory.API.Authorization;
using Zentory.API.Controllers.V1;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 T20 sobre los metadatos de AuthController (RF-29, RF-60; D-13, D-17).
//
// CA-T20-2 (parte de CA-84): [AllowAnonymous] no está en la clase, cada acción declara en el método [AllowAnonymous] o
// [Authorize], y solo resend-verification y reauth-nonce exigen autenticación. T29b suma invitations/accept: anónima, con
// su política de RF-29 f, y lee el usuario cuando el Bearer es válido (sus casos con JWT, en AcceptInvitationTests).
// Parte estática de CA-T20-1 (CA-34): cada endpoint de RF-29 lleva su [AuthRateLimit] con su política, refresh, logout y
// reauth-nonce no tienen límite propio, y la API no registra ForwardedHeaders (X-Forwarded-For no cambia la IP de la
// clave del limitador).
public sealed class AuthControllerAuthorizationTests
{
    private const string SourceFolder = "src";

    // Rutas (plantilla de [Http*]) que exigen el JWT de usuario (RF-60, RF-84).
    private static readonly string[] AuthenticatedRoutes = ["resend-verification", "reauth-nonce"];

    // Política de [AuthRateLimit] de cada ruta (RF-29 a-f). refresh, logout y reauth-nonce no tienen límite propio.
    private static readonly Dictionary<string, string> RateLimitedRoutes = new(StringComparer.Ordinal)
    {
        ["login"]               = AuthRateLimitPolicies.Login,
        ["forgot-password"]     = AuthRateLimitPolicies.ForgotPassword,
        ["resend-verification"] = AuthRateLimitPolicies.ResendVerification,
        ["register"]            = AuthRateLimitPolicies.Register,
        ["google"]              = AuthRateLimitPolicies.Google,
        ["reset-password"]      = AuthRateLimitPolicies.ResetPassword,
        ["verify-email"]        = AuthRateLimitPolicies.VerifyEmail,
        ["invitations/accept"]  = AuthRateLimitPolicies.AcceptInvitation,
    };

    // ── CA-T20-2 (CA-84) ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA84_AuthController_AllowAnonymousNoEstaEnLaClase()
    {
        Assert.Null(typeof(AuthController).GetCustomAttribute<AllowAnonymousAttribute>(inherit: true));
        Assert.Null(typeof(AuthController).GetCustomAttribute<AuthorizeAttribute>(inherit: true));
    }

    [Fact]
    public void PLAN0018_CA84_AuthController_CadaAccionDeclaraEnElMetodoAllowAnonymousOAuthorizeYNoAmbos()
    {
        var offenders = Actions()
            .Where(action => action.Method.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
                          == action.Method.IsDefined(typeof(AuthorizeAttribute), inherit: true))
            .Select(action => action.Route)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Acciones de AuthController sin exactamente uno de [AllowAnonymous] o [Authorize]: " + string.Join(", ", offenders));
    }

    [Fact]
    public void PLAN0018_CA84_AuthController_SoloResendVerificationYReauthNonceExigenAutenticacion()
    {
        var actions = Actions();
        Assert.Contains(actions, action => action.Route == "login");

        var authenticated = actions.Where(action => RequiresAuthentication(action.Method)).Select(action => action.Route);

        Assert.Equal(AuthenticatedRoutes.Order(StringComparer.Ordinal), authenticated.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PLAN0018_CA84_AuthController_InvitationsAcceptEsAnonimaYLeeElUsuarioDelBearerSinExigirlo()
    {
        var accept = Assert.Single(Actions(), action => action.Route == "invitations/accept");

        // Anónima ([AllowAnonymous] en el método, sin [Authorize]): un Bearer vencido o ausente no da 401, cuenta como sin
        // JWT. El usuario del Bearer válido lo lee el comando desde ITenantContext (ya poblado por la autenticación por
        // defecto), no un parámetro del cuerpo.
        Assert.True(accept.Method.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.False(RequiresAuthentication(accept.Method));
        Assert.Equal(HttpMethods.Post, Assert.Single(accept.Method.GetCustomAttribute<HttpMethodAttribute>()!.HttpMethods));
        Assert.DoesNotContain(accept.Method.GetParameters(),
            parameter => parameter.ParameterType == typeof(System.Security.Claims.ClaimsPrincipal));
    }

    // ── CA-T20-1 (CA-34), parte estática ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA34_AuthController_CadaEndpointDeRf29DeclaraSuPoliticaDeAuthRateLimitYLosDemasNinguna()
    {
        var declared = Actions().ToDictionary(
            action => action.Route,
            action => action.Method.GetCustomAttributes<AuthRateLimitAttribute>().Select(a => a.Policy).ToList(),
            StringComparer.Ordinal);

        foreach (var (route, policies) in declared)
        {
            if (RateLimitedRoutes.TryGetValue(route, out var expected))
                Assert.True(policies.SequenceEqual([expected]), $"{route} debe declarar solo [AuthRateLimit({expected})].");
            else
                Assert.True(policies.Count == 0, $"{route} no tiene límite propio en RF-29.");
        }
        Assert.Subset(declared.Keys.ToHashSet(StringComparer.Ordinal), RateLimitedRoutes.Keys.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void PLAN0018_CA34_PoliticasDeAuth_ExistenEnElLimitadorConSusValoresPorDefecto()
    {
        var limiter = new AuthAttemptLimiter(TimeProvider.System, AuthRateLimitPolicies.Defaults);
        var minutes15 = TimeSpan.FromMinutes(15);
        var hour      = TimeSpan.FromHours(1);

        AssertRules(limiter, AuthRateLimitPolicies.Login,
            (AuthRateLimitPartition.IpAndEmail, 5, minutes15), (AuthRateLimitPartition.Ip, 20, minutes15));
        AssertRules(limiter, AuthRateLimitPolicies.ForgotPassword,
            (AuthRateLimitPartition.Email, 3, hour), (AuthRateLimitPartition.Ip, 10, hour));
        AssertRules(limiter, AuthRateLimitPolicies.ResendVerification,
            (AuthRateLimitPartition.User, 3, hour), (AuthRateLimitPartition.Ip, 10, hour));
        AssertRules(limiter, AuthRateLimitPolicies.Register, (AuthRateLimitPartition.Ip, 3, hour));
        AssertRules(limiter, AuthRateLimitPolicies.Google, (AuthRateLimitPartition.Ip, 10, minutes15));
        AssertRules(limiter, AuthRateLimitPolicies.ResetPassword, (AuthRateLimitPartition.Ip, 20, minutes15));
        AssertRules(limiter, AuthRateLimitPolicies.VerifyEmail, (AuthRateLimitPartition.Ip, 20, minutes15));
        AssertRules(limiter, AuthRateLimitPolicies.AcceptInvitation, (AuthRateLimitPartition.Ip, 20, minutes15));

        Assert.Equal(RateLimitedRoutes.Values.Order(StringComparer.Ordinal),
            AuthRateLimitPolicies.AuthEndpointPolicies.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PLAN0018_CA34_HostDePruebasCompartido_SubeLasPoliticasDeAuthYLaFactoryDeLimitesLasDejaPorDefecto()
    {
        // ZentoryApiFactory sube todas (la suite repite logins en el mismo host); AuthRateLimitApiFactory deja las de
        // RF-29 en sus valores por defecto y sube solo las de reautenticación y organización.
        var raised = ZentoryApiFactory.RaisedAuthRateLimitSettings().Select(setting => setting.Key).ToHashSet();
        var raisedByLimits = AuthRateLimitApiFactory.RaisedSettingsExcept(AuthRateLimitPolicies.AuthEndpointPolicies)
            .Select(setting => setting.Key)
            .ToHashSet();

        foreach (var policy in AuthRateLimitPolicies.Defaults)
            foreach (var rule in policy.Rules)
            {
                var key = $"{rule.ConfigSection}:PermitLimit";
                Assert.Contains(key, raised);
                Assert.Equal(!AuthRateLimitPolicies.AuthEndpointPolicies.Contains(policy.Name), raisedByLimits.Contains(key));
            }
    }

    [Fact]
    public void PLAN0018_CA34_Api_NoRegistraForwardedHeaders_LaIpDeLaClaveEsLaDeLaConexion()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, SourceFolder), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("UseForwardedHeaders", StringComparison.Ordinal)
                    || text.Contains("ForwardedHeadersOptions", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "ForwardedHeaders cambiaría la IP de la clave del rate limit (G-5, PLAN-0002): " + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData(" 203.0.113.7 ", "203.0.113.7")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void PLAN0018_CA34_ClaveDeIp_IPv4MapeadaCuentaComoSuIPv4YSinIpQuedaNula(string? ip, string? expected)
        => Assert.Equal(expected, AuthAttemptLimiter.NormalizeIp(ip));

    [Fact]
    public void PLAN0018_CA34_ClaveDeEmail_UsaLaNormalizacionDeRf27()
        => Assert.Equal(Zentory.Domain.Common.EmailAddress.Normalize(" ANA@X.test "),
            AuthAttemptLimiter.NormalizeEmail(" ANA@X.test "));

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record AuthAction(string Route, MethodInfo Method);

    // Métodos de acción de AuthController con su plantilla de [Http*] (todas sin parámetros de ruta).
    private static List<AuthAction> Actions()
        => typeof(AuthController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Http: method.GetCustomAttribute<HttpMethodAttribute>()))
            .Where(pair => pair.Http is not null)
            .Select(pair => new AuthAction(pair.Http!.Template ?? string.Empty, pair.Method))
            .ToList();

    // Igual que decide el pipeline: exige autenticación si hay [Authorize] (en el método o la clase) y ningún
    // [AllowAnonymous] (que en la clase anularía el [Authorize] de la acción).
    private static bool RequiresAuthentication(MethodInfo method)
    {
        var type = method.DeclaringType!;
        var anonymous = method.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
                     || type.IsDefined(typeof(AllowAnonymousAttribute), inherit: true);
        var authorize = method.IsDefined(typeof(AuthorizeAttribute), inherit: true)
                     || type.IsDefined(typeof(AuthorizeAttribute), inherit: true);
        return authorize && !anonymous;
    }

    private static void AssertRules(
        AuthAttemptLimiter limiter, string policy, params (AuthRateLimitPartition Partition, int Limit, TimeSpan Window)[] expected)
        => Assert.Equal(expected,
            limiter.GetPolicy(policy).Rules.Select(rule => (rule.Partition, rule.PermitLimit, rule.Window)).ToArray());

    private static bool IsBuildOutput(string file)
        => file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj");
}
