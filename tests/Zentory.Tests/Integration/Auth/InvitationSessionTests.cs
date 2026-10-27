using System.Net;
using Zentory.API.Middleware;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;
using static Zentory.Tests.Support.InvitationAcceptApi;
using static Zentory.Tests.Support.OneTimeTokenApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T29c sobre el host real: sesión única en POST /auth/invitations/accept (RF-50; RF-90 b, c, d; D-47, C-16).
// La acción es [AllowAnonymous] y el middleware no compara el sid: lo compara AcceptInvitationCommand antes de buscar y
// canjear la invitación.
//
// CA-T29C-1 (parte de CA-129 y CA-130): sin JWT la cuenta nueva deja en current_session_id el sid de su token; con el JWT
// de una sesión reemplazada, 401 SESSION_REPLACED exacto sin consumir la invitación ni crear la membresía; con el JWT de
// la sesión vigente y un 409, 403 o 422, la sesión no cambia y su refresh sigue sirviendo. CA-130 (invitación): el JWT de
// S2 emite un token con el mismo sid. Un Bearer sin sid recibe el 401 sin code del middleware, sin canjear.
//
// ESC-0004 (DEC-0110, parte de CA-59): la cuenta nueva acepta Términos y Privacidad como un registro; sin aceptarlos,
// 422 validation_error sin consumir el token.
public sealed class InvitationSessionTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string Password = "Invitado-Sesion-Unica-2026!";

    public enum SessionKeepingError { EmailMismatch, EmailNotVerified, TokenInvalid }

    // ── CA-T29C-1 / CA-130 ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA130_InvitationsAccept_SinJwtCuentaNueva_CurrentSessionIdEsElSidDeSuToken()
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("sin-jwt"), OrgRole.Member);
        using var http = factory.CreateClient();

        using var response = await AcceptAnonymousAsync(http, invitation.RawToken);

        var session = await ReadSessionAsync(response);
        var sid = SessionIdOf(session.RawAccessToken);
        var user = Assert.IsType<User>(await UserByEmailAsync(factory.Services, invitation.Email));
        Assert.Equal(sid, await CurrentSessionIdAsync(factory.Services, user.UserId));
        Assert.Equal(sid, (await RefreshTokenRowAsync(factory.Services, session.RefreshToken)).SessionId);
    }

    [Fact]
    public async Task PLAN0018_CA130_InvitationsAccept_ConJwtDeS2_EmiteTokenConElMismoSidSinCambiarLaSesionNiRevocarSuRefresh()
    {
        var tenants = await BuildAsync();
        var invited = tenants.A.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invited.UserId);
        // Dos sesiones del mismo usuario (R-25): S1 queda reemplazada y S2 es la vigente.
        await LoginAsync(invited);
        var s2 = await LoginAsync(invited);
        var sid = SessionIdOf(s2.RawAccessToken);
        var invitation = await InviteAsync(tenants.B, invited.Email, OrgRole.Member);

        using var response = await AcceptWithBearerAsync(factory.CreateClient(), s2.RawAccessToken, invitation.RawToken);

        var accepted = await ReadSessionAsync(response);
        Assert.Equal(sid, SessionIdOf(accepted.RawAccessToken));
        Assert.Equal(tenants.B.OrganizationId.ToString(), accepted.ActiveOrgId);
        Assert.Equal(sid, await CurrentSessionIdAsync(factory.Services, invited.UserId));
        Assert.Equal(sid, (await RefreshTokenRowAsync(factory.Services, accepted.RefreshToken)).SessionId);
        Assert.False((await RefreshTokenRowAsync(factory.Services, s2.RefreshToken)).IsRevoked);

        // S2 sigue respondiendo 200 en la otra organización de U, con el token que emitió la aceptación.
        using var http = Bearer(factory, accepted.RawAccessToken);
        using var profile = await http.GetAsync(ProfileEndpoint);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA130_InvitationsAccept_ConJwtDeSesionReemplazada_401SessionReplacedExactoSinConsumirNiCrearMembresia()
    {
        var tenants = await BuildAsync();
        var invited = tenants.A.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invited.UserId);
        var s1 = await LoginAsync(invited);
        var s2 = await LoginAsync(invited);
        var invitation = await InviteAsync(tenants.B, invited.Email, OrgRole.Admin);

        using (var replaced = await AcceptWithBearerAsync(factory.CreateClient(), s1.RawAccessToken, invitation.RawToken))
            await AssertSessionReplacedAsync(replaced);

        Assert.Null((await InvitationRowAsync(factory.Services, invitation.Id)).AcceptedAt);
        Assert.DoesNotContain(await MembershipsOfAsync(factory.Services, invited.UserId),
            m => m.OrganizationId == tenants.B.OrganizationId);
        Assert.Equal(SessionIdOf(s2.RawAccessToken), await CurrentSessionIdAsync(factory.Services, invited.UserId));
        Assert.False((await RefreshTokenRowAsync(factory.Services, s2.RefreshToken)).IsRevoked);

        // Control: la invitación sigue sirviendo con la sesión vigente.
        using (var current = await AcceptWithBearerAsync(factory.CreateClient(), s2.RawAccessToken, invitation.RawToken))
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA130_InvitationsAccept_ConJwtDeSesionReemplazadaYTokenInexistente_ComparaLaSesionPrimero_401SessionReplaced()
    {
        var tenants = await BuildAsync();
        var invited = tenants.A.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invited.UserId);
        var s1 = await LoginAsync(invited);
        await LoginAsync(invited);

        using var response = await AcceptWithBearerAsync(
            factory.CreateClient(), s1.RawAccessToken, OneTimeToken.Issue().RawToken);

        await AssertSessionReplacedAsync(response);
    }

    [Fact]
    public async Task PLAN0018_CA130_InvitationsAccept_ConJwtSinSid_401SinCodigoSinConsumirLaInvitacion()
    {
        var tenants = await BuildAsync();
        var invited = tenants.A.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invited.UserId);
        var invitation = await InviteAsync(tenants.B, invited.Email, OrgRole.Member);
        var withoutSid = TestUserJwt.WithoutSession(invited, tenants.A.OrganizationId.ToString());

        using var response = await AcceptWithBearerAsync(factory.CreateClient(), withoutSid, invitation.RawToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(MembershipValidationMiddleware.MissingSessionBody, await response.Content.ReadAsStringAsync());
        Assert.Null((await InvitationRowAsync(factory.Services, invitation.Id)).AcceptedAt);
        Assert.DoesNotContain(await MembershipsOfAsync(factory.Services, invited.UserId),
            m => m.OrganizationId == tenants.B.OrganizationId);
    }

    [Theory]
    [InlineData(SessionKeepingError.EmailMismatch)]
    [InlineData(SessionKeepingError.EmailNotVerified)]
    [InlineData(SessionKeepingError.TokenInvalid)]
    public async Task PLAN0018_CA130_InvitationsAccept_ConJwtDeLaSesionVigenteYUnError_NoCambiaCurrentSessionIdYElRefreshSigueSirviendo(
        SessionKeepingError error)
    {
        var tenants = await BuildAsync();
        var caller = tenants.A.Owner;
        if (error != SessionKeepingError.EmailNotVerified)
            await MarkEmailVerifiedAsync(factory.Services, caller.UserId);
        var session = await LoginAsync(caller);
        var sid = SessionIdOf(session.RawAccessToken);
        var token = error switch
        {
            SessionKeepingError.EmailMismatch => (await InviteAsync(tenants.B, NewEmail("otro"), OrgRole.Member)).RawToken,
            SessionKeepingError.TokenInvalid  => OneTimeToken.Issue().RawToken,
            _                                 => (await InviteAsync(tenants.B, caller.Email, OrgRole.Member)).RawToken,
        };

        using (var response = await AcceptWithBearerAsync(factory.CreateClient(), session.RawAccessToken, token))
        {
            var expected = error switch
            {
                SessionKeepingError.EmailMismatch => HttpStatusCode.Conflict,
                SessionKeepingError.TokenInvalid  => HttpStatusCode.UnprocessableEntity,
                _                                 => HttpStatusCode.Forbidden,
            };
            Assert.Equal(expected, response.StatusCode);
        }

        Assert.Equal(sid, await CurrentSessionIdAsync(factory.Services, caller.UserId));
        Assert.False((await RefreshTokenRowAsync(factory.Services, session.RefreshToken)).IsRevoked);
        using var refreshed = await RefreshAsync(factory.CreateClient(), session.RefreshToken);
        Assert.Equal(sid, SessionIdOf((await ReadSessionAsync(refreshed)).RawAccessToken));
    }

    [Fact]
    public async Task PLAN0018_CA130_InvitationsAccept_SinJwtConCuentaExistente409_NoTocaLaSesionDeEsaCuenta()
    {
        var tenants = await BuildAsync();
        var existing = tenants.A.Owner;
        var session = await LoginAsync(existing);
        var invitation = await InviteAsync(tenants.B, existing.Email, OrgRole.Member);

        using (var response = await AcceptAnonymousAsync(factory.CreateClient(), invitation.RawToken))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        Assert.Equal(SessionIdOf(session.RawAccessToken), await CurrentSessionIdAsync(factory.Services, existing.UserId));
        using var refreshed = await RefreshAsync(factory.CreateClient(), session.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    // ── ESC-0004 (DEC-0110): términos en la cuenta nueva ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task PLAN0018_CA59_InvitationsAccept_SinJwtSinAceptarTerminos_422ValidationErrorSinConsumirElToken(bool? acceptedTerms)
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("sin-terminos"), OrgRole.Member);
        using var http = factory.CreateClient();

        await AssertValidationErrorAsync(
            await AcceptAnonymousAsync(http, invitation.RawToken, acceptedTerms: acceptedTerms),
            nameof(AcceptInvitationCommand.AcceptedTerms));

        Assert.Null((await InvitationRowAsync(factory.Services, invitation.Id)).AcceptedAt);
        Assert.Null(await UserByEmailAsync(factory.Services, invitation.Email));

        // El token sigue sirviendo al aceptar los términos.
        using var accepted = await AcceptAnonymousAsync(http, invitation.RawToken, acceptedTerms: true);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA59_InvitationsAccept_SinJwtConTerminos_GuardaTermsAcceptedAtYLaVersionVigente()
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("con-terminos"), OrgRole.Member);
        var before = DateTime.UtcNow;

        using (var response = await AcceptAnonymousAsync(factory.CreateClient(), invitation.RawToken, acceptedTerms: true))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = Assert.IsType<User>(await UserByEmailAsync(factory.Services, invitation.Email));
        Assert.Equal(LegalDocuments.CurrentTermsVersion, user.TermsAcceptedVersion);
        Assert.NotNull(user.TermsAcceptedAt);
        Assert.InRange(user.TermsAcceptedAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task PLAN0018_CA59_InvitationsAccept_ConJwt_NoPideTerminosNiLosEscribe()
    {
        var tenants = await BuildAsync();
        var invited = tenants.A.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invited.UserId);
        var invitation = await InviteAsync(tenants.B, invited.Email, OrgRole.Member);

        // Con JWT el cuerpo es { token }: la cuenta ya existe y no se crea otra.
        using var response = await AcceptWithBearerAsync(factory.CreateClient(), tenants.TokenFor(tenants.A), invitation.RawToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = Assert.IsType<User>(await UserByEmailAsync(factory.Services, invited.Email));
        Assert.Equal((invited.TermsAcceptedAt, invited.TermsAcceptedVersion), (user.TermsAcceptedAt, user.TermsAcceptedVersion));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<TenantPair> BuildAsync()
        => new TenantBuilder(factory).ForA(a => a.WithOwnerPassword(Password)).BuildAsync();

    // Invitación del tenant emitida por su owner, como la deja InviteMemberCommand.
    private Task<SeededInvitation> InviteAsync(SeededTenant tenant, string email, string role)
        => SeedInvitationAsync(factory.Services, tenant.OrganizationId, email, role, tenant.Owner.UserId);

    // Login con contraseña: inicia una sesión nueva del usuario (RF-90 b).
    private async Task<IssuedSession> LoginAsync(User user)
    {
        using var http = factory.CreateClient();
        using var response = await AuthSessionApi.LoginAsync(http, user.Email, Password);
        return await ReadSessionAsync(response);
    }
}
