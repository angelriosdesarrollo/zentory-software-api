using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Authorization;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.RateLimiting;

// PLAN-0018 CA-T10-1 (parte de CA-111; D-13, RF-29, RF-81 f y g, SR-0018.29): AuthAttemptLimiter con reloj simulado y
// el filtro [AuthRateLimit] sobre un ActionExecutingContext. Las pruebas de extremo a extremo de los endpoints llegan
// con T11 y T30c, que son las que ponen el atributo en las acciones.
public sealed class AuthAttemptLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);

    private AuthAttemptLimiter DefaultLimiter() => new(_time, AuthRateLimitPolicies.Defaults);

    private static AuthAttemptSubject User(Guid sub) => new(sub.ToString(), "10.0.0.1", null);

    // ── reauth:{sub} ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA111_Reauth_SextoIntentoEn15Min_SeRechazaConRetryAfter()
    {
        var limiter = DefaultLimiter();
        var owner   = User(Guid.NewGuid());

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner).IsAllowed, $"intento {attempt}");
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var sixth = limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner);

        Assert.False(sixth.IsAllowed);
        // El primero fue en Start; ahora es Start + 5 min: el cupo se libera a los 15 min del primero.
        Assert.Equal(TimeSpan.FromMinutes(10), sixth.RetryAfter);
    }

    [Fact]
    public void PLAN0018_CA111_Reauth_Pasados15Min_VuelveAPermitir()
    {
        var limiter = DefaultLimiter();
        var owner   = User(Guid.NewGuid());
        for (var attempt = 0; attempt < 5; attempt++)
            limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner);
        Assert.False(limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner).IsAllowed);

        _time.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        Assert.False(limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner).IsAllowed);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner).IsAllowed);
    }

    [Fact]
    public void PLAN0018_CA111_Reauth_VentanaDeslizante_LiberaSoloLosIntentosVencidos()
    {
        var limiter = DefaultLimiter();
        var owner   = User(Guid.NewGuid());
        for (var attempt = 0; attempt < 3; attempt++)
            limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner);
        _time.Advance(TimeSpan.FromMinutes(10));
        for (var attempt = 0; attempt < 2; attempt++)
            limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner);

        // A los 15 min vencen los tres primeros; los dos de los 10 min siguen dentro.
        _time.Advance(TimeSpan.FromMinutes(5));
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.True(limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner).IsAllowed);

        var rejected = limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner);
        Assert.False(rejected.IsAllowed);
        Assert.Equal(TimeSpan.FromMinutes(10), rejected.RetryAfter);
    }

    [Fact]
    public void PLAN0018_CA111_Reauth_ContadorPorSub_OtroUsuarioNoQuedaLimitado()
    {
        var limiter = DefaultLimiter();
        var owner   = User(Guid.NewGuid());
        var other   = User(Guid.NewGuid());
        for (var attempt = 0; attempt < 5; attempt++)
            limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner);

        Assert.False(limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner).IsAllowed);
        // Misma IP, otro sub: la política es por usuario.
        Assert.True(limiter.TryAcquire(AuthRateLimitPolicies.Reauth, other).IsAllowed);
    }

    [Fact]
    public void PLAN0018_CA111_Reauth_ConteoDeFallos_SeConsultaPorVentanaYPorSub()
    {
        var limiter = DefaultLimiter();
        var owner   = User(Guid.NewGuid());
        var other   = User(Guid.NewGuid());

        Assert.Equal(1, limiter.RecordFailure(AuthRateLimitPolicies.Reauth, owner));
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(2, limiter.RecordFailure(AuthRateLimitPolicies.Reauth, owner));
        Assert.Equal(3, limiter.RecordFailure(AuthRateLimitPolicies.Reauth, owner));
        Assert.Equal(3, limiter.CountFailures(AuthRateLimitPolicies.Reauth, owner));
        Assert.Equal(0, limiter.CountFailures(AuthRateLimitPolicies.Reauth, other));

        // Vence el primero (Start + 15 min); los dos de Start + 5 min siguen.
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, limiter.CountFailures(AuthRateLimitPolicies.Reauth, owner));

        // Los fallos no consumen el cupo de intentos: se cuentan aparte.
        Assert.True(limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner).IsAllowed);
    }

    // ── organization-delete, organization-restore, organization-transfer ────────────────────────────────────────

    [Theory]
    [InlineData(AuthRateLimitPolicies.OrganizationDelete)]
    [InlineData(AuthRateLimitPolicies.OrganizationRestore)]
    [InlineData(AuthRateLimitPolicies.OrganizationTransfer)]
    public void PLAN0018_CA111_PoliticasDeOrganizacion_CincoPorHoraPorSub_ElSextoSeRechaza(string policy)
    {
        var limiter = DefaultLimiter();
        var owner   = User(Guid.NewGuid());
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(limiter.TryAcquire(policy, owner).IsAllowed, $"intento {attempt}");
            _time.Advance(TimeSpan.FromMinutes(5));
        }

        var sixth = limiter.TryAcquire(policy, owner);
        Assert.False(sixth.IsAllowed);
        Assert.Equal(TimeSpan.FromMinutes(35), sixth.RetryAfter);

        Assert.True(limiter.TryAcquire(policy, User(Guid.NewGuid())).IsAllowed);

        _time.Advance(TimeSpan.FromMinutes(35));
        Assert.True(limiter.TryAcquire(policy, owner).IsAllowed);
    }

    [Fact]
    public void PLAN0018_CA111_Politicas_SonIndependientes_ReauthLlenoNoBloqueaOrganizationDelete()
    {
        var limiter = DefaultLimiter();
        var owner   = User(Guid.NewGuid());
        for (var attempt = 0; attempt < 5; attempt++)
            limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner);

        Assert.False(limiter.TryAcquire(AuthRateLimitPolicies.Reauth, owner).IsAllowed);
        Assert.True(limiter.TryAcquire(AuthRateLimitPolicies.OrganizationDelete, owner).IsAllowed);
    }

    [Fact]
    public void PLAN0018_CA111_ValoresPorDefecto_SonLosDeLaSpec()
    {
        var reauth = Assert.Single(DefaultLimiter().GetPolicy(AuthRateLimitPolicies.Reauth).Rules);
        Assert.Equal(AuthRateLimitPartition.User, reauth.Partition);
        Assert.Equal(5, reauth.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(15), reauth.Window);

        foreach (var name in new[]
                 {
                     AuthRateLimitPolicies.OrganizationDelete, AuthRateLimitPolicies.OrganizationRestore,
                     AuthRateLimitPolicies.OrganizationTransfer,
                 })
        {
            var rule = Assert.Single(DefaultLimiter().GetPolicy(name).Rules);
            Assert.Equal(AuthRateLimitPartition.User, rule.Partition);
            Assert.Equal(5, rule.PermitLimit);
            Assert.Equal(TimeSpan.FromHours(1), rule.Window);
        }
    }

    // ── Clave normalizada y techo por IP (mecanismo que usan las políticas de auth de la parte 2) ───────────────

    [Fact]
    public void PLAN0018_CA111_ClaveDeEmail_SeNormaliza_MayusculasYEspaciosCuentanJuntos()
    {
        var policy  = new AuthRateLimitPolicy("probe-email",
            [new AuthRateLimitRule("RateLimiting:Auth:Probe", AuthRateLimitPartition.IpAndEmail, 2, TimeSpan.FromMinutes(15))]);
        var limiter = new AuthAttemptLimiter(_time, [policy]);

        Assert.True(limiter.TryAcquire(policy.Name, new AuthAttemptSubject(null, "10.0.0.1", "Ana@Zentory.test")).IsAllowed);
        Assert.True(limiter.TryAcquire(policy.Name, new AuthAttemptSubject(null, "10.0.0.1", "  ana@zentory.TEST ")).IsAllowed);
        Assert.False(limiter.TryAcquire(policy.Name, new AuthAttemptSubject(null, "10.0.0.1", "ana@zentory.test")).IsAllowed);
    }

    [Fact]
    public void PLAN0018_CA111_TechoPorIp_LimitaAunqueCambieElEmail_SinContarElIntentoRechazado()
    {
        var policy  = new AuthRateLimitPolicy("probe-ceiling",
        [
            new AuthRateLimitRule("RateLimiting:Auth:Probe:Email", AuthRateLimitPartition.IpAndEmail, 2, TimeSpan.FromMinutes(15)),
            new AuthRateLimitRule("RateLimiting:Auth:Probe:Ip", AuthRateLimitPartition.Ip, 3, TimeSpan.FromMinutes(15)),
        ]);
        var limiter = new AuthAttemptLimiter(_time, [policy]);

        for (var index = 0; index < 3; index++)
            Assert.True(limiter.TryAcquire(policy.Name, new AuthAttemptSubject(null, "10.0.0.9", $"u{index}@zentory.test")).IsAllowed);

        Assert.False(limiter.TryAcquire(policy.Name, new AuthAttemptSubject(null, "10.0.0.9", "otro@zentory.test")).IsAllowed);
        // Otra IP con un email ya usado: el techo es por IP y el rechazo anterior no sumó al email.
        Assert.True(limiter.TryAcquire(policy.Name, new AuthAttemptSubject(null, "10.0.0.10", "otro@zentory.test")).IsAllowed);
    }

    // ── Configuración ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA111_Configuracion_SobrescribeUmbralYVentana()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AuthRateLimitPolicies.AuthSection}:Reauth:PermitLimit"]          = "2",
                [$"{AuthRateLimitPolicies.OrganizationSection}:Restore:Window"]      = "00:30:00",
            })
            .Build();

        var limiter = new AuthAttemptLimiter(_time, AuthRateLimitPolicies.FromConfiguration(configuration));

        var reauth  = Assert.Single(limiter.GetPolicy(AuthRateLimitPolicies.Reauth).Rules);
        var restore = Assert.Single(limiter.GetPolicy(AuthRateLimitPolicies.OrganizationRestore).Rules);
        Assert.Equal(2, reauth.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(15), reauth.Window);
        Assert.Equal(5, restore.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(30), restore.Window);
    }

    [Theory]
    [InlineData("PermitLimit", "0")]
    [InlineData("PermitLimit", "cinco")]
    [InlineData("Window", "-00:01:00")]
    public void PLAN0018_CA111_Configuracion_ValorInvalido_FallaConLaClave(string setting, string value)
    {
        var key = $"{AuthRateLimitPolicies.OrganizationSection}:Delete:{setting}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => AuthRateLimitPolicies.FromConfiguration(configuration));
        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PLAN0018_CA111_HostDePruebas_SubeLosUmbralesDeTodasLasPoliticas()
    {
        await using var factory = new ZentoryApiFactory();

        var limiter = factory.Services.GetRequiredService<AuthAttemptLimiter>();

        foreach (var policy in AuthRateLimitPolicies.Defaults)
            Assert.All(limiter.GetPolicy(policy.Name).Rules,
                rule => Assert.Equal(ZentoryApiFactory.RaisedAuthPermitLimit, rule.PermitLimit));
    }

    // ── Filtro [AuthRateLimit] ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA111_Filtro_SextoIntento_Responde429RateLimitedConRetryAfterSinEjecutarLaAccion()
    {
        var services  = FilterServices(DefaultLimiter());
        var filter    = new AuthRateLimitAttribute(AuthRateLimitPolicies.Reauth);
        var sub       = Guid.NewGuid();
        var executed  = 0;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var allowed = ExecutingContext(services, sub);
            await filter.OnActionExecutionAsync(allowed, Next(allowed, () => executed++));
            Assert.Null(allowed.Result);
        }
        Assert.Equal(5, executed);

        _time.Advance(TimeSpan.FromSeconds(90));
        var context = ExecutingContext(services, sub);
        await filter.OnActionExecutionAsync(context, Next(context, () => executed++));

        Assert.Equal(5, executed);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, result.StatusCode);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.Equal(AuthRateLimitAttribute.RateLimitedCode, body.RootElement.GetProperty("code").GetString());
        // 15 min − 90 s = 810 s.
        Assert.Equal("810", context.HttpContext.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task PLAN0018_CA111_Filtro_OtroSub_EjecutaLaAccion()
    {
        var services = FilterServices(DefaultLimiter());
        var filter   = new AuthRateLimitAttribute(AuthRateLimitPolicies.OrganizationDelete);
        var owner    = Guid.NewGuid();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var context = ExecutingContext(services, owner);
            await filter.OnActionExecutionAsync(context, Next(context, () => { }));
        }

        var executed = false;
        var other    = ExecutingContext(services, Guid.NewGuid());
        await filter.OnActionExecutionAsync(other, Next(other, () => executed = true));

        Assert.True(executed);
        Assert.Null(other.Result);
    }

    [Fact]
    public async Task PLAN0018_CA111_Filtro_TomaElEmailDelArgumentoEnlazado_Normalizado()
    {
        var policy   = new AuthRateLimitPolicy("probe-filter-email",
            [new AuthRateLimitRule("RateLimiting:Auth:Probe", AuthRateLimitPartition.Email, 1, TimeSpan.FromMinutes(15))]);
        var services = FilterServices(new AuthAttemptLimiter(_time, [policy]));
        var filter   = new AuthRateLimitAttribute(policy.Name);

        var first = ExecutingContext(services, sub: null, new EmailBody("Ana@Zentory.test"));
        await filter.OnActionExecutionAsync(first, Next(first, () => { }));
        var sameEmail = ExecutingContext(services, sub: null, new EmailBody(" ana@zentory.test "));
        await filter.OnActionExecutionAsync(sameEmail, Next(sameEmail, () => { }));
        var otherEmail = ExecutingContext(services, sub: null, new EmailBody("otra@zentory.test"));
        await filter.OnActionExecutionAsync(otherEmail, Next(otherEmail, () => { }));

        Assert.Null(first.Result);
        Assert.IsType<ObjectResult>(sameEmail.Result);
        Assert.Null(otherEmail.Result);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record EmailBody(string Email);

    private static IServiceProvider FilterServices(AuthAttemptLimiter limiter)
        => new ServiceCollection().AddSingleton(limiter).BuildServiceProvider();

    private static ActionExecutingContext ExecutingContext(IServiceProvider services, Guid? sub, object? body = null)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        if (sub is not null)
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, sub.Value.ToString())], authenticationType: "test"));

        var action    = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var arguments = new Dictionary<string, object?>();
        if (body is not null) arguments["request"] = body;
        return new ActionExecutingContext(action, new List<IFilterMetadata>(), arguments, controller: new object());
    }

    private static ActionExecutionDelegate Next(ActionExecutingContext context, Action onExecute)
        => () =>
        {
            onExecute();
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), context.Controller));
        };
}
