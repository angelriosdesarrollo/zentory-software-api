using System.Net;
using System.Text.Json;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Public;

// PLAN-0018 RF-17 (CA-23; SR-1.4): GET /public/share/{token} y GET /public/proposals/{token} sobre el host real, sin
// autenticación. Un token inexistente o revocado responde 404, y la respuesta de un token vigente no trae el NIT ni el
// email del cliente, ni el email de los miembros. La lista blanca de campos la fija PublicDtoWhitelistTests; aquí se
// comprueba que lo que sale por HTTP tiene la misma forma que el DTO.
public sealed class PublicTokenTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string ShareEndpoint     = "/api/v1/public/share/";
    private const string ProposalsEndpoint = "/api/v1/public/proposals/";

    // ── GET /public/share/{token} ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA23_PublicShare_TokenInexistente_Responde404()
    {
        await new TenantBuilder(factory).BuildAsync();

        using var response = await factory.CreateClient().GetAsync(ShareEndpoint + Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA23_PublicShare_TokenRevocado_Responde404()
    {
        var seed = await SeedShareAsync(share => share.Revoke());

        using var response = await factory.CreateClient().GetAsync(ShareEndpoint + seed.Share.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA23_PublicShare_TokenVigente_SinNitNiEmails()
    {
        var seed = await SeedShareAsync();

        using var response = await factory.CreateClient().GetAsync(ShareEndpoint + seed.Share.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        AssertWithoutPrivateData(body, seed.Tenants, seed.Client);
        Assert.Contains(seed.Project.Name, body, StringComparison.Ordinal);
    }

    // ── GET /public/proposals/{token} ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA23_PublicProposal_TokenInexistente_Responde404()
    {
        await SeedProposalAsync();

        using var response = await factory.CreateClient().GetAsync(ProposalsEndpoint + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA23_PublicProposal_TokenRevocadoPorEliminacion_Responde404()
    {
        // La propuesta no tiene otra revocación que el borrado: PublicTokenExpiresAt no se asigna en ningún flujo.
        var seed = await SeedProposalAsync(proposal => proposal.SoftDelete());

        using var response = await factory.CreateClient().GetAsync(ProposalsEndpoint + seed.Proposal.PublicToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA23_PublicProposal_TokenVigente_SinNitNiEmailsYConLaFormaDelDto()
    {
        var seed = await SeedProposalAsync();

        using var response = await factory.CreateClient().GetAsync(ProposalsEndpoint + seed.Proposal.PublicToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        AssertWithoutPrivateData(body, seed.Tenants, seed.Client);

        // Lo serializado tiene exactamente las propiedades del DTO, y las de cada ítem las de PublicItemDto.
        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            CamelCaseProperties(typeof(Zentory.Application.Proposals.DTOs.PublicProposalDto)),
            json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(
            CamelCaseProperties(typeof(Zentory.Application.Proposals.DTOs.PublicItemDto)),
            item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record ShareSeed(TenantPair Tenants, Client Client, Project Project, ProjectShare Share);
    private sealed record ProposalSeed(TenantPair Tenants, Client Client, Proposal Proposal);

    private async Task<ShareSeed> SeedShareAsync(Action<ProjectShare>? configure = null)
    {
        var tenants = await new TenantBuilder(factory).WithMember(Zentory.Domain.Constants.OrgRole.Admin).BuildAsync();
        var client = NewClient(tenants.A);
        var project = Project.Create(tenants.A.OrganizationId, client.Id, $"Proyecto {Guid.NewGuid():N}",
            BillingType.FixedPrice, 1_000m);
        var share = ProjectShare.Create(tenants.A.OrganizationId, project.Id, tenants.A.Owner.UserId,
            token: Guid.NewGuid().ToString("N"), message: "Avance");
        configure?.Invoke(share);
        await tenants.SeedAsync(client, project, share);
        return new ShareSeed(tenants, client, project, share);
    }

    private async Task<ProposalSeed> SeedProposalAsync(Action<Proposal>? configure = null)
    {
        var tenants = await new TenantBuilder(factory).WithMember(Zentory.Domain.Constants.OrgRole.Admin).BuildAsync();
        var client = NewClient(tenants.A);
        var proposal = Proposal.Create(tenants.A.OrganizationId, client.Id, $"Propuesta {Guid.NewGuid():N}",
            createdBy: tenants.A.Owner.UserId);
        proposal.AddItem(ProposalItem.Create(tenants.A.OrganizationId, proposal.Id, "Servicio", 1m, 500m));
        proposal.MarkAsSent();
        configure?.Invoke(proposal);
        await tenants.SeedAsync(client, proposal);
        return new ProposalSeed(tenants, client, proposal);
    }

    private static Client NewClient(SeededTenant tenant)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return Client.Create(tenant.OrganizationId, $"Cliente {suffix}", "Contacto",
            email: $"cliente-{suffix}@zentory.test", nit: $"9{suffix[..9]}");
    }

    private static void AssertWithoutPrivateData(string body, TenantPair tenants, Client client)
    {
        Assert.DoesNotContain(client.Nit!, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(client.Email!, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tenants.A.Owner.Email, body, StringComparison.OrdinalIgnoreCase);
        foreach (var member in tenants.A.Members)
            Assert.DoesNotContain(member.User.Email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", body, StringComparison.Ordinal);
    }

    private static IEnumerable<string> CamelCaseProperties(Type type)
        => type.GetProperties()
            .Where(p => p.Name != "EqualityContract")
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .Order(StringComparer.Ordinal);
}
