using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.CollaboratorPortal;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.CollaboratorPortal;

// PLAN-0018 RF-11 y RF-12 (CA-16; ADR-0008 §4): el filtro de tenant también aplica a la sesión de colaborador, con su
// active_org_id, sobre el host real (CollaboratorScheme + TenantContext + ZentoryDbContext). El cambio de organización
// del portal sigue funcionando porque ListByEmailAsync es la entrada explícita de la lista blanca, y una lectura de un
// recurso de B con la sesión de A responde 404. La siembra corre sin HttpContext, sin filtro de tenant.
public sealed class PortalTenantFilterTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string SwitchEndpoint   = "/api/v1/collaborator-portal/switch";
    private const string CollaboratorType = "hourly_contractor";

    [Fact]
    public async Task PLAN0018_CA16_PortalSesionEnA_SwitchHaciaB_DevuelveSesionConActiveOrgIdB()
    {
        var seed = await SeedCollaboratorInTwoOrganizationsAsync();
        var sessionA = SessionIn(seed, seed.InA, seed.OrgA);

        using var response = await SendAsync(HttpMethod.Post, SwitchEndpoint, sessionA, new { collaboratorId = seed.InB.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var issued = TestCollaboratorJwt.Read(body.RootElement.GetProperty("accessToken").GetString()!);
        Assert.Equal(seed.OrgB.OrganizationId.ToString(), TestCollaboratorJwt.Claim(issued, "active_org_id"));
        Assert.Equal(seed.InB.Id.ToString(), TestCollaboratorJwt.Claim(issued, "active_collaborator_id"));
        var memberships = body.RootElement.GetProperty("memberships").EnumerateArray()
            .Select(item => item.GetProperty("collaboratorId").GetGuid())
            .Order()
            .ToList();
        Assert.Equal(new[] { seed.InA.Id, seed.InB.Id }.Order(), memberships);
    }

    [Fact]
    public async Task PLAN0018_CA16_PortalSesionEnA_LeeCuentaDeCobroDeB_Devuelve404()
    {
        var seed = await SeedCollaboratorInTwoOrganizationsAsync();
        var sessionA = SessionIn(seed, seed.InA, seed.OrgA);

        using var response = await SendAsync(HttpMethod.Get, DownloadUrlOf(seed.InvoiceOfB), sessionA);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA16_PortalSesionEnB_LeeCuentaDeCobroDeB_Devuelve200()
    {
        // Control del anterior: el recurso existe y la sesión de su organización sí lo lee.
        var seed = await SeedCollaboratorInTwoOrganizationsAsync();
        var sessionB = SessionIn(seed, seed.InB, seed.OrgB);

        using var response = await SendAsync(HttpMethod.Get, DownloadUrlOf(seed.InvoiceOfB), sessionB);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA16_PortalActiveOrgIdA_ConColaboradorActivoDeB_LeeCuentaDeB_Devuelve404PorElFiltro()
    {
        // Defensa en profundidad: el handler solo compara el colaborador activo, que aquí coincide con el dueño de la
        // cuenta. El 404 solo puede venir del filtro de tenant, que acota la sesión a su active_org_id (A).
        var seed = await SeedCollaboratorInTwoOrganizationsAsync();
        var mixed = SessionIn(seed, seed.InB, seed.OrgA);

        using var response = await SendAsync(HttpMethod.Get, DownloadUrlOf(seed.InvoiceOfB), mixed);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Seed(
        string Email, Organization OrgA, Organization OrgB, Collaborator InA, Collaborator InB, CollaboratorPayoutInvoice InvoiceOfB);

    private static string DownloadUrlOf(CollaboratorPayoutInvoice invoice)
        => $"/api/v1/collaborator-portal/payout-invoices/{invoice.Id}/download-url";

    // Sesión de magic link con las dos colaboraciones, activa en la organización indicada.
    private string SessionIn(Seed seed, Collaborator active, Organization organization)
        => factory.Tokens.ForCollaborator(
            seed.Email, [seed.InA.Id, seed.InB.Id], active.Id, organization.OrganizationId, organization.Name,
            CollaboratorAuthMethod.MagicLink);

    private async Task<Seed> SeedCollaboratorInTwoOrganizationsAsync()
    {
        var email = $"persona-{Guid.NewGuid():N}@zentory.test";
        var orgA  = Organization.Create($"Portal A {Guid.NewGuid():N}", LegalType.Empresa);
        var orgB  = Organization.Create($"Portal B {Guid.NewGuid():N}", LegalType.Empresa);
        var inA   = Collaborator.Create(orgA.OrganizationId, "Persona A", CollaboratorType, email: email);
        var inB   = Collaborator.Create(orgB.OrganizationId, "Persona B", CollaboratorType, email: email);
        var invoiceOfB = CollaboratorPayoutInvoice.Create(orgB.OrganizationId, inB.Id, "2026-09", "Servicios", 100m);
        invoiceOfB.MarkGenerated($"payout-invoices/{Guid.NewGuid():N}.pdf");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.Organizations.AddRange(orgA, orgB);
        db.Collaborators.AddRange(inA, inB);
        db.CollaboratorPayoutInvoices.Add(invoiceOfB);
        await db.SaveChangesAsync();

        return new Seed(email, orgA, orgB, inA, inB, invoiceOfB);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
