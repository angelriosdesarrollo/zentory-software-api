using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Middleware;
using Zentory.Application.Auth.Services;
using Zentory.Application.CollaboratorPortal;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T18c (RF-90 d, g; RF-14; RNF-1; RNF-10; D-47 "Middleware" y "Tests"; DEC-0095, C-16) sobre el host real:
// MembershipValidationMiddleware compara el sid del access token con users.current_session_id antes de la membresía y
// del estado de la organización, también en las acciones con [SkipMembershipValidation], y no en las [AllowAnonymous].
// Las sesiones reemplazadas salen de dos inicios de sesión reales del mismo usuario; los tests que abren dos sesiones lo
// dicen en su nombre (R-25).
//
// CA-129 (parte de acceso), CA-133, CA-134, CA-T18C-1 y CA-T18C-2 (partes de CA-133).
public sealed class SessionReplacedAccessTests
    : IClassFixture<ZentoryApiFactory>, IClassFixture<AuthorizationProbeApiFactory>, IDisposable
{
    private const string Password               = "Owner-Sesion-Reemplazada-2026!";
    private const string ClientsEndpoint        = "/api/v1/clients";
    private const string ReauthNonceEndpoint    = "/api/v1/auth/reauth-nonce?action=" + ReauthAction.OrganizationDelete;
    private const string RegisterEndpoint       = "/api/v1/auth/register";
    private const string ResendVerificationEndpoint = OneTimeTokenApi.ResendEndpoint;
    private const string PortalInvoicesEndpoint = "/api/v1/collaborator-portal/payout-invoices";
    private const int    ConcurrentRepetitions  = 20;

    private readonly ZentoryApiFactory              _factory;
    private readonly AuthorizationProbeApiFactory   _probe;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly WebApplicationFactory<Program> _host;

    public SessionReplacedAccessTests(ZentoryApiFactory factory, AuthorizationProbeApiFactory probe)
    {
        _factory = factory;
        _probe   = probe;
        _host    = WithGoogle(factory, _google);
    }

    public void Dispose() => _host.Dispose();

    // ── CA-129: access token de una sesión reemplazada ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA129_AccessDeS1TrasLoginS2_DosSesiones_ClientsYMeResponden401SessionReplacedYS2Responde200()
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);

        Assert.NotEqual(SessionIdOf(s1.RawAccessToken), SessionIdOf(s2.RawAccessToken));
        Assert.Equal(SessionIdOf(s2.RawAccessToken), await CurrentSessionIdAsync(_host.Services, owner.UserId));
        foreach (var path in new[] { ClientsEndpoint, ProfileEndpoint })
        {
            using var replaced = await GetAsync(path, s1.RawAccessToken);
            await AssertSessionReplacedAsync(replaced);

            using var current = await GetAsync(path, s2.RawAccessToken);
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        }
    }

    [Fact]
    public async Task PLAN0018_CA129_AccessDeS1TrasGoogleS2_DosSesiones_ClientsYMeResponden401SessionReplacedYS2Responde200()
    {
        var owner = await SeedOwnerAsync(googleId: $"google-{Guid.NewGuid():N}");
        var s1 = await LoginOwnerAsync(owner);
        using var google = await GoogleAsync(_host.CreateClient(), _google, owner.GoogleId!, owner);
        var s2 = await ReadSessionAsync(google);

        foreach (var path in new[] { ClientsEndpoint, ProfileEndpoint })
        {
            using var replaced = await GetAsync(path, s1.RawAccessToken);
            await AssertSessionReplacedAsync(replaced);

            using var current = await GetAsync(path, s2.RawAccessToken);
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        }
    }

    [Fact]
    public async Task PLAN0018_CA129_RegisterDeUnaCuentaNueva_SuAccessTokenConElSidVigenteResponde200EnMe()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        using var response = await _host.CreateClient().PostAsJsonAsync(RegisterEndpoint, new
        {
            email         = $"registro-acceso-{Guid.NewGuid():N}@zentory.test",
            password      = Password,
            firstName     = "Registro",
            lastName      = "Acceso",
            orgName       = $"Org acceso {Guid.NewGuid():N}",
            legalType     = LegalType.Freelance,
            acceptedTerms = true,
        });
        var session = await ReadSessionAsync(response, HttpStatusCode.Created);

        Assert.Equal(SessionIdOf(session.RawAccessToken),
            await CurrentSessionIdAsync(_host.Services, Guid.Parse(session.Claim("sub")!)));
        using var me = await GetAsync(ProfileEndpoint, session.RawAccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA129_PortalDeColaboradoresConElMismoEmailAbiertoAntesDeS2_DosSesiones_SigueRespondiendo200()
    {
        var tenant = await SeedTenantAsync();
        var owner = tenant.Owner;
        var collaborator = Collaborator.Create(tenant.OrganizationId, "Persona", "hourly_contractor", email: owner.Email);
        await SeedAsync(collaborator);
        var portal = _factory.Tokens.ForCollaborator(owner.Email, [collaborator.Id], collaborator.Id,
            tenant.OrganizationId, tenant.Organization.Name, CollaboratorAuthMethod.MagicLink);
        using (var before = await GetAsync(PortalInvoicesEndpoint, portal))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var s1 = await LoginOwnerAsync(owner);
        await LoginOwnerAsync(owner);

        using var after = await GetAsync(PortalInvoicesEndpoint, portal);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var replaced = await GetAsync(ClientsEndpoint, s1.RawAccessToken);
        await AssertSessionReplacedAsync(replaced);
    }

    // ── CA-133: concurrencia, orden frente a la membresía y acciones excluidas ──────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA133_DosLoginsConcurrentes_DosSesiones_VeinteVecesSoloLaVigenteResponde200EnMeYRefreshYLaOtraSessionReplaced()
    {
        for (var repetition = 0; repetition < ConcurrentRepetitions; repetition++)
        {
            var owner = await SeedOwnerAsync();
            var logins = await Task.WhenAll(LoginRawAsync(owner), LoginRawAsync(owner));

            var sessions = new List<IssuedSession>();
            foreach (var login in logins)
                using (login)
                    sessions.Add(await ReadSessionAsync(login));

            var current = await CurrentSessionIdAsync(_host.Services, owner.UserId);
            var winners = sessions.Where(s => SessionIdOf(s.RawAccessToken) == current).ToList();
            var winner = Assert.Single(winners);
            var loser = sessions.Single(s => !ReferenceEquals(s, winner));

            using (var me = await GetAsync(ProfileEndpoint, loser.RawAccessToken))
                await AssertSessionReplacedAsync(me);
            using (var refresh = await RefreshAsync(_host.CreateClient(), loser.RefreshToken))
                await AssertSessionReplacedAsync(refresh);

            using (var me = await GetAsync(ProfileEndpoint, winner.RawAccessToken))
                Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            using (var refresh = await RefreshAsync(_host.CreateClient(), winner.RefreshToken))
                Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        }
    }

    [Fact]
    public async Task PLAN0018_CA133_S1ReemplazadaYMembresiaBorrada_DosSesiones_AccessDeS1RecibeSessionReplacedNoMembershipRevoked()
    {
        var tenant = await SeedTenantAsync();
        var s1 = await LoginOwnerAsync(tenant.Owner);
        var s2 = await LoginOwnerAsync(tenant.Owner);
        await RemoveMembershipAsync(_host.Services, tenant.OrganizationId, tenant.Owner.UserId);

        using var replaced = await GetAsync(ClientsEndpoint, s1.RawAccessToken);
        await AssertSessionReplacedAsync(replaced);

        // Control: la sesión vigente sí recibe la membresía revocada, así que el escenario está armado.
        using var current = await GetAsync(ClientsEndpoint, s2.RawAccessToken);
        await AssertUnauthorizedCodeAsync(current, MembershipValidationMiddleware.MembershipRevokedCode);
    }

    [Fact]
    public async Task PLAN0018_CA133_S1ReemplazadaYOrganizacionEliminada_DosSesiones_AccessDeS1RecibeSessionReplacedNoOrganizationDeleted()
    {
        var tenant = await SeedTenantAsync();
        var s1 = await LoginOwnerAsync(tenant.Owner);
        var s2 = await LoginOwnerAsync(tenant.Owner);
        await OrganizationRestoreApi.MarkDeletedAsync(_host.Services, tenant.OrganizationId, tenant.Owner.UserId, DateTime.UtcNow);

        using var replaced = await GetAsync(ClientsEndpoint, s1.RawAccessToken);
        await AssertSessionReplacedAsync(replaced);

        using var current = await GetAsync(ClientsEndpoint, s2.RawAccessToken);
        await AssertUnauthorizedCodeAsync(current, MembershipValidationMiddleware.OrganizationDeletedCode);
    }

    [Fact]
    public async Task PLAN0018_CA133_AccessDeS1EnAccionesExcluidas_DosSesiones_MeOrganizationsSwitchYReauthNonceRecibenSessionReplaced()
    {
        // El owner de A también es miembro de B, así el switch de control tiene destino.
        var tenants = await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(Password)).BuildAsync();
        var owner = tenants.A.Owner;
        await SeedAsync(OrganizationMember.Create(tenants.B.OrganizationId, owner.UserId, OrgRole.Member));
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);

        using (var organizations = await GetAsync(MyOrgsEndpoint, s1.RawAccessToken))
            await AssertSessionReplacedAsync(organizations);
        using (var http = Bearer(_host, s1.RawAccessToken))
        using (var switched = await SwitchAsync(http, tenants.B.OrganizationId))
            await AssertSessionReplacedAsync(switched);
        using (var nonce = await GetAsync(ReauthNonceEndpoint, s1.RawAccessToken))
            await AssertSessionReplacedAsync(nonce);

        // Control: la sesión vigente pasa las mismas acciones.
        using (var organizations = await GetAsync(MyOrgsEndpoint, s2.RawAccessToken))
            Assert.Equal(HttpStatusCode.OK, organizations.StatusCode);
        using (var http = Bearer(_host, s2.RawAccessToken))
        using (var switched = await SwitchAsync(http, tenants.B.OrganizationId))
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        using (var nonce = await GetAsync(ReauthNonceEndpoint, s2.RawAccessToken))
            Assert.Equal(HttpStatusCode.OK, nonce.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA133_ResendVerificationConSesionReemplazada_Recibe401SessionReplaced()
    {
        // T19b (RF-90 d, DEC-0098): resend-verification es [Authorize] dentro de AuthController ([SkipMembershipValidation]
        // de clase), así que el middleware compara el sid por clave primaria, sin la membresía. Dos sesiones (R-25).
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);

        using (var http = Bearer(_host, s1.RawAccessToken))
        using (var replaced = await http.PostAsync(ResendVerificationEndpoint, content: null))
            await AssertSessionReplacedAsync(replaced);

        // Control: la sesión vigente reenvía (202).
        using (var http = Bearer(_host, s2.RawAccessToken))
        using (var current = await http.PostAsync(ResendVerificationEndpoint, content: null))
            Assert.Equal(HttpStatusCode.Accepted, current.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA133_AccessTokenBienFirmadoSinSid_Recibe401SinCodigoSessionReplaced()
    {
        var tenant = await SeedTenantAsync();
        var withoutSid = TestUserJwt.WithoutSession(tenant.Owner, tenant.OrganizationId.ToString());

        foreach (var path in new[] { ClientsEndpoint, ProfileEndpoint, MyOrgsEndpoint })
        {
            using var response = await GetAsync(path, withoutSid);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(MembershipValidationMiddleware.MissingSessionBody, body);
            Assert.DoesNotContain(SessionCodesSessionReplaced, body, StringComparison.Ordinal);
        }

        // Control: el mismo token con el sid de la sesión vigente pasa.
        using var signed = await GetAsync(ProfileEndpoint,
            TestUserJwt.Sign(_factory.Services, tenant.Owner, tenant.OrganizationId.ToString()));
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA133_RutaConMembresia_ElMiddlewareNoSumaConsultasALasDeFindActiveAsync()
    {
        var counter = new DbCommandCounter();
        await using var host = _probe.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => DbCommandCounter.Install(services, counter)));
        var (owner, organization, token) = await SeedOwnerInAsync(host.Services);

        // Lo que cuesta la lectura de membresía de RNF-1 por sí sola, en el mismo host.
        int findActive;
        using (var scope = host.Services.CreateScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IUserMembershipReader>();
            counter.Reset();
            var membership = await reader.FindActiveAsync(owner.UserId, organization.OrganizationId);
            findActive = counter.Count;
            Assert.NotNull(membership);
            Assert.Equal(SessionIdOf(token), membership.CurrentSessionId);
        }

        // La acción de prueba no consulta la base: todo lo que cuenta la petición es del pipeline.
        using var http = Bearer(host, token);
        counter.Reset();
        using var response = await http.GetAsync(AuthorizationProbeApiFactory.RequireOrgAdminPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(findActive > 0, "El contador no vio la lectura de membresía.");
        Assert.Equal(findActive, counter.Count);
        // Desde T25 (D-26; RNF-1: dos consultas indexadas como máximo) la lectura de membresía trae también el plan
        // efectivo y el nombre del owner: membresía + organización + sesión + nombre del owner en una consulta, y el plan
        // del owner en la otra. Ni una más.
        Assert.Equal(MaxQueriesPerMembershipRequest, counter.Count);
    }

    private const int MaxQueriesPerMembershipRequest = 2;

    [Fact]
    public async Task PLAN0018_CA133_ContadorDeComandos_VeLaLecturaPorClaveDeUnaAccionExcluida()
    {
        // Control del anterior: el contador está conectado al pipeline y cuenta la lectura por clave de D-47 paso 3.
        var counter = new DbCommandCounter();
        await using var host = _probe.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => DbCommandCounter.Install(services, counter)));
        var (_, _, token) = await SeedOwnerInAsync(host.Services);
        using var http = Bearer(host, token);

        counter.Reset();
        using var response = await http.GetAsync(AuthorizationProbeApiFactory.SkippedOrgOwnerPath);

        Assert.Equal(1, counter.Count);
    }

    // Decisión del orquestador que extiende C-16: las rutas públicas (sin [Authorize]) no comparan la sesión aunque
    // llegue un Bearer, porque el cliente lo adjunta en toda petición. Responden lo mismo que sin token.
    [Theory]
    [InlineData("/api/v1/public/master/currencies")]
    [InlineData("/api/v1/public/plans")]
    [InlineData("/api/v1/public/proposals/7d1c3f0e-6b8a-4c55-9f1e-2a4b6c8d0e13")]
    public async Task PLAN0018_CA133_RutaPublicaConBearerDeSesionReemplazada_NoRecibeSessionReplaced(string path)
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        await LoginOwnerAsync(owner);
        using (var replaced = await GetAsync(ClientsEndpoint, s1.RawAccessToken))
            await AssertSessionReplacedAsync(replaced);

        using var withBearer = await GetAsync(path, s1.RawAccessToken);
        using var anonymous = await _host.CreateClient().GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, withBearer.StatusCode);
        Assert.Equal(anonymous.StatusCode, withBearer.StatusCode);
        Assert.DoesNotContain(SessionCodesSessionReplaced, await withBearer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void PLAN0018_CA133_HostSinFallbackPolicy_UnaAccionSinAuthorizeEsAnonimaYNoComparaLaSesion()
    {
        // El middleware decide por metadatos (IAuthorizeData sin IAllowAnonymous): con una FallbackPolicy, una acción sin
        // [Authorize] exigiría usuario y esa regla dejaría de valer.
        var options = _factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Authorization.AuthorizationOptions>>()
            .Value;

        Assert.Null(options.FallbackPolicy);
    }

    // ── CA-133: tokens mal formados y sesión sin valor (tests de QA de TK-0081) ─────────────────────────────────

    public static IEnumerable<object?[]> InvalidSubjects() =>
    [
        ["no-es-guid"], [Guid.Empty.ToString()], [null], [""],
    ];

    [Theory]
    [MemberData(nameof(InvalidSubjects))]
    public async Task PLAN0018_CA133_TokenConSidYSubInvalidoOAusente_Responde401NuncaPasaNiDa500(string? sub)
    {
        var tenant = await SeedTenantAsync();
        var token = TestUserJwt.Raw(sub, Guid.NewGuid().ToString("D"), tenant.OrganizationId.ToString());

        foreach (var path in new[] { ProfileEndpoint, ClientsEndpoint, MyOrgsEndpoint, ReauthNonceEndpoint })
        {
            using var response = await GetAsync(path, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("no-es-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("N")]
    [InlineData("")]
    public async Task PLAN0018_CA133_TokenConSidIlegible_Responde401SinCodigo(string sidKind)
    {
        var tenant = await SeedTenantAsync();
        // "N": un Guid válido pero sin guiones; SessionClaims solo acepta el formato D.
        var sid = sidKind == "N" ? Guid.NewGuid().ToString("N") : sidKind;
        var token = TestUserJwt.Raw(tenant.Owner.UserId.ToString(), sid, tenant.OrganizationId.ToString());

        foreach (var path in new[] { ProfileEndpoint, ClientsEndpoint })
        {
            using var response = await GetAsync(path, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(MembershipValidationMiddleware.MissingSessionBody, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task PLAN0018_CA133_UsuarioConCurrentSessionIdNulo_CualquierSidRecibeSessionReplaced()
    {
        var tenant = await SeedTenantAsync();
        var owner = tenant.Owner;
        Assert.Null(await CurrentSessionIdAsync(_host.Services, owner.UserId));
        var token = TestUserJwt.Raw(owner.UserId.ToString(), Guid.NewGuid().ToString("D"), tenant.OrganizationId.ToString());

        foreach (var path in new[] { ProfileEndpoint, ClientsEndpoint, MyOrgsEndpoint })
        {
            using var response = await GetAsync(path, token);
            await AssertSessionReplacedAsync(response);
        }
    }

    [Fact]
    public async Task PLAN0018_CA133_SesionReemplazadaConTokenSinActiveOrgId_DosSesiones_RutaConMembresiaRecibeSessionReplacedNoMembershipRevoked()
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        await LoginOwnerAsync(owner);
        var token = TestUserJwt.Raw(owner.UserId.ToString(), SessionIdOf(s1.RawAccessToken).ToString("D"), activeOrgId: null);

        using var response = await GetAsync(ClientsEndpoint, token);

        await AssertSessionReplacedAsync(response);
    }

    // ── CA-T18C-1 (CA-133): tokens de los helpers de tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA133_TokensDeTestTokensYTestUserJwt_SidIgualACurrentSessionId_Responden200EnMeYEnRutaConMembresia()
    {
        var tenant = await SeedTenantAsync();
        var fromTokens = _factory.Tokens.ForUser(tenant.Owner, tenant.Organization, OrgRole.Owner, tenant.Plan);
        var fromJwt = TestUserJwt.Sign(_factory.Services, tenant.Owner, tenant.OrganizationId.ToString());

        var current = await CurrentSessionIdAsync(_factory.Services, tenant.Owner.UserId);
        Assert.NotNull(current);
        Assert.Equal(current, SessionIdOf(fromTokens));
        Assert.Equal(current, SessionIdOf(fromJwt));
        foreach (var token in new[] { fromTokens, fromJwt })
            foreach (var path in new[] { ProfileEndpoint, ClientsEndpoint })
            {
                using var response = await GetAsync(path, token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
    }

    [Fact]
    public async Task PLAN0018_CA133_DosTokensDelMismoUsuarioEnDosOrganizacionesConLosHelpers_LosDosResponden200()
    {
        var tenants = await new TenantBuilder(_factory).BuildAsync();
        var user = tenants.A.Owner;
        await SeedAsync(OrganizationMember.Create(tenants.B.OrganizationId, user.UserId, OrgRole.Member));

        var inA = _factory.Tokens.ForUser(user, tenants.A.Organization, OrgRole.Owner, tenants.A.Plan);
        var inB = _factory.Tokens.ForUser(user, tenants.B.Organization, OrgRole.Member, tenants.B.Plan);

        Assert.Equal(SessionIdOf(inA), SessionIdOf(inB));
        foreach (var token in new[] { inA, inB })
            foreach (var path in new[] { ClientsEndpoint, ProfileEndpoint })
            {
                using var response = await GetAsync(path, token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
    }

    [Fact]
    public async Task PLAN0018_CA133_TestTokensTrasUnLoginReal_ReutilizaLaSesionDelLoginYNoLaExpulsa()
    {
        var tenant = await SeedTenantAsync();
        var login = await LoginOwnerAsync(tenant.Owner);

        var helper = _factory.Tokens.ForUser(tenant.Owner, tenant.Organization, OrgRole.Owner, tenant.Plan);

        Assert.Equal(SessionIdOf(login.RawAccessToken), SessionIdOf(helper));
        using var fromLogin = await GetAsync(ClientsEndpoint, login.RawAccessToken);
        Assert.Equal(HttpStatusCode.OK, fromLogin.StatusCode);
    }

    // ── CA-T18C-2 (CA-133): acciones [AllowAnonymous] con el Bearer de una sesión reemplazada ───────────────────

    [Fact]
    public async Task PLAN0018_CA133_LoginConBearerDeSesionReemplazada_DosSesiones_Responde200YAbreUnaSesionNueva()
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);

        using var http = Bearer(_host, s1.RawAccessToken);
        using var response = await LoginAsync(http, owner.Email, Password);

        var s3 = await ReadSessionAsync(response);
        var sid3 = SessionIdOf(s3.RawAccessToken);
        Assert.NotEqual(SessionIdOf(s2.RawAccessToken), sid3);
        Assert.Equal(sid3, await CurrentSessionIdAsync(_host.Services, owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA133_RefreshDeLaSesionVigenteConBearerDeSesionReemplazada_DosSesiones_Responde200()
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);

        using var http = Bearer(_host, s1.RawAccessToken);
        using var response = await RefreshAsync(http, s2.RefreshToken);

        var refreshed = await ReadSessionAsync(response);
        Assert.Equal(SessionIdOf(s2.RawAccessToken), SessionIdOf(refreshed.RawAccessToken));
    }

    [Fact]
    public async Task PLAN0018_CA133_LogoutConBearerDeSesionReemplazada_DosSesiones_Responde204()
    {
        var owner = await SeedOwnerAsync();
        var s1 = await LoginOwnerAsync(owner);
        var s2 = await LoginOwnerAsync(owner);

        using var http = Bearer(_host, s1.RawAccessToken);
        using var response = await LogoutAsync(http, s2.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True((await RefreshTokenRowAsync(_host.Services, s2.RefreshToken)).IsRevoked);
    }

    // ── CA-134: cuerpo y cabeceras del 401 SESSION_REPLACED ─────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA134_SessionReplaced_DosSesiones_CuerpoIdenticoByteAByteConS2PorContrasenaGoogleUOtraIpYUserAgent_SinDatosDeS2()
    {
        var byPassword = await ReplacedResponsesAsync(async owner => await LoginOwnerAsync(owner));
        var byGoogle = await ReplacedResponsesAsync(async owner =>
        {
            using var response = await GoogleAsync(_host.CreateClient(), _google, owner.GoogleId!, owner);
            return await ReadSessionAsync(response);
        });
        var fromElsewhere = await ReplacedResponsesAsync(LoginFromOtherDeviceAsync);

        var expected = Encoding.UTF8.GetBytes(AuthSessionApi.SessionReplacedBody);
        foreach (var scenario in new[] { byPassword, byGoogle, fromElsewhere })
        {
            foreach (var replaced in new[] { scenario.Access, scenario.Refresh })
            {
                Assert.Equal(StatusCodes.Status401Unauthorized, replaced.Status);
                Assert.Equal(expected, replaced.Body);
                using var json = JsonDocument.Parse(replaced.Body);
                Assert.Equal(["error", "code"], json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());

                // Solo Content-Type (y su longitud): ninguna cabecera propia.
                Assert.All(replaced.Headers.Keys, name =>
                    Assert.Contains(name, new[] { "Content-Type", "Content-Length" }, StringComparer.OrdinalIgnoreCase));
                Assert.Equal("application/json", replaced.Headers["Content-Type"]);

                var leaked = new[]
                {
                    scenario.NewSessionId.ToString("D"), scenario.NewSessionId.ToString("N"),
                    OtherDeviceIp, OtherDeviceUserAgent, scenario.NewSessionStartedAt.ToString("yyyy-MM-dd"),
                };
                var text = Encoding.UTF8.GetString(replaced.Body) + string.Join("\n", replaced.Headers.Values);
                Assert.All(leaked, value => Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase));
            }
        }
        Assert.Equal(byPassword.Access.Headers, byGoogle.Access.Headers);
        Assert.Equal(byPassword.Access.Headers, fromElsewhere.Access.Headers);
        Assert.Equal(byPassword.Refresh.Headers, fromElsewhere.Refresh.Headers);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private const string OtherDeviceIp        = "203.0.113.77";
    private const string OtherDeviceUserAgent = "OtroNavegador/9.9 (Equipo de prueba)";
    private const string SessionCodesSessionReplaced = Zentory.Application.Exceptions.SessionCodes.SessionReplaced;

    private sealed record CapturedResponse(int Status, byte[] Body, IReadOnlyDictionary<string, string> Headers);

    private sealed record ReplacedScenario(
        CapturedResponse Access, CapturedResponse Refresh, Guid NewSessionId, DateTime NewSessionStartedAt);

    // S1 por contraseña, S2 con el inicio pedido; devuelve el 401 del access token y del refresh de S1.
    private async Task<ReplacedScenario> ReplacedResponsesAsync(Func<User, Task<IssuedSession>> startS2)
    {
        var owner = await SeedOwnerAsync(googleId: $"google-{Guid.NewGuid():N}");
        var s1 = await LoginOwnerAsync(owner);
        var startedAt = DateTime.UtcNow;
        var s2 = await startS2(owner);

        using var access = await GetAsync(ClientsEndpoint, s1.RawAccessToken);
        using var refresh = await RefreshAsync(_host.CreateClient(), s1.RefreshToken);
        return new ReplacedScenario(
            await CaptureAsync(access), await CaptureAsync(refresh), SessionIdOf(s2.RawAccessToken), startedAt);
    }

    // Login de S2 desde otra IP y con otro User-Agent: TestServer deja fijar la conexión de la petición.
    private async Task<IssuedSession> LoginFromOtherDeviceAsync(User owner)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { email = owner.Email, password = Password });
        var context = await _host.Server.SendAsync(http =>
        {
            http.Request.Method      = HttpMethods.Post;
            http.Request.Path        = LoginEndpoint;
            http.Request.ContentType = "application/json";
            http.Request.Headers.UserAgent = OtherDeviceUserAgent;
            http.Request.Body        = new MemoryStream(payload);
            http.Connection.RemoteIpAddress = IPAddress.Parse(OtherDeviceIp);
        });

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        using var reader = new StreamReader(context.Response.Body);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(await reader.ReadToEndAsync()),
        };
        return await ReadSessionAsync(response);
    }

    private static async Task<CapturedResponse> CaptureAsync(HttpResponseMessage response)
    {
        var headers = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        return new CapturedResponse((int)response.StatusCode, await response.Content.ReadAsByteArrayAsync(), headers);
    }

    private static async Task AssertUnauthorizedCodeAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var http = Bearer(_host, token);
        return await http.GetAsync(path);
    }

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

    // Owner con su organización sembrados en el host del contador, con un token de TestTokens de ese mismo host: la
    // base del host con el contador se arma con sus propias opciones de EF.
    private static async Task<(User Owner, Organization Organization, string Token)> SeedOwnerInAsync(IServiceProvider services)
    {
        var organization = Organization.Create($"Org contador {Guid.NewGuid():N}", LegalType.Empresa);
        var owner = User.Create($"owner-contador-{Guid.NewGuid():N}@zentory.test", "Prueba", "Contador", OrgRole.Owner,
            organizationId: organization.OrganizationId);
        organization.SetOwner(owner.UserId);
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Zentory.Infrastructure.Persistence.ZentoryDbContext>();
            db.Organizations.Add(organization);
            db.Users.Add(owner);
            db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, owner.UserId, OrgRole.Owner));
            await db.SaveChangesAsync();
        }
        var token = new TestTokens(services).ForUser(owner, organization, OrgRole.Owner, Plan.Free);
        return (owner, organization, token);
    }

    private async Task SeedAsync(params object[] entities)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Zentory.Infrastructure.Persistence.ZentoryDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> LoginRawAsync(User user) => LoginAsync(_host.CreateClient(), user.Email, Password);

    private async Task<IssuedSession> LoginOwnerAsync(User user)
    {
        using var response = await LoginRawAsync(user);
        return await ReadSessionAsync(response);
    }
}
