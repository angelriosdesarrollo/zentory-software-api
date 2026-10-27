using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;
using static Zentory.Tests.Support.InvitationAcceptApi;
using static Zentory.Tests.Support.OneTimeTokenApi;

namespace Zentory.Tests.Integration.Invitations;

// PLAN-0018 T29b sobre el host real: POST /auth/invitations/accept (RF-50, RF-60, RF-90 b y c; SR-0018.21, SR-0018.24;
// D-20, D-21; G-7).
//
// CA-59: sin JWT crea la cuenta verificada sin organización propia (o 409 ACCOUNT_EXISTS, o 422 sin consumir el token con
// una contraseña fuera de la política); con JWT exige el email invitado y verificado y crea la membresía en la
// organización de la invitación, nunca en la activa ni en la del cuerpo; token usado o vencido → 422 TOKEN_INVALID; el
// exmiembro recibe el rol de la invitación. CA-T29B-2 (parte de CA-57): dos aceptaciones simultáneas dejan una membresía.
// CA-T29B-1 (parte de CA-60): ActivityLog de la organización con actor, objetivo y rol. Parte de CA-84: un Bearer válido
// da el usuario; uno inválido cuenta como sin JWT, sin 401. RF-90: sin JWT inicia sesión y con JWT conserva la del token.
//
// Las organizaciones del TenantBuilder son empresa en studio; la invitación se siembra como la deja InviteMemberCommand
// (solo el hash del token, email normalizado, 7 días).
public sealed class AcceptInvitationTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string ActiveOrgClaim = "active_org_id";

    private readonly ZentoryApiFactory              _factory;
    private readonly WebApplicationFactory<Program> _host;

    // Operación que compite, para la carrera determinista de CA-57: corre una vez justo antes del primer SaveChanges del
    // canje (el que marca AcceptedAt), completa y confirmada en el host de la factory, sin gancho.
    private Func<Task>? _beforeSave;

    public AcceptInvitationTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp =>
                new HookedUnitOfWork(new UnitOfWork(sp.GetRequiredService<ZentoryDbContext>()), TakeHook));
        }));
    }

    public void Dispose() => _host.Dispose();

    // ── CA-59: sin JWT ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA59_SinJwtSinCuenta_CreaUsuarioVerificadoSinOrganizacionPropiaConElRolInvitado_200ConLaInvitadaActiva()
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("nueva-cuenta"), OrgRole.Admin);
        using var http = _host.CreateClient();

        using var response = await AcceptAnonymousAsync(http, invitation.RawToken);

        var session = await ReadSessionAsync(response);
        Assert.Equal(invitation.OrganizationId.ToString(), session.ActiveOrgId);
        Assert.Equal(invitation.OrganizationId.ToString(), session.Claim(ActiveOrgClaim));
        Assert.Equal(1, session.MembershipCount);

        var user = Assert.IsType<User>(await UserByEmailAsync(_host.Services, invitation.Email));
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.Null(user.OrganizationId);
        Assert.True(BCrypt.Net.BCrypt.Verify(NewAccountPassword, user.PasswordHash));
        var membership = Assert.Single(await MembershipsOfAsync(_host.Services, user.UserId));
        Assert.Equal((invitation.OrganizationId, OrgRole.Admin, (DateTime?)null),
            (membership.OrganizationId, membership.Role, membership.DeletedAt));
        Assert.NotNull((await InvitationRowAsync(_host.Services, invitation.Id)).AcceptedAt);

        // Sin organización propia: no hay ninguna organización con este usuario como owner.
        Assert.False(await OwnsAnyOrganizationAsync(user.UserId));

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA59_SinJwt_LaCuentaNuevaIniciaSesion_SidNuevoYVigente()
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("sesion-nueva"), OrgRole.Member);
        using var http = _host.CreateClient();

        using var response = await AcceptAnonymousAsync(http, invitation.RawToken);

        var session = await ReadSessionAsync(response);
        var user = Assert.IsType<User>(await UserByEmailAsync(_host.Services, invitation.Email));
        Assert.Equal(user.CurrentSessionId, SessionIdOf(session.RawAccessToken));
        Assert.Equal(user.CurrentSessionId, (await RefreshTokenRowAsync(_host.Services, session.RefreshToken)).SessionId);

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    [Theory]
    [MemberData(nameof(PasswordsOutsideThePolicy))]
    public async Task PLAN0018_CA59_SinJwt_ContrasenaDe7CaracteresODeMasDe72Bytes_422SinConsumirElToken(string password)
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("politica"), OrgRole.Member);
        using var http = _host.CreateClient();

        await AssertValidationErrorAsync(await AcceptAnonymousAsync(http, invitation.RawToken, password), "password");

        Assert.Null((await InvitationRowAsync(_host.Services, invitation.Id)).AcceptedAt);
        Assert.Null(await UserByEmailAsync(_host.Services, invitation.Email));

        // El token sigue sirviendo con una contraseña válida.
        using var accepted = await AcceptAnonymousAsync(http, invitation.RawToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    public static TheoryData<string> PasswordsOutsideThePolicy()
    {
        // 7 caracteres; y 37 caracteres que ocupan 73 bytes en UTF-8 (36 "ñ" de 2 bytes y una letra).
        var tooManyBytes = new string('ñ', 36) + "a";
        Assert.Equal(PasswordPolicy.MaxUtf8Bytes + 1, Encoding.UTF8.GetByteCount(tooManyBytes));
        return new TheoryData<string> { new string('a', PasswordPolicy.MinLength - 1), tooManyBytes };
    }

    [Fact]
    public async Task PLAN0018_CA59_SinJwtConCuentaExistenteParaEseEmail_409AccountExistsSinMembresia()
    {
        var tenants = await BuildAsync();
        // El owner de A ya tiene cuenta; la invitación usa su email con otras mayúsculas (se guarda normalizado).
        var existing = tenants.A.Owner;
        var invitation = await InviteAsync(tenants.B, existing.Email.ToUpperInvariant(), OrgRole.Member);
        using var http = _host.CreateClient();

        using var response = await AcceptAnonymousAsync(http, invitation.RawToken);

        await AssertConflictAsync(response, AcceptInvitationErrors.AccountExistsCode);
        Assert.DoesNotContain(await MembershipsOfAsync(_host.Services, existing.UserId),
            m => m.OrganizationId == tenants.B.OrganizationId);
        Assert.Null((await InvitationRowAsync(_host.Services, invitation.Id)).AcceptedAt);

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    // ── CA-59: con JWT ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA59_ConJwtDelInvitadoVerificadoYActivaA_CreaLaMembresiaEnB_YNingunaEnA_AunqueElCuerpoTraigaA()
    {
        var tenants = await BuildAsync(a => a.WithMember(OrgRole.Member));
        // U es member de A (activa en su token) y recibe una invitación de B como admin.
        var invited = tenants.A.Member(OrgRole.Member);
        await MarkEmailVerifiedAsync(_host.Services, invited.UserId);
        var invitation = await InviteAsync(tenants.B, invited.Email, OrgRole.Admin);
        using var http = _host.CreateClient();

        using var response = await AcceptWithBearerAsync(
            http, tenants.TokenFor(tenants.A, invited), invitation.RawToken, organizationId: tenants.A.OrganizationId);

        var session = await ReadSessionAsync(response);
        Assert.Equal(tenants.B.OrganizationId.ToString(), session.ActiveOrgId);
        Assert.Equal(2, session.MembershipCount);

        var memberships = await MembershipsOfAsync(_host.Services, invited.UserId);
        Assert.Equal(2, memberships.Count);
        var inA = Assert.Single(memberships, m => m.OrganizationId == tenants.A.OrganizationId);
        Assert.Equal(OrgRole.Member, inA.Role);
        var inB = Assert.Single(memberships, m => m.OrganizationId == tenants.B.OrganizationId);
        Assert.Equal((OrgRole.Admin, (DateTime?)null), (inB.Role, inB.DeletedAt));

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA59_ConJwt_ConservaLaSesionDelTokenQueLlama_SinIniciarOtraNiRevocarRefresh()
    {
        var tenants = await BuildAsync();
        var invited = tenants.A.Owner;
        await MarkEmailVerifiedAsync(_host.Services, invited.UserId);
        var accessToken = tenants.TokenFor(tenants.A);
        var existingRefresh = await SeedRefreshTokenAsync(_host.Services, invited);
        var sessionBefore = await CurrentSessionIdAsync(_host.Services, invited.UserId);
        var invitation = await InviteAsync(tenants.B, invited.Email, OrgRole.Member);
        using var http = _host.CreateClient();

        using var response = await AcceptWithBearerAsync(http, accessToken, invitation.RawToken);

        var session = await ReadSessionAsync(response);
        Assert.Equal(SessionIdOf(accessToken), SessionIdOf(session.RawAccessToken));
        Assert.Equal(sessionBefore, await CurrentSessionIdAsync(_host.Services, invited.UserId));
        Assert.False((await RefreshTokenRowAsync(_host.Services, existingRefresh)).IsRevoked);
        Assert.Equal(sessionBefore, (await RefreshTokenRowAsync(_host.Services, session.RefreshToken)).SessionId);

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA59_ConJwtDeUnEmailDistinto_409InvitationEmailMismatchSinMembresia()
    {
        var tenants = await BuildAsync();
        var caller = tenants.A.Owner;
        await MarkEmailVerifiedAsync(_host.Services, caller.UserId);
        var invitation = await InviteAsync(tenants.B, NewEmail("otro-correo"), OrgRole.Member);
        using var http = _host.CreateClient();

        using var response = await AcceptWithBearerAsync(http, tenants.TokenFor(tenants.A), invitation.RawToken);

        await AssertConflictAsync(response, AcceptInvitationErrors.EmailMismatchCode);
        Assert.DoesNotContain(await MembershipsOfAsync(_host.Services, caller.UserId),
            m => m.OrganizationId == tenants.B.OrganizationId);
        Assert.Null((await InvitationRowAsync(_host.Services, invitation.Id)).AcceptedAt);

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA59_ConJwtDelEmailCorrectoSinVerificar_403EmailNotVerifiedSinMembresia()
    {
        var tenants = await BuildAsync();
        var caller = tenants.A.Owner;
        var invitation = await InviteAsync(tenants.B, caller.Email, OrgRole.Member);
        using var http = _host.CreateClient();

        using var response = await AcceptWithBearerAsync(http, tenants.TokenFor(tenants.A), invitation.RawToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("EMAIL_NOT_VERIFIED", await CodeOfAsync(response));
        Assert.DoesNotContain(await MembershipsOfAsync(_host.Services, caller.UserId),
            m => m.OrganizationId == tenants.B.OrganizationId);
        Assert.Null((await InvitationRowAsync(_host.Services, invitation.Id)).AcceptedAt);

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    // ── CA-59: token usado, caducado o inexistente ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA59_TokenUsado_422TokenInvalid_SinOtraCuentaNiMembresia()
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("usado"), OrgRole.Member);
        using var http = _host.CreateClient();
        using (var first = await AcceptAnonymousAsync(http, invitation.RawToken))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        await AssertTokenInvalidAsync(await AcceptAnonymousAsync(http, invitation.RawToken));

        // Ni con el JWT del usuario ya creado: la invitación está usada.
        var user = Assert.IsType<User>(await UserByEmailAsync(_host.Services, invitation.Email));
        await AssertTokenInvalidAsync(await AcceptWithBearerAsync(
            http, TestUserJwt.Sign(_host.Services, user, tenants.B.OrganizationId.ToString()), invitation.RawToken));
        Assert.Single(await MembershipsOfAsync(_host.Services, user.UserId));

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA59_TokenCaducado_422TokenInvalid_SinCuentaNiMembresia()
    {
        var tenants = await BuildAsync();
        var issuedAt = DateTime.UtcNow - OrganizationInvitation.Lifetime - TimeSpan.FromMinutes(1);
        var invitation = await InviteAsync(tenants.B, NewEmail("caducado"), OrgRole.Member, issuedAt);
        using var http = _host.CreateClient();

        await AssertTokenInvalidAsync(await AcceptAnonymousAsync(http, invitation.RawToken));

        Assert.Null(await UserByEmailAsync(_host.Services, invitation.Email));
        Assert.Equal(0, await MembershipCountForEmailAsync(_host.Services, invitation.OrganizationId, invitation.Email));
        Assert.Null((await InvitationRowAsync(_host.Services, invitation.Id)).AcceptedAt);

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA59_TokenCaducadoConJwt_422TokenInvalid_SinMembresia()
    {
        var tenants = await BuildAsync();
        var caller = tenants.A.Owner;
        await MarkEmailVerifiedAsync(_host.Services, caller.UserId);
        var issuedAt = DateTime.UtcNow - OrganizationInvitation.Lifetime - TimeSpan.FromMinutes(1);
        var invitation = await InviteAsync(tenants.B, caller.Email, OrgRole.Member, issuedAt);
        using var http = _host.CreateClient();

        await AssertTokenInvalidAsync(await AcceptWithBearerAsync(http, tenants.TokenFor(tenants.A), invitation.RawToken));

        Assert.DoesNotContain(await MembershipsOfAsync(_host.Services, caller.UserId),
            m => m.OrganizationId == tenants.B.OrganizationId);

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA59_TokenInexistente_422TokenInvalidConElMismoCuerpo()
    {
        using var http = _host.CreateClient();

        await AssertTokenInvalidAsync(await AcceptAnonymousAsync(http, OneTimeToken.Issue().RawToken));
    }

    // ── CA-59: exmiembro (D-20) ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA59_ExmiembroConMembresiaBorradaComoAdmin_InvitadoComoMember_QuedaMember()
    {
        var tenants = await BuildAsync(b: b => b.WithMember(OrgRole.Admin));
        var former = tenants.B.Member(OrgRole.Admin);
        await RemoveMembershipAsync(_host.Services, tenants.B.OrganizationId, former.UserId);
        await MarkEmailVerifiedAsync(_host.Services, former.UserId);
        var invitation = await InviteAsync(tenants.B, former.Email, OrgRole.Member);
        using var http = _host.CreateClient();

        // Sin membresías vigentes, su token no tiene organización activa.
        using var response = await AcceptWithBearerAsync(http, TestUserJwt.Sign(_host.Services, former), invitation.RawToken);

        var session = await ReadSessionAsync(response);
        Assert.Equal(tenants.B.OrganizationId.ToString(), session.ActiveOrgId);
        var memberships = await MembershipsOfAsync(_host.Services, former.UserId);
        Assert.Equal(2, memberships.Count);
        Assert.Single(memberships, m => m.Role == OrgRole.Admin && m.DeletedAt != null);
        Assert.Single(memberships, m => m.Role == OrgRole.Member && m.DeletedAt == null);
        Assert.Equal(OrgRole.Member, await ActiveOrgRoleOfAsync(response));

        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    // ── CA-T29B-2 (parte de CA-57): canje atómico ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA57_DosAceptacionesSimultaneasSinJwt_ExactamenteUnaCuentaYUnaMembresia()
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("carrera-sin-jwt"), OrgRole.Member);
        HttpStatusCode? competitor = null;

        // La otra aceptación confirma completa entre la lectura de la invitación y el canje de esta.
        _beforeSave = async () =>
        {
            using var other = _factory.CreateClient();
            using var response = await AcceptAnonymousAsync(other, invitation.RawToken);
            competitor = response.StatusCode;
        };
        using var http = _host.CreateClient();

        await AssertTokenInvalidAsync(await AcceptAnonymousAsync(http, invitation.RawToken));

        Assert.Equal(HttpStatusCode.OK, competitor);
        var user = Assert.IsType<User>(await UserByEmailAsync(_host.Services, invitation.Email));
        Assert.Single(await MembershipsOfAsync(_host.Services, user.UserId));
        Assert.Equal(1, await MembershipCountForEmailAsync(_host.Services, invitation.OrganizationId, invitation.Email));
    }

    [Fact]
    public async Task PLAN0018_CA57_DosAceptacionesSimultaneasConJwt_ExactamenteUnaMembresia()
    {
        var tenants = await BuildAsync();
        var invited = tenants.A.Owner;
        await MarkEmailVerifiedAsync(_host.Services, invited.UserId);
        var accessToken = tenants.TokenFor(tenants.A);
        var invitation = await InviteAsync(tenants.B, invited.Email, OrgRole.Member);
        HttpStatusCode? competitor = null;

        _beforeSave = async () =>
        {
            using var other = _factory.CreateClient();
            using var response = await AcceptWithBearerAsync(other, accessToken, invitation.RawToken);
            competitor = response.StatusCode;
        };
        using var http = _host.CreateClient();

        await AssertTokenInvalidAsync(await AcceptWithBearerAsync(http, accessToken, invitation.RawToken));

        Assert.Equal(HttpStatusCode.OK, competitor);
        Assert.Single(await MembershipsOfAsync(_host.Services, invited.UserId),
            m => m.OrganizationId == tenants.B.OrganizationId);
    }

    [Fact]
    public async Task PLAN0018_CA57_DosAceptacionesALaVez_SinGancho_UnaSolaGanaYQuedaUnaMembresia()
    {
        const int Repetitions = 5;
        for (var repetition = 0; repetition < Repetitions; repetition++)
        {
            var tenants = await BuildAsync();
            var invitation = await InviteAsync(tenants.B, NewEmail("carrera-real"), OrgRole.Member);
            using var first = _factory.CreateClient();
            using var second = _factory.CreateClient();

            var responses = await Task.WhenAll(
                AcceptAnonymousAsync(first, invitation.RawToken), AcceptAnonymousAsync(second, invitation.RawToken));

            // El que pierde ve la invitación ya canjeada (422) o, si la leyó antes, la cuenta ya creada (409).
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict);
            Assert.Equal(1, await MembershipCountForEmailAsync(_host.Services, invitation.OrganizationId, invitation.Email));
            foreach (var response in responses) response.Dispose();
        }
    }

    // ── CA-T29B-1 (parte de CA-60): ActivityLog ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA60_Aceptacion_DejaUnaFilaEnActivityLogDeLaOrganizacionInvitadaConActorObjetivoYRol()
    {
        var tenants = await BuildAsync();
        var invited = tenants.A.Owner;
        await MarkEmailVerifiedAsync(_host.Services, invited.UserId);
        var invitation = await InviteAsync(tenants.B, invited.Email, OrgRole.Admin);
        using var http = _host.CreateClient();

        using (var response = await AcceptWithBearerAsync(http, tenants.TokenFor(tenants.A), invitation.RawToken))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = Assert.Single(await AcceptanceLogsAsync(tenants.B.OrganizationId));
        Assert.Equal((invitation.Id, invited.UserId), (row.EntityId, row.UserId));
        using var metadata = JsonDocument.Parse(row.Metadata!);
        Assert.Equal(invited.UserId, metadata.RootElement.GetProperty("actorUserId").GetGuid());
        Assert.Equal(invited.UserId, metadata.RootElement.GetProperty("targetUserId").GetGuid());
        Assert.Equal(invitation.Email, metadata.RootElement.GetProperty("targetEmail").GetString());
        Assert.Equal(OrgRole.Admin, metadata.RootElement.GetProperty("role").GetString());
        Assert.DoesNotContain(invitation.RawToken, row.Metadata!, StringComparison.Ordinal);
        Assert.DoesNotContain(invitation.Invitation.TokenHash, row.Metadata!, StringComparison.Ordinal);

        // La activa del token (A) no recibe la fila.
        Assert.Empty(await AcceptanceLogsAsync(tenants.A.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA60_AceptacionSinJwt_ActorYObjetivoSonLaCuentaNueva()
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("bitacora"), OrgRole.Member);
        using var http = _host.CreateClient();

        using (var response = await AcceptAnonymousAsync(http, invitation.RawToken))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = Assert.IsType<User>(await UserByEmailAsync(_host.Services, invitation.Email));
        var row = Assert.Single(await AcceptanceLogsAsync(tenants.B.OrganizationId));
        Assert.Equal(user.UserId, row.UserId);
        using var metadata = JsonDocument.Parse(row.Metadata!);
        Assert.Equal(user.UserId, metadata.RootElement.GetProperty("actorUserId").GetGuid());
        Assert.Equal(OrgRole.Member, metadata.RootElement.GetProperty("role").GetString());
    }

    // ── Parte de CA-84: el Bearer da el usuario solo si es válido ───────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA84_InvitationsAccept_ConBearerInvalido_NoResponde401YCuentaComoSinJwt()
    {
        var tenants = await BuildAsync();
        var invitation = await InviteAsync(tenants.B, NewEmail("bearer-invalido"), OrgRole.Member);
        using var http = _host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "no-es-un-jwt");

        using var response = await AcceptAnonymousAsync(http, invitation.RawToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await UserByEmailAsync(_host.Services, invitation.Email));
    }

    [Fact]
    public async Task PLAN0018_CA84_InvitationsAccept_ConBearerValido_UsaElUsuarioDelTokenYNoPideCuentaNueva()
    {
        var tenants = await BuildAsync();
        var invited = tenants.A.Owner;
        await MarkEmailVerifiedAsync(_host.Services, invited.UserId);
        var invitation = await InviteAsync(tenants.B, invited.Email, OrgRole.Member);
        using var http = _host.CreateClient();

        // Con JWT el cuerpo es solo { token }: sin contraseña ni nombres no hay 422, porque no se crea cuenta.
        using var response = await AcceptWithBearerAsync(http, tenants.TokenFor(tenants.A), invitation.RawToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await MembershipsOfAsync(_host.Services, invited.UserId),
            m => m.OrganizationId == tenants.B.OrganizationId);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private Func<Task>? TakeHook() => Interlocked.Exchange(ref _beforeSave, null);

    private Task<TenantPair> BuildAsync(Action<TenantOptions>? a = null, Action<TenantOptions>? b = null)
        => new TenantBuilder(_factory).ForA(a ?? (_ => { })).ForB(b ?? (_ => { })).BuildAsync();

    // Invitación de B emitida por su owner, como la deja InviteMemberCommand.
    private Task<SeededInvitation> InviteAsync(SeededTenant tenant, string email, string role, DateTime? issuedAt = null)
        => SeedInvitationAsync(_host.Services, tenant.OrganizationId, email, role, tenant.Owner.UserId, issuedAt);

    private async Task<bool> OwnsAnyOrganizationAsync(Guid userId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.OwnerId == userId);
    }

    private async Task<List<ActivityLog>> AcceptanceLogsAsync(Guid organizationId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.ActivityLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.OrganizationId == organizationId
                     && l.EntityType == AcceptInvitationCommandHandler.EntityType
                     && l.Action == AcceptInvitationCommandHandler.LogAction)
            .ToListAsync();
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }

    private static async Task<string?> ActiveOrgRoleOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("user").GetProperty("activeOrgRole").GetString();
    }
}
