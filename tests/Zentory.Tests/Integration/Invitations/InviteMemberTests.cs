using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zentory.API.Authorization;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding.Demo;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.MemberManagementApi;
using static Zentory.Tests.Support.OneTimeTokenApi;

namespace Zentory.Tests.Integration.Invitations;

// PLAN-0018 T29 sobre el host real: POST /organization/members/invite (RF-49, RF-25, RF-51, RF-75 d; SR-0018.10,
// SR-0018.21, SR-0018.22, SR-0018.24; D-19, D-20, D-21). La aceptación es de T29b.
//
// CA-53: ManageMembers también en invite (freelance 403 ACCESS_DENIED, empresa + free 403 PLAN_REQUIRED, pro y studio 202).
// CA-T29-1 (parte de CA-57): token de 32 bytes, solo su SHA-256 en la base, 7 días, URL de App:BaseUrl, email normalizado,
// ALREADY_MEMBER (también uno mismo) e INVITATION_PENDING; y el rol fuera de OrgRole.Assignable → 422 (SR-0018.21).
// CA-58: max_org_members = vigentes + pendientes de la organización activa. CA-80: el token no queda en entidades, Hangfire
// ni logs. CA-93: valores codificados en el HTML real y techos de 20/día por organización y 3/día por email → 429.
// CA-T29-2 (parte de CA-60): ActivityLog con actor, objetivo y rol. CA-88 y CA-99: la Consultora del escenario demo y las
// invitaciones por encima de max_org_members tras una bajada.
//
// El catálogo de referencia (PlanLimits) y los escenarios demo se siembran una vez por base, antes de cualquier
// TenantBuilder, como en PlanLimitsTests.
public sealed class InviteMemberTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string InviteEndpoint  = MembersEndpoint + "/invite";
    private const string ClientsEndpoint  = "/api/v1/clients";
    private const string InvoicesEndpoint = "/api/v1/invoices";
    private const string RegisterEndpoint = "/api/v1/auth/register";

    private const string InviteKind = nameof(FakeEmailService.SendOrganizationInvitationEmailAsync);

    // Valores de CA-93 (SR-0018.22).
    private const string HostileOrganizationName = "<a href=\"https://x\">y</a>";
    private const string HostilePersonName       = "<script>";

    private const string ForgedHost   = "evil-host.example";
    private const string ForgedOrigin = "https://evil-origin.example";

    private const int ProMaxOrgMembers = 5;

    // Techos de invitaciones de la spec (RF-49 d; SR-0018.22), escritos aquí y no leídos de la configuración: un cambio
    // del valor por defecto rompe CA-93.
    private const int SpecInvitesPerOrganizationPerDay = 20;
    private const int SpecInvitesPerEmailPerDay        = 3;

    private FakeEmailService Emails => factory.Services.GetRequiredService<FakeEmailService>();

    // ── CA-53: ManageMembers ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Plan.Free, OrgRole.Owner)]
    [InlineData(Plan.Free, OrgRole.Admin)]
    [InlineData(Plan.Pro, OrgRole.Owner)]
    [InlineData(Plan.Pro, OrgRole.Admin)]
    [InlineData(Plan.Studio, OrgRole.Owner)]
    [InlineData(Plan.Studio, OrgRole.Admin)]
    public async Task PLAN0018_CA53_Invite_EnFreelance_OwnerYAdmin_Reciben403AccessDenied_SinInvitacion(string plan, string actorRole)
    {
        var tenants = await BuildAsync(a => a.WithLegalType(LegalType.Freelance).WithPlan(plan).WithMember(OrgRole.Admin));
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await InviteAsync(http, NewEmail("tipo-freelance"), OrgRole.Member);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Empty(await InvitationsAsync(tenants.A.OrganizationId));
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA53_Invite_EnEmpresaFree_OwnerYAdmin_Reciben403PlanRequiredPro_SinInvitacion(string actorRole)
    {
        var tenants = await BuildAsync(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Free).WithMember(OrgRole.Admin));
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await InviteAsync(http, NewEmail("plan-gratis"), OrgRole.Member);

        await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Pro);
        Assert.Empty(await InvitationsAsync(tenants.A.OrganizationId));
    }

    [Theory]
    [InlineData(Plan.Pro, OrgRole.Owner)]
    [InlineData(Plan.Pro, OrgRole.Admin)]
    [InlineData(Plan.Studio, OrgRole.Owner)]
    [InlineData(Plan.Studio, OrgRole.Admin)]
    public async Task PLAN0018_CA53_Invite_EnEmpresaProYStudio_OwnerYAdmin_Responden202(string plan, string actorRole)
    {
        var tenants = await BuildAsync(a => a.WithLegalType(LegalType.Empresa).WithPlan(plan).WithMember(OrgRole.Admin));
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await InviteAsync(http, NewEmail("pago"), OrgRole.Admin);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(await InvitationsAsync(tenants.A.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA53_Invite_MemberEnEmpresaPro_Recibe403AccessDenied()
    {
        var tenants = await BuildAsync(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro).WithMember(OrgRole.Member));
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Member));

        using var response = await InviteAsync(http, NewEmail("rol-miembro"), OrgRole.Member);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Empty(await InvitationsAsync(tenants.A.OrganizationId));
    }

    // ── CA-T29-1 (parte de CA-57) ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA57_Invite_TokenDe32Bytes_LaBaseSoloGuardaSuSha256_YCaducaALos7Dias()
    {
        var tenants = await ProOrganizationAsync();
        var email = NewEmail("token");
        using var http = tenants.ClientFor(tenants.A);

        using var response = await InviteAsync(http, email, OrgRole.Member);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var invitationId = body.RootElement.GetProperty("invitationId").GetGuid();
        var expiresAt = body.RootElement.GetProperty("expiresAt").GetDateTime().ToUniversalTime();

        var token = TokenOf(await WaitForEmailAsync(Emails, InviteKind, email));
        Assert.Equal(OneTimeToken.ByteLength, Base64UrlDecode(token).Length);

        var row = Assert.Single(await InvitationsAsync(tenants.A.OrganizationId));
        Assert.Equal(invitationId, row.Id);
        Assert.Equal(OneTimeToken.HashOf(token), row.TokenHash);
        Assert.NotEqual(token, row.TokenHash);
        Assert.Equal(TimeSpan.FromDays(7), row.ExpiresAt - row.CreatedAt);
        Assert.Equal(row.ExpiresAt, expiresAt);
        Assert.Equal(OrgRole.Member, row.Role);
        Assert.Equal(tenants.A.Owner.UserId, row.InvitedBy);
        Assert.Null(row.AcceptedAt);
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task PLAN0018_CA57_Invite_ConHostYOriginManipulados_ElCorreoUsaAppBaseUrlInviteAcceptSinElHostManipulado()
    {
        var tenants = await ProOrganizationAsync();
        var email = NewEmail("host");
        // La base del cliente fija el Host de la petición en el host manipulado; Origin y X-Forwarded-Host también.
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{ForgedHost}") });
        using var request = new HttpRequestMessage(HttpMethod.Post, InviteEndpoint)
        {
            Content = JsonContent.Create(new { email, roleId = OrgRole.Member }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenants.TokenFor(tenants.A));
        request.Headers.Host = ForgedHost;
        request.Headers.Add("Origin", ForgedOrigin);
        request.Headers.Add("X-Forwarded-Host", ForgedHost);

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var sent = await WaitForEmailAsync(Emails, InviteKind, email);
        Assert.StartsWith(TestAppUrlSettings.BaseUrl + InviteMemberCommandHandler.AcceptPath + "?token=", sent.Url);
        Assert.DoesNotContain(ForgedHost, sent.Url!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new Uri(ForgedOrigin).Host, sent.Url!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PLAN0018_CA57_Invite_EmailConEspaciosYMayusculas_GuardaElEmailNormalizado_YElCorreoVaAEse()
    {
        var tenants = await ProOrganizationAsync();
        var local = $"Ana-{Guid.NewGuid():N}";
        using var http = tenants.ClientFor(tenants.A);

        using var response = await InviteAsync(http, $" {local}@X.com ", OrgRole.Member);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var normalized = $"{local.ToLowerInvariant()}@x.com";
        Assert.Equal(normalized, Assert.Single(await InvitationsAsync(tenants.A.OrganizationId)).Email);
        await WaitForEmailAsync(Emails, InviteKind, normalized);
    }

    [Fact]
    public async Task PLAN0018_CA57_Invite_MiembroVigenteConOtrasMayusculas_Responde409AlreadyMember_SinInvitacionNiCorreo()
    {
        var tenants = await ProOrganizationAsync();
        var member = tenants.A.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A);

        using var response = await InviteAsync(http, $"  {member.Email.ToUpperInvariant()} ", OrgRole.Admin);

        await AssertConflictAsync(response, InviteMemberCommandHandler.AlreadyMemberCode);
        Assert.Empty(await InvitationsAsync(tenants.A.OrganizationId));
        await AssertNoInvitationEmailAsync(member.Email);
    }

    [Fact]
    public async Task PLAN0018_CA57_Invite_DosVecesAlMismoEmailConInvitacionVigente_Responde409InvitationPending()
    {
        var tenants = await ProOrganizationAsync();
        var email = NewEmail("dos-veces");
        using var http = tenants.ClientFor(tenants.A);

        using (var first = await InviteAsync(http, email, OrgRole.Member))
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await WaitForEmailAsync(Emails, InviteKind, email);

        using var second = await InviteAsync(http, email.ToUpperInvariant(), OrgRole.Admin);

        await AssertConflictAsync(second, InviteMemberCommandHandler.InvitationPendingCode);
        Assert.Single(await InvitationsAsync(tenants.A.OrganizationId));
        await AssertNoInvitationEmailAsync(email, alreadySent: 1);
    }

    [Fact]
    public async Task PLAN0018_CA57_AdminSeInvitaASuPropioEmail_Recibe409AlreadyMember_SinInvitacionNiCorreo_YSuRolNoCambia()
    {
        var tenants = await ProOrganizationAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A, admin);

        using var response = await InviteAsync(http, admin.Email.ToUpperInvariant(), OrgRole.Member);

        await AssertConflictAsync(response, InviteMemberCommandHandler.AlreadyMemberCode);
        Assert.Empty(await InvitationsAsync(tenants.A.OrganizationId));
        Assert.Equal(OrgRole.Admin, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, admin.UserId))!.Role);
        await AssertNoInvitationEmailAsync(admin.Email);
    }

    public static TheoryData<string?> NotAssignableRoles => new()
    {
        OrgRole.Owner,
        OrgRole.Admin.ToUpperInvariant(),
        "superadmin",
        "",
        null,
    };

    [Theory]
    [MemberData(nameof(NotAssignableRoles))]
    public async Task PLAN0018_CA57_Invite_RoleIdFueraDeAssignable_IncluidoOwner_Responde422SinInvitacionNiCorreo(string? roleId)
    {
        var tenants = await ProOrganizationAsync();
        var email = NewEmail("rol");
        using var http = tenants.ClientFor(tenants.A);

        await AssertValidationErrorAsync(await InviteAsync(http, email, roleId), InviteMemberCommandHandler.RoleIdField);

        Assert.Empty(await InvitationsAsync(tenants.A.OrganizationId));
        await AssertNoInvitationEmailAsync(email);
    }

    // ── CA-58: cupo de max_org_members ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA58_ProConCuatroVigentesYUnaPendiente_OwnerOAdminInvita_422QuotaExceededMaxOrgMembers5(string actorRole)
    {
        var tenants = await QuotaOrganizationAsync(Plan.Pro);
        await SeedInvitationAsync(tenants.A, DateTime.UtcNow);
        var email = NewEmail("cupo");
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await InviteAsync(http, email, OrgRole.Member);

        await AssertMemberQuotaExceededAsync(response, ProMaxOrgMembers, used: ProMaxOrgMembers);
        Assert.Single(await InvitationsAsync(tenants.A.OrganizationId));
        await AssertNoInvitationEmailAsync(email);
    }

    [Fact]
    public async Task PLAN0018_CA58_ConLaInvitacionPendienteCaducada_LaInvitacionSeCrea()
    {
        var tenants = await QuotaOrganizationAsync(Plan.Pro);
        await SeedInvitationAsync(tenants.A, DateTime.UtcNow - OrganizationInvitation.Lifetime - TimeSpan.FromMinutes(1));
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, OrgRole.Admin));

        using var response = await InviteAsync(http, NewEmail("caducada"), OrgRole.Member);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(2, (await InvitationsAsync(tenants.A.OrganizationId)).Count);
    }

    [Fact]
    public async Task PLAN0018_CA58_InvitacionesPendientesDeOtraOrganizacionB_NoCuentanParaA()
    {
        var tenants = await QuotaOrganizationAsync(Plan.Pro, bPlan: Plan.Pro);
        for (var i = 0; i < ProMaxOrgMembers; i++)
            await SeedInvitationAsync(tenants.B, DateTime.UtcNow);
        using var http = tenants.ClientFor(tenants.A);

        using var response = await InviteAsync(http, NewEmail("solo-a"), OrgRole.Member);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(await InvitationsAsync(tenants.A.OrganizationId));
        Assert.Equal(ProMaxOrgMembers, (await InvitationsAsync(tenants.B.OrganizationId)).Count);
    }

    [Fact]
    public async Task PLAN0018_CA58_EnStudio_NoHayLimiteDeMiembros()
    {
        var tenants = await QuotaOrganizationAsync(Plan.Studio);
        for (var i = 0; i < ProMaxOrgMembers; i++)
            await SeedInvitationAsync(tenants.A, DateTime.UtcNow);
        using var http = tenants.ClientFor(tenants.A);

        using var response = await InviteAsync(http, NewEmail("sin-limite"), OrgRole.Member);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(ProMaxOrgMembers + 1, (await InvitationsAsync(tenants.A.OrganizationId)).Count);
    }

    // ── CA-80 (invite) ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA80_Invite_ElTokenYSuUrlNoAparecenEnEntidadesNiEnHangfireNiEnLogs()
    {
        var logs = new CapturingLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs)));
        var tenants = await ProOrganizationAsync();
        var email = NewEmail("fuga");
        using var http = ClientFor(host, tenants.TokenFor(tenants.A));

        using (var response = await InviteAsync(http, email, OrgRole.Member))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var sent  = await WaitForEmailAsync(host.Services.GetRequiredService<FakeEmailService>(), InviteKind, email);
        var url   = sent.Url!;
        var token = TokenOf(sent);
        Assert.Contains(token, url, StringComparison.Ordinal);

        // Control: el hash sí está guardado, así que el recorrido de entidades ve la fila de la invitación.
        var entityStrings = await EntityStringsAsync(host.Services);
        Assert.Contains(OneTimeToken.HashOf(token), entityStrings);
        Assert.DoesNotContain(entityStrings, value => value.Contains(token, StringComparison.Ordinal));

        Assert.DoesNotContain(HangfireStrings(host.Services.GetRequiredService<JobStorage>()),
            value => value.Contains(token, StringComparison.Ordinal));

        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry =>
            entry.Message.Contains(token, StringComparison.Ordinal) || entry.Message.Contains(url, StringComparison.Ordinal));
    }

    // ── CA-93: codificación y techos de envío ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA93_Invite_OrganizacionEInvitadorConHtml_ElHtmlDelCorreoLosEscapaYNoTraeLaEtiqueta()
    {
        var resend = CapturingResend.Create();
        using var host = WithRealTemplates(resend);
        var tenants = await ProOrganizationAsync();
        await RenameAsync(tenants.A, HostileOrganizationName, HostilePersonName);
        using var http = ClientFor(host, tenants.TokenFor(tenants.A));

        using (var response = await InviteAsync(http, NewEmail("html"), OrgRole.Admin, name: HostilePersonName))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var message = await WaitForMessageAsync(resend, "invit");
        Assert.Contains("&lt;a href=&quot;https://x&quot;&gt;y&lt;/a&gt;", message.HtmlBody);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
        Assert.DoesNotContain(HostileOrganizationName, message.HtmlBody);
        Assert.DoesNotContain(HostilePersonName, message.HtmlBody);
        Assert.Contains("href=\"" + TestAppUrlSettings.BaseUrl + InviteMemberCommandHandler.AcceptPath + "?token=", message.HtmlBody);
        Assert.DoesNotContain("<", message.Subject);
    }

    [Fact]
    public async Task PLAN0018_CA93_Verificacion_UsuarioConNombreScript_ElHtmlDelCorreoLoEscapaYNoTraeLaEtiqueta()
    {
        await EnsureSeededAsync();
        var resend = CapturingResend.Create();
        using var host = WithRealTemplates(resend);
        using var http = host.CreateClient();

        using (var response = await http.PostAsJsonAsync(RegisterEndpoint, new
               {
                   email         = NewEmail("verifica-html"),
                   password      = "Registro-Html-2026!",
                   firstName     = HostilePersonName,
                   lastName      = "Prueba",
                   orgName       = $"Org html {Guid.NewGuid():N}",
                   legalType     = LegalType.Freelance,
                   acceptedTerms = true,
               }))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var message = await WaitForMessageAsync(resend, "Confirma");
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
        Assert.DoesNotContain(HostilePersonName, message.HtmlBody);
    }

    [Fact]
    public void PLAN0018_CA93_TechosPorDefecto_SonLosDeLaSpec_20PorOrganizacionY3PorEmailAlDia()
    {
        var perOrganization = AuthRateLimitApiFactory.DefaultRule(
            AuthRateLimitPolicies.OrganizationInvite, AuthRateLimitPartition.Organization);
        Assert.Equal(SpecInvitesPerOrganizationPerDay, perOrganization.PermitLimit);
        Assert.Equal(TimeSpan.FromDays(1), perOrganization.Window);

        var perEmail = AuthRateLimitApiFactory.DefaultRule(
            AuthRateLimitPolicies.OrganizationInviteEmail, AuthRateLimitPartition.Email);
        Assert.Equal(SpecInvitesPerEmailPerDay, perEmail.PermitLimit);
        Assert.Equal(TimeSpan.FromDays(1), perEmail.Window);
    }

    [Fact]
    public async Task PLAN0018_CA93_Invitacion21DelDiaDeUnaOrganizacion_Responde429RateLimited_SinInvitacionNiCorreo()
    {
        using var host = AuthRateLimitApiFactory.WithDefaultAuthRateLimits(factory);
        var tenants = await BuildAsync(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Studio));
        using var http = ClientFor(host, tenants.TokenFor(tenants.A));

        // Las 20 primeras del día pasan (techo de la spec, no el valor configurado)...
        for (var i = 1; i <= SpecInvitesPerOrganizationPerDay; i++)
        {
            using var accepted = await InviteAsync(http, NewEmail($"dia-{i}"), OrgRole.Member);
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, $"Invitación {i}: {(int)accepted.StatusCode}");
        }

        // ...y la 21 responde 429.
        var rejectedEmail = NewEmail("dia-21");
        await AssertRateLimitedAsync(await InviteAsync(http, rejectedEmail, OrgRole.Member));

        Assert.Equal(SpecInvitesPerOrganizationPerDay, (await InvitationsAsync(tenants.A.OrganizationId)).Count);
        Assert.DoesNotContain(await InvitationsAsync(tenants.A.OrganizationId), i => i.Email == rejectedEmail);
        await AssertNoInvitationEmailAsync(rejectedEmail, host: host);

        // El techo es por organización: B sigue invitando.
        using var fromB = ClientFor(host, tenants.TokenFor(tenants.B));
        using var other = await InviteAsync(fromB, NewEmail("dia-b"), OrgRole.Member);
        Assert.Equal(HttpStatusCode.Accepted, other.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA93_CuartaInvitacionDelDiaAlMismoEmail_DesdeCualquierOrganizacion_Responde429SinCorreo()
    {
        using var host = AuthRateLimitApiFactory.WithDefaultAuthRateLimits(factory);
        var organizations = await StudioOrganizationsAsync(SpecInvitesPerEmailPerDay + 1);
        var email = NewEmail("buzon");

        // Las 3 primeras al mismo buzón pasan, cada una desde otra organización...
        for (var i = 0; i < SpecInvitesPerEmailPerDay; i++)
        {
            var (pair, tenant) = organizations[i];
            using var http = ClientFor(host, pair.TokenFor(tenant));
            // Mismo buzón con otra forma: la clave del techo es el email normalizado.
            using var accepted = await InviteAsync(http, i % 2 == 0 ? email : $" {email.ToUpperInvariant()} ", OrgRole.Member);
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, $"Invitación {i + 1}: {(int)accepted.StatusCode}");
        }
        await WaitForEmailAsync(host.Services.GetRequiredService<FakeEmailService>(), InviteKind, email,
            expected: SpecInvitesPerEmailPerDay);

        // ...y la 4.ª responde 429 sin invitación ni correo.
        var (lastPair, lastTenant) = organizations[SpecInvitesPerEmailPerDay];
        using var last = ClientFor(host, lastPair.TokenFor(lastTenant));
        await AssertRateLimitedAsync(await InviteAsync(last, email, OrgRole.Member));

        Assert.Empty(await InvitationsAsync(lastTenant.OrganizationId));
        await AssertNoInvitationEmailAsync(email, alreadySent: SpecInvitesPerEmailPerDay, host: host);
    }

    [Fact]
    public async Task PLAN0018_CA93_IntentosRechazadosDesdeOtraOrganizacion_NoGastanElCupoDelEmail_LaCuartaInvitacionRealDa429()
    {
        // Decisión del orquestador (DoS del buzón): el cupo por email solo lo gasta una invitación que pasó todas las
        // validaciones. Una organización X que falla 3 veces contra la víctima no le impide a nadie invitarla.
        using var host = AuthRateLimitApiFactory.WithDefaultAuthRateLimits(factory);
        var organizations = await StudioOrganizationsAsync(SpecInvitesPerEmailPerDay + 2);
        var victim = NewEmail("victima");
        var (attackerPair, attacker) = organizations[0];
        using (var fromX = ClientFor(host, attackerPair.TokenFor(attacker)))
        {
            for (var i = 0; i < SpecInvitesPerEmailPerDay; i++)
                await AssertValidationErrorAsync(await InviteAsync(fromX, victim, OrgRole.Owner), InviteMemberCommandHandler.RoleIdField);
            // Un 409 ALREADY_MEMBER (el owner de X se invita a sí mismo) tampoco gasta el cupo de su buzón.
            for (var i = 0; i < SpecInvitesPerEmailPerDay; i++)
                await AssertConflictAsync(await InviteAsync(fromX, attacker.Owner.Email, OrgRole.Member),
                    InviteMemberCommandHandler.AlreadyMemberCode);
        }

        // Las 3 invitaciones reales a la víctima pasan...
        for (var i = 1; i <= SpecInvitesPerEmailPerDay; i++)
        {
            var (pair, tenant) = organizations[i];
            using var http = ClientFor(host, pair.TokenFor(tenant));
            using var accepted = await InviteAsync(http, victim, OrgRole.Member);
            Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, $"Invitación real {i}: {(int)accepted.StatusCode}");
        }

        // ...y la 4.ª real responde 429.
        var (lastPair, lastTenant) = organizations[SpecInvitesPerEmailPerDay + 1];
        using (var last = ClientFor(host, lastPair.TokenFor(lastTenant)))
            await AssertRateLimitedAsync(await InviteAsync(last, victim, OrgRole.Member));

        Assert.Empty(await InvitationsAsync(attacker.OrganizationId));
        Assert.Empty(await InvitationsAsync(lastTenant.OrganizationId));
        await AssertNoInvitationEmailAsync(victim, alreadySent: SpecInvitesPerEmailPerDay, host: host);

        // El owner de X no gastó el cupo de su buzón con los 409: otra organización lo invita.
        var (ownerPair, ownerInviter) = organizations[1];
        using var toOwner = ClientFor(host, ownerPair.TokenFor(ownerInviter));
        using var invitedOwner = await InviteAsync(toOwner, attacker.Owner.Email, OrgRole.Member);
        Assert.Equal(HttpStatusCode.Accepted, invitedOwner.StatusCode);
    }

    // ── CA-T29-2 (parte de CA-60): ActivityLog ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA60_Invite_DejaFilaEnActivityLogDeLaOrganizacionConActorObjetivoYRol(string actorRole)
    {
        var tenants = await ProOrganizationAsync();
        var actor = Actor(tenants.A, actorRole);
        var email = NewEmail("bitacora");
        using var http = tenants.ClientFor(tenants.A, actor);

        using (var response = await InviteAsync(http, email, OrgRole.Admin))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var invitation = Assert.Single(await InvitationsAsync(tenants.A.OrganizationId));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var log = await db.ActivityLogs.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(l => l.OrganizationId == tenants.A.OrganizationId
                && l.EntityType == InviteMemberCommandHandler.EntityType);

        Assert.Equal(invitation.Id, log.EntityId);
        Assert.Equal(actor.UserId, log.UserId);
        Assert.Equal(InviteMemberCommandHandler.LogAction, log.Action);
        using var metadata = JsonDocument.Parse(log.Metadata!);
        Assert.Equal(actor.UserId, metadata.RootElement.GetProperty("actorUserId").GetGuid());
        Assert.Equal(email, metadata.RootElement.GetProperty("targetEmail").GetString());
        Assert.Equal(OrgRole.Admin, metadata.RootElement.GetProperty("role").GetString());
    }

    // ── CA-88: la Consultora ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA88_OwnerDeLaConsultoraFree_AlInvitar_NoCreaInvitacionNiCorreo_YConservaSusDosMiembros()
    {
        // La Consultora es empresa + free: ManageMembers (CA-53, D-19) responde 403 PLAN_REQUIRED antes del cupo, así que
        // la invitación queda bloqueada sin crear nada. Los 2 miembros se conservan en la base (RF-75 b).
        var (owner, consultancy) = await ConsultancyAsync();
        using var http = ClientFor(factory, factory.Tokens.ForUser(owner, consultancy, OrgRole.Owner));
        var email = NewEmail("consultora");

        using var response = await InviteAsync(http, email, OrgRole.Member);

        await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Pro);
        Assert.Empty(await InvitationsAsync(ConsultancyScenario.OrganizationId));
        Assert.Equal(2, await ActiveMembersAsync(ConsultancyScenario.OrganizationId));
        await AssertNoInvitationEmailAsync(email);
    }

    // ── CA-99: por encima de un límite tras la bajada ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA99_Consultora_ClientesYFacturasPorEncimaDelLimite_SonVisiblesYEditables_YCrearResponde422()
    {
        var (owner, consultancy) = await ConsultancyAsync();
        using var http = ClientFor(factory, factory.Tokens.ForUser(owner, consultancy, OrgRole.Owner));

        var clients = await GetArrayAsync(http, ClientsEndpoint);
        Assert.Equal(3, clients.Count);
        var client = clients[0];
        using (var patched = await http.PatchAsJsonAsync($"{ClientsEndpoint}/{client.GetProperty("id").GetGuid()}",
                   new { name = client.GetProperty("name").GetString() + " editado", contactName = "Contacto editado" }))
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        using (var created = await PostClientAsync(http))
            await AssertQuotaExceededAsync(created, PlanLimits.FeatureKeys.MaxClients);

        Assert.Equal(4, (await GetArrayAsync(http, InvoicesEndpoint)).Count);
        using (var invoice = await PostInvoiceAsync(http, client.GetProperty("id").GetGuid()))
            await AssertQuotaExceededAsync(invoice, PlanLimits.FeatureKeys.MaxInvoicesMonth);
    }

    [Fact]
    public async Task PLAN0018_CA99_EmpresaQueBajaDeProAFree_TrasBorrarClientesYFacturasPorDebajoDelLimite_VuelveACrear()
    {
        // Mismo estado que la Consultora (3 clientes con límite 2 y 4 facturas del mes con límite 3), creado con los
        // endpoints reales en pro y bajado a free por la vía de producción: los clientes y facturas de la Consultora tienen
        // proyectos activos o están enviadas, así que no se pueden borrar para bajar del límite.
        var tenants = await BuildAsync(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro));
        using var http = tenants.ClientFor(tenants.A);
        var clientIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
            clientIds.Add(await CreateAsync(await PostClientAsync(http)));
        var invoiceIds = new List<Guid>();
        for (var i = 0; i < 4; i++)
            invoiceIds.Add(await CreateAsync(await PostInvoiceAsync(http, clientIds[0])));
        await TenantBuilder.AssignPlanAsync(factory.Services, tenants.A.Owner, Plan.Free);

        Assert.Equal(3, (await GetArrayAsync(http, ClientsEndpoint)).Count);
        using (var patched = await http.PatchAsJsonAsync($"{ClientsEndpoint}/{clientIds[0]}",
                   new { name = "Cliente editado", contactName = "Contacto" }))
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        using (var blocked = await PostClientAsync(http))
            await AssertQuotaExceededAsync(blocked, PlanLimits.FeatureKeys.MaxClients);
        using (var blocked = await PostInvoiceAsync(http, clientIds[0]))
            await AssertQuotaExceededAsync(blocked, PlanLimits.FeatureKeys.MaxInvoicesMonth);

        foreach (var id in clientIds.Skip(1))
            using (var deleted = await http.DeleteAsync($"{ClientsEndpoint}/{id}"))
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        foreach (var id in invoiceIds.Take(2))
            using (var deleted = await http.DeleteAsync($"{InvoicesEndpoint}/{id}"))
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using (var client = await PostClientAsync(http))
            Assert.Equal(HttpStatusCode.Created, client.StatusCode);
        using (var invoice = await PostInvoiceAsync(http, clientIds[0]))
            Assert.Equal(HttpStatusCode.Created, invoice.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA99_EmpresaQueBajaDeStudioAPro_ConSeisMiembros_VisiblesYEditables_InvitarDa422_YTrasDarDeBajaDosSeInvita()
    {
        // 6 vigentes con max_org_members = 5 en pro: lo existente se ve y se edita, la invitación se bloquea hasta quedar
        // por debajo del límite (RF-75 d).
        var tenants = await BuildAsync(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Studio)
            .WithMember(OrgRole.Admin).WithMember(OrgRole.Member).WithMember(OrgRole.Member)
            .WithMember(OrgRole.Member).WithMember(OrgRole.Member));
        await TenantBuilder.AssignPlanAsync(factory.Services, tenants.A.Owner, Plan.Pro);
        using var http = tenants.ClientFor(tenants.A);
        var members = tenants.A.Members.Where(m => m.Role == OrgRole.Member).Select(m => m.User).ToList();

        Assert.Equal(6, (await GetArrayAsync(http, MembersEndpoint)).Count);
        using (var changed = await ChangeRoleAsync(http, members[0].UserId, OrgRole.Admin))
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using (var blocked = await InviteAsync(http, NewEmail("sobre-limite"), OrgRole.Member))
            await AssertMemberQuotaExceededAsync(blocked, ProMaxOrgMembers, used: 6);

        foreach (var member in members.Skip(2))
            using (var removed = await RemoveAsync(http, member.UserId))
                Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using var invited = await InviteAsync(http, NewEmail("bajo-limite"), OrgRole.Member);
        Assert.Equal(HttpStatusCode.Accepted, invited.StatusCode);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> InviteAsync(HttpClient http, string email, string? roleId, string? name = null)
        => http.PostAsJsonAsync(InviteEndpoint, new { email, roleId, name });

    private static HttpClient ClientFor(WebApplicationFactory<Program> host, string accessToken)
    {
        var http = host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http;
    }

    private async Task<TenantPair> BuildAsync(Action<TenantOptions> configureA, Action<TenantOptions>? configureB = null)
    {
        await EnsureSeededAsync();
        var builder = new TenantBuilder(factory).ForA(configureA);
        if (configureB is not null)
            builder.ForB(configureB);
        return await builder.BuildAsync();
    }

    // count organizaciones empresa + studio (sin cupo de miembros), de a dos por TenantBuilder.
    private async Task<List<(TenantPair Pair, SeededTenant Tenant)>> StudioOrganizationsAsync(int count)
    {
        var organizations = new List<(TenantPair, SeededTenant)>();
        while (organizations.Count < count)
        {
            var pair = await BuildAsync(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Studio),
                b => b.WithLegalType(LegalType.Empresa).WithPlan(Plan.Studio));
            organizations.Add((pair, pair.A));
            organizations.Add((pair, pair.B));
        }
        return organizations;
    }

    // Empresa + pro con owner, un admin y un member.
    private Task<TenantPair> ProOrganizationAsync()
        => BuildAsync(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro).WithMember(OrgRole.Admin).WithMember(OrgRole.Member));

    // CA-58: A con owner y 3 miembros vigentes (un admin y dos members), en el plan pedido; B empresa en el suyo.
    private Task<TenantPair> QuotaOrganizationAsync(string plan, string? bPlan = null)
        => BuildAsync(
            a => a.WithLegalType(LegalType.Empresa).WithPlan(plan)
                .WithMember(OrgRole.Admin).WithMember(OrgRole.Member).WithMember(OrgRole.Member),
            b => b.WithLegalType(LegalType.Empresa).WithPlan(bPlan ?? Plan.Studio));

    // Invitación pendiente sembrada como el seed: emitida en issuedAtUtc (vencida si fue hace más de 7 días).
    private async Task SeedInvitationAsync(SeededTenant tenant, DateTime issuedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.OrganizationInvitations.Add(OrganizationInvitation.Issue(tenant.OrganizationId, NewEmail("pendiente"),
            OrgRole.Member, OneTimeToken.Issue().Hash, tenant.Owner.UserId, issuedAtUtc));
        await db.SaveChangesAsync();
    }

    private async Task<List<OrganizationInvitation>> InvitationsAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationInvitations.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.OrganizationId == organizationId)
            .ToListAsync();
    }

    private async Task<int> ActiveMembersAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationMembers.IgnoreQueryFilters()
            .CountAsync(m => m.OrganizationId == organizationId && m.DeletedAt == null);
    }

    // CA-93: nombre de la organización y del owner (el invitador) con HTML.
    private async Task RenameAsync(SeededTenant tenant, string organizationName, string ownerFirstName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var organization = await db.Organizations.SingleAsync(o => o.OrganizationId == tenant.OrganizationId);
        db.Entry(organization).Property(o => o.Name).CurrentValue = organizationName;
        var owner = await db.Users.SingleAsync(u => u.UserId == tenant.Owner.UserId);
        db.Entry(owner).Property(u => u.FirstName).CurrentValue = ownerFirstName;
        db.Entry(owner).Property(u => u.LastName).CurrentValue = string.Empty;
        await db.SaveChangesAsync();
    }

    // Host derivado cuyo IEmailService es el ResendEmailService real sobre CapturingResend: el HTML que se captura es el
    // de las plantillas de producción, sin llamar a Resend.
    private WebApplicationFactory<Program> WithRealTemplates(Resend.IResend resend)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(new ResendEmailService(resend, new ConfigurationBuilder().Build()));
        }));

    private static async Task<Resend.EmailMessage> WaitForMessageAsync(Resend.IResend resend, string subjectFragment)
    {
        var capturing = CapturingResend.Of(resend);
        Resend.EmailMessage? Match()
            => capturing.Messages.ToArray().FirstOrDefault(m => m.Subject.Contains(subjectFragment, StringComparison.Ordinal));

        await WaitUntilAsync(() => Match() is not null, $"La cola no entregó el correo «{subjectFragment}» a tiempo.");
        return Match()!;
    }

    // Prueba que un rechazo no encoló correo: la cola es FIFO de un solo lector, así que cuando llega un correo de
    // control encolado después del rechazo, cualquier correo anterior ya se habría registrado.
    private async Task AssertNoInvitationEmailAsync(
        string toEmail, int alreadySent = 0, WebApplicationFactory<Program>? host = null)
    {
        var services = (host ?? (WebApplicationFactory<Program>)factory).Services;
        var emails   = services.GetRequiredService<FakeEmailService>();
        var control  = NewEmail("control-cola");
        Assert.True(services.GetRequiredService<IAuthEmailQueue>().TryEnqueue("test-control", (email, ct) =>
            email.SendReauthenticationFailureWarningAsync(control, "Control", DateTime.UtcNow, ct)));
        await WaitUntilAsync(() => emails.Sent.Any(sent => sent.ToEmail == control), "La cola no entregó el control.");

        Assert.Equal(alreadySent, emails.Sent.Count(sent => sent.Kind == InviteKind
            && string.Equals(sent.ToEmail, toEmail, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response, string code)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        }
    }

    // 422 QUOTA_EXCEEDED de max_org_members con su límite y su uso (miembros vigentes + invitaciones pendientes).
    private static Task AssertMemberQuotaExceededAsync(HttpResponseMessage response, int limit, int used)
        => AssertQuotaExceededAsync(response, PlanLimits.FeatureKeys.MaxOrgMembers, limit, used);

    private static async Task AssertQuotaExceededAsync(
        HttpResponseMessage response, string featureKey, int? limit = null, int? used = null)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("QUOTA_EXCEEDED", body.RootElement.GetProperty("code").GetString());
            Assert.Equal(featureKey, body.RootElement.GetProperty("featureKey").GetString());
            var details = body.RootElement.GetProperty("details")[0];
            if (limit is not null)
                Assert.Equal(limit, details.GetProperty("limit").GetInt32());
            if (used is not null)
                Assert.Equal(used, details.GetProperty("used").GetInt32());
        }
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.True(response.Headers.RetryAfter is not null, "Falta Retry-After.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(AuthRateLimitAttribute.RateLimitedCode, body.RootElement.GetProperty("code").GetString());
        }
    }

    private static Task<HttpResponseMessage> PostClientAsync(HttpClient http)
        => http.PostAsJsonAsync(ClientsEndpoint, new { name = $"Cliente {Guid.NewGuid():N}", contactName = "Contacto" });

    private static Task<HttpResponseMessage> PostInvoiceAsync(HttpClient http, Guid clientId)
        => http.PostAsJsonAsync(InvoicesEndpoint, new
        {
            clientId,
            dueAt = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            items = new[] { new { description = "Servicio", quantity = 1m, unitPrice = 100_000m } },
            currency = "COP",
        });

    private static async Task<Guid> CreateAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("id").GetGuid();
        }
    }

    private static async Task<List<JsonElement>> GetArrayAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().Select(element => element.Clone()).ToList();
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    private async Task<(User Owner, Organization Consultancy)> ConsultancyAsync()
    {
        await EnsureSeededAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var owner = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == ConsultancyScenario.Owner.Id);
        var consultancy = await db.Organizations.AsNoTracking()
            .SingleAsync(o => o.OrganizationId == ConsultancyScenario.OrganizationId);
        return (owner, consultancy);
    }

    private Task EnsureSeededAsync() => DemoScenarioSeed.EnsureAsync(factory.Services);
}
