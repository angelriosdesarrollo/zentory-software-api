using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 CA-T11A-1 (parte de CA-103; RF-17 rev. 4, RF-65 d, e; SR-0018.26 c; D-31) sobre el host real: tras eliminar
// A con DELETE /organization, cada acción por token de A responde exactamente como ante un token inexistente (mismo
// estado y mismo cuerpo), sin efecto y sin correo. Vías: GET /public/share/{token}; GET /public/proposals/{token} y los
// POST view, accept y reject; el canje del portal con los tres kind; y request-link.
//
// Un host derivado por test: el canje y request-link comparten una ventana de 10 peticiones por IP en cada host.
public sealed class DeletedOrganizationPublicTokenTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword    = "Owner-Correcta-Tokens-2026!";
    private const string CollaboratorType = "hourly_contractor";
    private const string Period           = "2026-11";

    private const string ShareEndpoint       = "/api/v1/public/share/";
    private const string ProposalsEndpoint   = "/api/v1/public/proposals/";
    private const string ExchangeEndpoint    = "/api/v1/public/collaborator-portal/exchange";
    private const string RequestLinkEndpoint = "/api/v1/public/collaborator-portal/request-link";

    private const string MagicLinkKind     = "magic_link";
    private const string PilaKind          = "pila_request";
    private const string PayoutInvoiceKind = "payout_invoice_request";

    private readonly ZentoryApiFactory              _factory;
    private readonly WebApplicationFactory<Program> _host;

    public DeletedOrganizationPublicTokenTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host    = factory.WithWebHostBuilder(_ => { });
    }

    public void Dispose() => _host.Dispose();

    private FakeEmailService Emails => _host.Services.GetRequiredService<FakeEmailService>();

    // ── GET /public/share/{token} ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA103_PublicShare_TokenDeOrganizacionEliminada_RespondeComoTokenInexistente()
    {
        var scenario = await SeedAsync();
        var share = ProjectShare.Create(scenario.A.OrganizationId, scenario.ProjectInA.Id, scenario.A.Owner.UserId,
            token: Guid.NewGuid().ToString("N"), message: "Avance");
        await scenario.Tenants.SeedAsync(share);

        // Control: antes de eliminar, el enlace muestra el proyecto.
        using (var before = await Anonymous().GetAsync(ShareEndpoint + share.Token))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await DeleteAAsync(scenario);

        using var deleted = await Anonymous().GetAsync(ShareEndpoint + share.Token);
        using var missing = await Anonymous().GetAsync(ShareEndpoint + Guid.NewGuid().ToString("N"));
        await AssertSameResponseAsync(missing, deleted, HttpStatusCode.NotFound);
    }

    // ── /public/proposals/{token} ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "")]
    [InlineData("POST", "/view")]
    [InlineData("POST", "/accept")]
    [InlineData("POST", "/reject")]
    public async Task PLAN0018_CA103_PublicProposal_TokenDeOrganizacionEliminada_RespondeComoTokenInexistenteSinCambiosNiCorreos(
        string method, string action)
    {
        var scenario = await SeedAsync();
        var proposal = await SeedProposalAsync(scenario, scenario.A);

        // Control: antes de eliminar, el token se lee.
        using (var before = await Anonymous().GetAsync(ProposalsEndpoint + proposal.PublicToken))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var snapshot = await ProposalSnapshotAsync(proposal.Id);

        await DeleteAAsync(scenario);
        var emailsBefore = Emails.Sent.Count;

        using var deleted = await SendProposalActionAsync(method, proposal.PublicToken, action);
        using var missing = await SendProposalActionAsync(method, Guid.NewGuid(), action);

        await AssertSameResponseAsync(missing, deleted, method == "GET" || action != "/view"
            ? HttpStatusCode.NotFound
            : HttpStatusCode.NoContent);
        Assert.Equal(snapshot, await ProposalSnapshotAsync(proposal.Id));
        Assert.Equal(emailsBefore, Emails.Sent.Count);
    }

    [Fact]
    public async Task PLAN0018_CA103_PublicProposal_OrganizacionVigente_ViewYAcceptSiSurtenEfecto()
    {
        // Control de los casos anteriores: con la organización vigente, view cuenta la apertura y avisa al creador, y
        // accept cambia el estado. Si la comprobación de organización se rompiera hacia "siempre inexistente", este falla.
        var scenario = await SeedAsync();
        var proposal = await SeedProposalAsync(scenario, scenario.Tenants.B);

        using (var view = await SendProposalActionAsync("POST", proposal.PublicToken, "/view"))
            Assert.Equal(HttpStatusCode.NoContent, view.StatusCode);
        using (var accept = await SendProposalActionAsync("POST", proposal.PublicToken, "/accept"))
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);

        var after = await ProposalSnapshotAsync(proposal.Id);
        Assert.Equal(1, after.ViewCount);
        Assert.NotNull(after.AcceptedAt);
        await OrganizationDeletionApi.WaitUntilAsync(
            () => Emails.Sent.Any(e => e.ToEmail == scenario.Tenants.B.Owner.Email),
            "No llegó el aviso de apertura al creador de la propuesta.");
    }

    // ── Canje del portal (los tres kind) ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA103_CanjeDelPortal_MagicLinkDeColaboradorDeOrganizacionEliminada_RespondeComoTokenInexistenteSinSesion()
    {
        var scenario = await SeedAsync();
        var control = await SeedMagicLinkAsync(scenario.EmailOnlyInA);
        var raw = await SeedMagicLinkAsync(scenario.EmailOnlyInA);

        // Control: antes de eliminar, el mismo tipo de enlace abre sesión.
        using (var before = await ExchangeAsync(MagicLinkKind, control))
            await AssertSessionAsync(before);

        await DeleteAAsync(scenario);

        using var deleted = await ExchangeAsync(MagicLinkKind, raw);
        using var missing = await ExchangeAsync(MagicLinkKind, NewRawToken());
        await AssertSameResponseAsync(missing, deleted, HttpStatusCode.NotFound);
        // Sin efecto: el enlace no se consume.
        Assert.Null((await MagicLinkAsync(raw)).UsedAt);
    }

    [Theory]
    [InlineData(PilaKind)]
    [InlineData(PayoutInvoiceKind)]
    public async Task PLAN0018_CA103_CanjeDelPortal_TokenDeSolicitudDeOrganizacionEliminada_RespondeComoTokenInexistenteSinSesion(
        string kind)
    {
        var scenario = await SeedAsync();
        var token = await SeedRequestTokenAsync(scenario, kind, TimeSpan.FromDays(30));

        // Control: antes de eliminar, el token de solicitud (reusable hasta que vence) abre sesión.
        using (var before = await ExchangeAsync(kind, token.ToString()))
            await AssertSessionAsync(before);

        await DeleteAAsync(scenario);

        using var deleted = await ExchangeAsync(kind, token.ToString());
        using var missing = await ExchangeAsync(kind, Guid.NewGuid().ToString());
        await AssertSameResponseAsync(missing, deleted, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(PilaKind)]
    [InlineData(PayoutInvoiceKind)]
    public async Task PLAN0018_CA103_CanjeDelPortal_TokenVencidoDeOrganizacionEliminada_Responde404YNoTokenExpired(string kind)
    {
        // Un TOKEN_EXPIRED delataría que el token existe: la organización se comprueba antes que la vigencia.
        var scenario = await SeedAsync();
        var token = await SeedRequestTokenAsync(scenario, kind, TimeSpan.FromDays(-1));

        using (var before = await ExchangeAsync(kind, token.ToString()))
        {
            Assert.Equal(HttpStatusCode.Conflict, before.StatusCode);
            Assert.Contains("TOKEN_EXPIRED", await before.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        await DeleteAAsync(scenario);

        using var deleted = await ExchangeAsync(kind, token.ToString());
        using var missing = await ExchangeAsync(kind, Guid.NewGuid().ToString());
        await AssertSameResponseAsync(missing, deleted, HttpStatusCode.NotFound);
    }

    // ── request-link ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA103_RequestLink_ColaboradorDeOrganizacionEliminada_NoEmiteEnlaceNiCorreo()
    {
        var scenario = await SeedAsync();
        var request = new { email = scenario.EmailOnlyInA, idNumber = scenario.CollaboratorInA.IdNumber };

        // Control: antes de eliminar, el email y la cédula del colaborador de A reciben el enlace.
        using (var before = await Anonymous().PostAsJsonAsync(RequestLinkEndpoint, request))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(1, await MagicLinkCountAsync(scenario.EmailOnlyInA));
        Assert.Single(Emails.Sent, e => e.ToEmail == scenario.EmailOnlyInA);

        await DeleteAAsync(scenario);

        using var deleted = await Anonymous().PostAsJsonAsync(RequestLinkEndpoint, request);
        using var missing = await Anonymous().PostAsJsonAsync(RequestLinkEndpoint,
            new { email = $"nadie-{Guid.NewGuid():N}@zentory.test", idNumber = scenario.CollaboratorInA.IdNumber });

        await AssertSameResponseAsync(missing, deleted, HttpStatusCode.OK);
        Assert.Equal(1, await MagicLinkCountAsync(scenario.EmailOnlyInA));
        Assert.Single(Emails.Sent, e => e.ToEmail == scenario.EmailOnlyInA);
    }

    // ── Un mecanismo por vía (SR-0018.26) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA103_PublicProposals_LasCuatroAccionesPorTokenPasanPorPublicProposalLookup()
    {
        // La única lectura por token público de una propuesta en Application es la de PublicProposalLookup, que agrega la
        // condición de organización vigente; una acción nueva que llame al repositorio directo rompe este test.
        var application = Path.Combine(RepoRoot.Path, "src", "Zentory.Application");
        var callers = Directory.EnumerateFiles(application, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => File.ReadAllText(file).Contains(".GetByPublicTokenAsync(", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .ToList();
        var lookups = Directory.EnumerateFiles(Path.Combine(application, "Proposals"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Count(file => File.ReadAllText(file).Contains(".FindLiveByPublicTokenAsync(", StringComparison.Ordinal));

        Assert.Equal(["PublicProposalLookup.cs"], callers);
        Assert.Equal(4, lookups);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Scenario(
        TenantPair Tenants, Project ProjectInA, Collaborator CollaboratorInA, string EmailOnlyInA)
    {
        public SeededTenant A => Tenants.A;
    }

    private sealed record ProposalSnapshot(string Status, int ViewCount, DateTime? AcceptedAt, DateTime? RejectedAt);

    // A con su owner (con contraseña para el DELETE), un proyecto y un colaborador cuyo email solo existe en A; B vigente.
    private async Task<Scenario> SeedAsync()
    {
        var tenants = await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(OwnerPassword)).BuildAsync();
        var a = tenants.A;
        var suffix = Guid.NewGuid().ToString("N");

        var client = Client.Create(a.OrganizationId, $"Cliente {suffix}", "Contacto");
        var project = Project.Create(a.OrganizationId, client.Id, $"Proyecto {suffix}", BillingType.FixedPrice, 1_000m);
        var email = $"colaborador-{suffix}@zentory.test";
        var collaborator = Collaborator.Create(a.OrganizationId, $"Colaborador {suffix[..6]}", CollaboratorType,
            email: email, idNumber: $"10{Random.Shared.Next(10_000_000, 99_999_999)}");

        await tenants.SeedAsync(client, project, collaborator);
        return new Scenario(tenants, project, collaborator, email);
    }

    private async Task<Proposal> SeedProposalAsync(Scenario scenario, SeededTenant tenant)
    {
        var client = Client.Create(tenant.OrganizationId, $"Cliente {Guid.NewGuid():N}", "Contacto");
        var proposal = Proposal.Create(tenant.OrganizationId, client.Id, $"Propuesta {Guid.NewGuid():N}",
            createdBy: tenant.Owner.UserId);
        proposal.AddItem(ProposalItem.Create(tenant.OrganizationId, proposal.Id, "Servicio", 1m, 500m));
        proposal.MarkAsSent();
        await scenario.Tenants.SeedAsync(client, proposal);
        return proposal;
    }

    private async Task<Guid> SeedRequestTokenAsync(Scenario scenario, string kind, TimeSpan validity)
    {
        var collaborator = scenario.CollaboratorInA;
        if (kind == PilaKind)
        {
            var pila = PilaVerification.Create(scenario.A.OrganizationId, collaborator.Id, Period, tokenValidity: validity);
            await scenario.Tenants.SeedAsync(pila);
            return pila.Token;
        }

        var invoice = CollaboratorPayoutInvoice.Create(scenario.A.OrganizationId, collaborator.Id, Period, "Honorarios",
            1_000m, tokenValidity: validity);
        await scenario.Tenants.SeedAsync(invoice);
        return invoice.PublicToken;
    }

    // Magic link vigente para ese email, guardado como lo guarda RequestCollaboratorAccessLinkCommand (solo el hash).
    private async Task<string> SeedMagicLinkAsync(string email)
    {
        var raw = NewRawToken();
        await WithDbAsync(db =>
        {
            db.CollaboratorAccessTokens.Add(CollaboratorAccessToken.Create(email, Hash(raw), DateTime.UtcNow.AddMinutes(30)));
            return Task.CompletedTask;
        });
        return raw;
    }

    private async Task<CollaboratorAccessToken> MagicLinkAsync(string raw)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var hash = Hash(raw);
        return await db.CollaboratorAccessTokens.AsNoTracking().SingleAsync(t => t.TokenHash == hash);
    }

    private async Task<int> MagicLinkCountAsync(string email)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.CollaboratorAccessTokens.CountAsync(t => t.Email == email);
    }

    private async Task<ProposalSnapshot> ProposalSnapshotAsync(Guid proposalId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var proposal = await db.Proposals.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == proposalId);
        return new ProposalSnapshot(proposal.Status, proposal.ViewCount, proposal.AcceptedAt, proposal.RejectedAt);
    }

    private async Task DeleteAAsync(Scenario scenario)
    {
        using var http = scenario.Tenants.ClientFor(scenario.A);
        using var response = await OrganizationDeletionApi.DeleteAsync(http, scenario.A.Organization.Name, OwnerPassword);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private HttpClient Anonymous() => _host.CreateClient();

    private async Task<HttpResponseMessage> SendProposalActionAsync(string method, Guid token, string action)
    {
        using var http = Anonymous();
        using var request = new HttpRequestMessage(new HttpMethod(method), ProposalsEndpoint + token + action);
        return await http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ExchangeAsync(string kind, string token)
    {
        using var http = Anonymous();
        return await http.PostAsJsonAsync(ExchangeEndpoint, new { kind, token });
    }

    private async Task WithDbAsync(Func<ZentoryDbContext, Task> change)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        await change(db);
        await db.SaveChangesAsync();
    }

    // "Como un token inexistente": mismo estado y mismo cuerpo, byte a byte.
    private static async Task AssertSameResponseAsync(HttpResponseMessage missing, HttpResponseMessage deleted, HttpStatusCode expected)
    {
        Assert.Equal(expected, missing.StatusCode);
        Assert.Equal(missing.StatusCode, deleted.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await deleted.Content.ReadAsStringAsync());
    }

    private static async Task AssertSessionAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"accessToken\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static string NewRawToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static bool IsBuildOutput(string file)
        => file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
