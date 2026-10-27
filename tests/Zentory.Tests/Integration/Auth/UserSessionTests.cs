using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Repositories;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T18b (RF-90 a-c, e-g; RNF-10; D-47; M2c) sobre el host real: una sola sesión vigente por usuario. Login,
// Google y registro inician una sesión nueva (sid nuevo en el access token, users.current_session_id y revocación de los
// refresh anteriores); refresh y switch la conservan; el refresh de una sesión reemplazada responde 401 SESSION_REPLACED
// con el cuerpo exacto, sin disparar el reuso; el logout solo revoca el presentado. Las carreras con un login nuevo son
// deterministas con InterleavingRefreshTokenRepository. El access token de una sesión reemplazada se prueba en
// SessionReplacedAccessTests (T18c, comparación del sid en el middleware).
//
// CA-T18B-1 (parte de CA-129), CA-131, CA-132, CA-T18B-2 (parte de CA-130) y CA-T18B-4 (parte de CA-132 y CA-133). Los
// tests que abren dos sesiones del mismo usuario lo dicen en su nombre (R-25).
public sealed class UserSessionTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string Password         = "Owner-Sesion-Unica-2026!";
    private const string RegisterEndpoint = "/api/v1/auth/register";
    private const string ReauthEndpoint   = "/api/v1/auth/reauth-nonce";

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly CapturingLoggerProvider        _logs   = new();
    private readonly WebApplicationFactory<Program> _host;

    public UserSessionTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(_logs))
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IGoogleTokenValidator>();
                services.AddSingleton<IGoogleTokenValidator>(_google);
            }));
    }

    public void Dispose() => _host.Dispose();

    // ── CA-T18B-1 (CA-129): inicio de sesión ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA129_DosLoginsDelMismoUsuario_DosSesiones_SidDistintosCurrentSessionIdDeS2YRefreshAnterioresRevocados()
    {
        var owner = await SeedOwnerAsync();
        var older = await SeedRefreshTokenAsync(_host.Services, owner);
        var s1 = await LoginOwnerAsync(owner);

        var s2 = await LoginOwnerAsync(owner);

        var sid1 = SessionIdOf(s1.RawAccessToken);
        var sid2 = SessionIdOf(s2.RawAccessToken);
        Assert.NotEqual(sid1, sid2);
        Assert.All(new[] { sid1, sid2 }, sid =>
        {
            Assert.NotEqual(Guid.Empty, sid);
            Assert.NotEqual(owner.UserId, sid);
        });
        Assert.Equal(sid2, await CurrentSessionIdAsync(_host.Services, owner.UserId));

        var current = await RefreshTokenRowAsync(_host.Services, s2.RefreshToken);
        Assert.Equal(sid2, current.SessionId);
        Assert.False(current.IsRevoked);
        var previous = (await RefreshTokensOfAsync(_host.Services, owner.UserId)).Where(r => r.Id != current.Id).ToList();
        Assert.Equal(2, previous.Count);
        Assert.All(previous, row => Assert.True(row.IsRevoked));
        Assert.True((await RefreshTokenRowAsync(_host.Services, older)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_CA129_LoginYDespuesGoogle_DosSesiones_GoogleIniciaS2ConSidNuevoYRevocaLosAnteriores()
    {
        var owner = await SeedOwnerAsync(googleId: $"google-{Guid.NewGuid():N}");
        var s1 = await LoginOwnerAsync(owner);

        using var response = await GoogleAsync(_host.CreateClient(), _google, owner.GoogleId!, owner);

        var s2 = await ReadSessionAsync(response);
        var sid2 = SessionIdOf(s2.RawAccessToken);
        Assert.NotEqual(SessionIdOf(s1.RawAccessToken), sid2);
        Assert.NotEqual(Guid.Empty, sid2);
        Assert.NotEqual(owner.UserId, sid2);
        Assert.Equal(sid2, await CurrentSessionIdAsync(_host.Services, owner.UserId));
        Assert.Equal(sid2, (await RefreshTokenRowAsync(_host.Services, s2.RefreshToken)).SessionId);
        Assert.True((await RefreshTokenRowAsync(_host.Services, s1.RefreshToken)).IsRevoked);
        Assert.False((await RefreshTokenRowAsync(_host.Services, s2.RefreshToken)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_CA129_RegisterDeUnaCuentaNueva_CurrentSessionIdEsElSidDeSuToken()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var email = $"registro-sesion-{Guid.NewGuid():N}@zentory.test";

        using var response = await _host.CreateClient().PostAsJsonAsync(RegisterEndpoint, new
        {
            email,
            password      = Password,
            firstName     = "Registro",
            lastName      = "Sesion",
            orgName       = $"Org sesión {Guid.NewGuid():N}",
            legalType     = LegalType.Freelance,
            acceptedTerms = true,
        });

        var session = await ReadSessionAsync(response, HttpStatusCode.Created);
        var userId = Guid.Parse(session.Claim("sub")!);
        var sid = SessionIdOf(session.RawAccessToken);
        Assert.NotEqual(Guid.Empty, sid);
        Assert.Equal(sid, await CurrentSessionIdAsync(_host.Services, userId));
        Assert.Equal(sid, (await RefreshTokenRowAsync(_host.Services, session.RefreshToken)).SessionId);
    }

    // ── CA-131: refresh de una sesión reemplazada ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA131_RefreshDeS1TrasLoginS2_DosSesiones_Responde401SessionReplacedSinEmitirYS2SigueSirviendo()
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);
        var rowsBefore = (await RefreshTokensOfAsync(_host.Services, owner.UserId)).Count;
        using var http = _host.CreateClient();

        using var first  = await RefreshAsync(http, s1.RefreshToken);
        using var second = await RefreshAsync(http, s1.RefreshToken);

        await AssertSessionReplacedAsync(first);
        await AssertSessionReplacedAsync(second);
        Assert.Equal(rowsBefore, (await RefreshTokensOfAsync(_host.Services, owner.UserId)).Count);
        // Sin reuso de RF-30 b: el refresh de S2 no quedó revocado y sigue sirviendo.
        Assert.False((await RefreshTokenRowAsync(_host.Services, s2.RefreshToken)).IsRevoked);
        using var withS2 = await RefreshAsync(http, s2.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, withS2.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA131_RefreshSembradoConSessionIdDeS1SinRevocar_DosSesiones_Responde401SessionReplaced()
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);
        var notRevoked = await SeedRefreshTokenAsync(_host.Services, owner, SessionIdOf(s1.RawAccessToken));
        using var http = _host.CreateClient();

        using var response = await RefreshAsync(http, notRevoked);

        await AssertSessionReplacedAsync(response);
        Assert.False((await RefreshTokenRowAsync(_host.Services, notRevoked)).IsRevoked);
        Assert.False((await RefreshTokenRowAsync(_host.Services, s2.RefreshToken)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_CA131_RefreshExpiradoInexistenteOConSessionIdNulo_Responde401RefreshTokenInvalid()
    {
        var owner = await SeedOwnerAsync();
        var session = await LoginOwnerAsync(owner);
        var sid = SessionIdOf(session.RawAccessToken);
        var expired = $"test-refresh-expirado-{Guid.NewGuid():N}";
        var withoutSession = $"test-refresh-sin-sesion-{Guid.NewGuid():N}";
        await SeedRawRefreshTokenAsync(owner, expired, DateTime.UtcNow.AddSeconds(-1), sid);
        await SeedRawRefreshTokenAsync(owner, withoutSession, DateTime.UtcNow.AddDays(1), sessionId: null);
        using var http = _host.CreateClient();

        using var withExpired        = await RefreshAsync(http, expired);
        using var withUnknown        = await RefreshAsync(http, $"desconocido-{Guid.NewGuid():N}");
        using var withNullSession    = await RefreshAsync(http, withoutSession);

        await AssertRefreshTokenInvalidAsync(withExpired);
        await AssertRefreshTokenInvalidAsync(withUnknown);
        await AssertRefreshTokenInvalidAsync(withNullSession);
        // Ninguno de los tres revoca la sesión vigente.
        Assert.False((await RefreshTokenRowAsync(_host.Services, session.RefreshToken)).IsRevoked);
        Assert.Equal(sid, await CurrentSessionIdAsync(_host.Services, owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA131_RefreshYaRotadoDeS2_DosSesiones_Responde401RefreshTokenInvalidYRevocaTodosLosDeU()
    {
        var owner = await SeedOwnerAsync();
        await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);
        using var http = _host.CreateClient();
        using var rotated = await RefreshAsync(http, s2.RefreshToken);
        var current = await ReadSessionAsync(rotated);

        using var reused = await RefreshAsync(http, s2.RefreshToken);

        await AssertRefreshTokenInvalidAsync(reused);
        Assert.All(await RefreshTokensOfAsync(_host.Services, owner.UserId), row => Assert.True(row.IsRevoked));
        using var withCurrent = await RefreshAsync(http, current.RefreshToken);
        await AssertRefreshTokenInvalidAsync(withCurrent);
    }

    [Fact]
    public async Task PLAN0018_CA131_RefreshSessionReplaced_ElLogNoRegistraElSidDeNingunaSesion()
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);

        using var response = await RefreshAsync(_host.CreateClient(), s1.RefreshToken);

        await AssertSessionReplacedAsync(response);
        var sids = new[] { SessionIdOf(s1.RawAccessToken), SessionIdOf(s2.RawAccessToken) }
            .SelectMany(sid => new[] { sid.ToString("D"), sid.ToString("N") })
            .ToList();
        Assert.DoesNotContain(_logs.Entries, entry => sids.Any(sid =>
            entry.Message.Contains(sid, StringComparison.OrdinalIgnoreCase)));
    }

    // ── CA-132: logout ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA132_LogoutConElRefreshDeS2_DosSesiones_SoloRevocaEseSinCambiarLaSesionYElAccessDeS2Sigue200()
    {
        var owner = await SeedOwnerAsync();
        var other = await SeedOwnerAsync();
        await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);
        var w = await LoginOwnerAsync(other);
        var sid2 = SessionIdOf(s2.RawAccessToken);
        var sibling = await SeedRefreshTokenAsync(_host.Services, owner, sid2);
        var otherBefore = await SnapshotAsync(other.UserId);

        using var logout = await LogoutAsync(_host.CreateClient(), s2.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.True((await RefreshTokenRowAsync(_host.Services, s2.RefreshToken)).IsRevoked);
        Assert.False((await RefreshTokenRowAsync(_host.Services, sibling)).IsRevoked);
        Assert.Equal(sid2, await CurrentSessionIdAsync(_host.Services, owner.UserId));
        using var me = await Bearer(_host, s2.RawAccessToken).GetAsync(ProfileEndpoint);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(otherBefore, await SnapshotAsync(other.UserId));
        using var withW = await RefreshAsync(_host.CreateClient(), w.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, withW.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA132_LogoutConElRefreshDeS1_DosSesiones_Responde204SinEscribirYS2SigueSirviendo()
    {
        var owner = await SeedOwnerAsync();
        var other = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);
        await LoginOwnerAsync(other);
        // Un refresh de S1 sin revocar (como el que deja un login concurrente, RF-90 g): el logout tampoco lo toca.
        var s1NotRevoked = await SeedRefreshTokenAsync(_host.Services, owner, SessionIdOf(s1.RawAccessToken));
        var ownerBefore = await SnapshotAsync(owner.UserId);
        var otherBefore = await SnapshotAsync(other.UserId);
        using var http = _host.CreateClient();

        using var revoked    = await LogoutAsync(http, s1.RefreshToken);
        using var notRevoked = await LogoutAsync(http, s1NotRevoked);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, notRevoked.StatusCode);
        Assert.Equal(ownerBefore, await SnapshotAsync(owner.UserId));
        Assert.Equal(otherBefore, await SnapshotAsync(other.UserId));
        using var withS2 = await RefreshAsync(http, s2.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, withS2.StatusCode);
    }

    // ── CA-T18B-4 (CA-132, CA-133): carreras con un login o una rotación ────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA132_LogoutDeS2ConRotacionSimultaneaDeS2_DosSesiones_RevocaSoloLosRefreshDeS2()
    {
        var owner = await SeedOwnerAsync();
        var other = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);
        await LoginOwnerAsync(other);
        var s1NotRevoked = await SeedRefreshTokenAsync(_host.Services, owner, SessionIdOf(s1.RawAccessToken));
        var otherBefore = await SnapshotAsync(other.UserId);
        HttpResponseMessage? competitor = null;
        using var host = WithInterleaving(afterRead: RunOnceFor(RefreshToken.HashOf(s2.RefreshToken),
            async http => competitor = await RefreshAsync(http, s2.RefreshToken)));

        using var logout = await LogoutAsync(host.CreateClient(), s2.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using (competitor)
        {
            var rotated = await ReadSessionAsync(competitor!);
            var sid2 = SessionIdOf(s2.RawAccessToken);
            Assert.Equal(sid2, SessionIdOf(rotated.RawAccessToken));
            var rows = await RefreshTokensOfAsync(_host.Services, owner.UserId);
            Assert.All(rows.Where(r => r.SessionId == sid2), row => Assert.True(row.IsRevoked));
            Assert.Contains(rows, r => r.TokenHash == RefreshToken.HashOf(rotated.RefreshToken));
            Assert.False((await RefreshTokenRowAsync(_host.Services, s1NotRevoked)).IsRevoked);
            Assert.Equal(otherBefore, await SnapshotAsync(other.UserId));
        }
    }

    [Fact]
    public async Task PLAN0018_CA133_RefreshDeS1QueChocaConUnLoginNuevo_DosSesiones_SessionReplacedSinRevocarElRefreshDeS2()
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        HttpResponseMessage? login = null;
        // El login de S2 confirma entre la lectura del usuario (S1 aún vigente) y el reclamo del token de S1.
        using var host = WithInterleaving(beforeUpdate: RunOnceFor(RefreshToken.HashOf(s1.RefreshToken),
            async http => login = await LoginAsync(http, owner.Email, Password)));

        using var refresh = await RefreshAsync(host.CreateClient(), s1.RefreshToken);

        await AssertSessionReplacedAsync(refresh);
        using (login)
        {
            var s2 = await ReadSessionAsync(login!);
            Assert.Equal(SessionIdOf(s2.RawAccessToken), await CurrentSessionIdAsync(_host.Services, owner.UserId));
            Assert.False((await RefreshTokenRowAsync(_host.Services, s2.RefreshToken)).IsRevoked);
            using var withS2 = await RefreshAsync(_host.CreateClient(), s2.RefreshToken);
            Assert.Equal(HttpStatusCode.OK, withS2.StatusCode);
        }
    }

    // ── CA-T18B-2 (CA-130): lo que conserva la sesión ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA130_RefreshDeS2_ConservaSidYSessionIdSinCambiarCurrentSessionIdTambienHaciaOrganizacionSinMembresia()
    {
        var owner = await SeedOwnerAsync();
        var s2 = await LoginOwnerAsync(owner);
        var sid = SessionIdOf(s2.RawAccessToken);
        using var http = _host.CreateClient();

        using var plain = await RefreshAsync(http, s2.RefreshToken);
        var rotated = await ReadSessionAsync(plain);
        using var foreign = await RefreshAsync(http, rotated.RefreshToken, activeOrgId: Guid.NewGuid());
        var fallback = await ReadSessionAsync(foreign);

        foreach (var issued in new[] { rotated, fallback })
        {
            Assert.Equal(sid, SessionIdOf(issued.RawAccessToken));
            Assert.Equal(sid, (await RefreshTokenRowAsync(_host.Services, issued.RefreshToken)).SessionId);
        }
        Assert.Equal(sid, await CurrentSessionIdAsync(_host.Services, owner.UserId));
        Assert.False((await RefreshTokenRowAsync(_host.Services, fallback.RefreshToken)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_CA130_SwitchHaciaB_EmiteTokenConElMismoSidSinCambiarLaSesion()
    {
        var tenants = await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(Password)).BuildAsync();
        var owner = tenants.A.Owner;
        await tenants.SeedAsync(OrganizationMember.Create(
            tenants.B.OrganizationId, owner.UserId, OrgRole.Member, invitedBy: tenants.B.Owner.UserId));
        var session = await LoginOwnerAsync(owner);
        var sid = SessionIdOf(session.RawAccessToken);

        using var response = await SwitchAsync(Bearer(_host, session.RawAccessToken), tenants.B.OrganizationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accessToken = await AccessTokenOfAsync(response);
        Assert.Equal(tenants.B.OrganizationId.ToString(), ClaimOf(accessToken, TestUserJwt.ActiveOrgClaim));
        Assert.Equal(sid, SessionIdOf(accessToken));
        Assert.Equal(sid, await CurrentSessionIdAsync(_host.Services, owner.UserId));
        Assert.False((await RefreshTokenRowAsync(_host.Services, session.RefreshToken)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_CA130_ReauthNonceYDeleteOrganizationConContrasena_NoCambianLaSesionNiRevocanElRefresh()
    {
        var tenant = await SeedTenantAsync();
        var owner = tenant.Owner;
        var session = await LoginOwnerAsync(owner);
        var sid = SessionIdOf(session.RawAccessToken);
        using var http = Bearer(_host, session.RawAccessToken);

        using (var nonce = await http.GetAsync($"{ReauthEndpoint}?action={ReauthAction.OrganizationDelete}"))
            Assert.Equal(HttpStatusCode.OK, nonce.StatusCode);
        Assert.Equal(sid, await CurrentSessionIdAsync(_host.Services, owner.UserId));
        Assert.False((await RefreshTokenRowAsync(_host.Services, session.RefreshToken)).IsRevoked);

        using (var delete = await OrganizationDeletionApi.DeleteAsync(http, tenant.Organization.Name, Password))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(sid, await CurrentSessionIdAsync(_host.Services, owner.UserId));
        Assert.False((await RefreshTokenRowAsync(_host.Services, session.RefreshToken)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_CA130_OwnerSinOrganizacion_LoginEmiteSidYSwitchTrasRestaurarLoConserva()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithPlan(Plan.Free).WithOwnerPassword(Password))
            .BuildAsync();
        var a = tenants.A;
        await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(_host.Services, Plan.Free, a.LegalType, 1);
        await OrganizationRestoreApi.MarkDeletedAsync(_host.Services, a.OrganizationId, a.Owner.UserId, DateTime.UtcNow.AddDays(-1));

        var session = await LoginOwnerAsync(a.Owner);
        Assert.Null(session.ActiveOrgId);
        var sid = SessionIdOf(session.RawAccessToken);
        Assert.Equal(sid, await CurrentSessionIdAsync(_host.Services, a.Owner.UserId));
        Assert.Equal(sid, (await RefreshTokenRowAsync(_host.Services, session.RefreshToken)).SessionId);

        using var http = Bearer(_host, session.RawAccessToken);
        using (var restore = await OrganizationRestoreApi.RestoreAsync(http, a.OrganizationId))
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        using var response = await SwitchAsync(http, a.OrganizationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(sid, SessionIdOf(await AccessTokenOfAsync(response)));
        Assert.Equal(sid, await CurrentSessionIdAsync(_host.Services, a.Owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA130_LoginConContrasenaIncorrectaRechazadoCon429OGoogleSinEmailVerificado_NoTocanLaSesion()
    {
        var owner = await SeedOwnerAsync(googleId: $"google-{Guid.NewGuid():N}");
        var session = await LoginOwnerAsync(owner);
        var before = await SnapshotAsync(owner.UserId);

        using (var wrong = await LoginAsync(_host.CreateClient(), owner.Email, Password + "-mal"))
            await AssertGenericErrorAsync(wrong, CredentialsField);

        var idToken = _google.Register(new GoogleUserInfo(owner.GoogleId!, owner.Email, owner.FirstName, owner.LastName,
            EmailVerified: false));
        using (var google = await _host.CreateClient().PostAsJsonAsync(GoogleEndpoint, new { idToken }))
            Assert.Equal(HttpStatusCode.Forbidden, google.StatusCode);

        // El 429 de RF-29 lo da el limitador real con sus umbrales por defecto, antes del comando: tras agotar el cupo
        // de IP + email con contraseñas incorrectas, el login con la correcta no llega a emitir.
        using (var limitedHost = AuthRateLimitApiFactory.WithDefaultAuthRateLimits(_host))
        using (var limitedHttp = limitedHost.CreateClient())
        {
            var permitLimit = AuthRateLimitApiFactory.DefaultPermitLimit(
                AuthRateLimitPolicies.Login, AuthRateLimitPartition.IpAndEmail);
            for (var attempt = 0; attempt < permitLimit; attempt++)
                using (var wrongAttempt = await LoginAsync(limitedHttp, owner.Email, Password + "-mal"))
                    await AssertGenericErrorAsync(wrongAttempt, CredentialsField);

            using var limited = await LoginAsync(limitedHttp, owner.Email, Password);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }

        Assert.Equal(before, await SnapshotAsync(owner.UserId));
        Assert.Equal(SessionIdOf(session.RawAccessToken), await CurrentSessionIdAsync(_host.Services, owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA130_LoginRf61DeUnUsuarioConSesionQuePerdioSuUnicaMembresia_NoCambiaSuCurrentSessionId()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithMember(OrgRole.Member).WithMemberPassword(Password))
            .BuildAsync();
        var member = tenants.A.Member(OrgRole.Member);
        var session = await LoginOwnerAsync(member);
        var sid = SessionIdOf(session.RawAccessToken);
        await RemoveMembershipAsync(_host.Services, tenants.A.OrganizationId, member.UserId);

        using var response = await LoginAsync(_host.CreateClient(), member.Email, Password);

        await AssertGenericErrorAsync(response, CredentialsField);
        Assert.Equal(sid, await CurrentSessionIdAsync(_host.Services, member.UserId));
        Assert.False((await RefreshTokenRowAsync(_host.Services, session.RefreshToken)).IsRevoked);
    }

    // ── Contrato del 401 (RNF-10) ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA131_SessionReplacedResponse_CuerpoExactoSoloErrorYCode()
    {
        Assert.Equal(SessionReplacedBody, Zentory.API.Http.SessionReplacedResponse.Body);
        using var body = JsonDocument.Parse(Zentory.API.Http.SessionReplacedResponse.Body);
        Assert.Equal(["error", "code"], body.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(Zentory.Application.Exceptions.SessionCodes.SessionReplaced,
            body.RootElement.GetProperty("code").GetString());
    }

    // ── Lectura del claim (D-47): un solo lector, con los dos nombres ───────────────────────────────────────────

    [Theory]
    [InlineData(SessionClaims.Sid)]
    [InlineData(System.Security.Claims.ClaimTypes.Sid)]
    public void PLAN0018_CA129_SessionClaimsTryRead_AceptaSidYClaimTypesSid(string claimType)
    {
        var sid = Guid.NewGuid();
        var principal = PrincipalWith(new System.Security.Claims.Claim(claimType, sid.ToString("D")));

        Assert.True(SessionClaims.TryRead(principal, out var read));
        Assert.Equal(sid, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void PLAN0018_CA129_SessionClaimsTryRead_SinSidLegible_DevuelveFalse(string? value)
    {
        var principal = value is null
            ? PrincipalWith()
            : PrincipalWith(new System.Security.Claims.Claim(SessionClaims.Sid, value));

        Assert.False(SessionClaims.TryRead(principal, out _));
        Assert.False(SessionClaims.TryRead(null, out _));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static System.Security.Claims.ClaimsPrincipal PrincipalWith(params System.Security.Claims.Claim[] claims)
        => new(new System.Security.Claims.ClaimsIdentity(claims, authenticationType: "test"));

    private async Task<User> SeedOwnerAsync(string? googleId = null) => (await SeedTenantAsync(googleId)).Owner;

    // Organización A con su owner, con contraseña y, si se pide, Google vinculado.
    private async Task<SeededTenant> SeedTenantAsync(string? googleId = null)
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a =>
            {
                a.WithOwnerPassword(Password);
                if (googleId is not null) a.WithOwnerGoogleId(googleId);
            })
            .BuildAsync();
        return tenants.A;
    }

    private async Task<IssuedSession> LoginOwnerAsync(User user)
    {
        using var http = _host.CreateClient();
        using var response = await LoginAsync(http, user.Email, Password);
        return await ReadSessionAsync(response);
    }

    private async Task SeedRawRefreshTokenAsync(User user, string token, DateTime expiresAt, Guid? sessionId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var row = RefreshToken.Create(user.UserId, token, expiresAt, sessionId ?? Guid.NewGuid());
        db.RefreshTokens.Add(row);
        // Token anterior a M2c: session_id nulo (el dominio no los crea así).
        if (sessionId is null)
            db.Entry(row).Property(r => r.SessionId).CurrentValue = null;
        await db.SaveChangesAsync();
    }

    // Foto de la sesión del usuario: su sesión vigente y el estado de cada refresh token.
    private async Task<string> SnapshotAsync(Guid userId)
    {
        var current = await CurrentSessionIdAsync(_host.Services, userId);
        var rows = (await RefreshTokensOfAsync(_host.Services, userId))
            .OrderBy(r => r.Id)
            .Select(r => $"{r.Id}:{r.SessionId}:{r.IsRevoked}");
        return $"{current}|{string.Join(",", rows)}";
    }

    private static async Task<string> AccessTokenOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    // Ejecuta la operación competidora una sola vez, para el token con ese hash, en un cliente del host derivado.
    private static Func<WebApplicationFactory<Program>, Func<string, Task>> RunOnceFor(
        string tokenHash, Func<HttpClient, Task> competitor)
    {
        var pending = 1;
        return host => async hash =>
        {
            if (hash != tokenHash || Interlocked.Exchange(ref pending, 0) == 0)
                return;
            using var http = host.CreateClient();
            await competitor(http);
        };
    }

    private WebApplicationFactory<Program> WithInterleaving(
        Func<WebApplicationFactory<Program>, Func<string, Task>>? afterRead    = null,
        Func<WebApplicationFactory<Program>, Func<string, Task>>? beforeUpdate = null)
    {
        WebApplicationFactory<Program>? host = null;
        host = _host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRefreshTokenRepository>();
            services.AddScoped<IRefreshTokenRepository>(sp => new InterleavingRefreshTokenRepository(
                new RefreshTokenRepository(sp.GetRequiredService<ZentoryDbContext>()),
                afterRead is null ? null : hash => afterRead(host!)(hash),
                beforeUpdate is null ? null : token => beforeUpdate(host!)(token.TokenHash)));
        }));
        return host;
    }
}
