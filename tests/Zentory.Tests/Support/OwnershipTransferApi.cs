using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Apoyo de los tests de la transferencia de la propiedad (PLAN-0018 RF-64, RF-77, RF-83; D-33; T30b, T30c): las rutas del
// owner y del destino, la lectura de propuestas sin filtros, la verificación del email del destino, el plan propio de un
// usuario que no es owner y la organización con el catálogo de planes sembrado (PlanFeature y PlanLimit de
// ReferenceDataSeeder), que el aviso necesita.
public static class OwnershipTransferApi
{
    public const string TransferEndpoint = "/api/v1/organization/transfer-ownership";
    public const string PreviewEndpoint  = "/api/v1/organization/transfer-ownership/preview";

    // Rutas del catálogo de autorización (plantillas de ASP.NET Core).
    public const string ProposeRoute = "POST api/v1/organization/transfer-ownership";
    public const string CancelRoute  = "DELETE api/v1/organization/transfer-ownership";
    public const string PreviewRoute = "GET api/v1/organization/transfer-ownership/preview";

    // Rutas del destino (T30c).
    public const string MyTransfersEndpoint = "/api/v1/me/ownership-transfers";
    public const string MyTransfersRoute    = "GET api/v1/me/ownership-transfers";
    public const string AcceptTemplate      = "api/v1/ownership-transfers/{id:guid}/accept";
    public const string DeclineTemplate     = "api/v1/ownership-transfers/{id:guid}/decline";
    public const string AcceptRoute         = "POST " + AcceptTemplate;
    public const string DeclineRoute        = "POST " + DeclineTemplate;

    public static string AcceptEndpoint(Guid transferId)  => $"/api/v1/ownership-transfers/{transferId}/accept";
    public static string DeclineEndpoint(Guid transferId) => $"/api/v1/ownership-transfers/{transferId}/decline";

    public static Task<HttpResponseMessage> PreviewAsync(HttpClient http, Guid userId)
        => http.GetAsync($"{PreviewEndpoint}?userId={userId}");

    // previewHash vigente del aviso: es lo que el owner acusa al proponer.
    public static async Task<string> PreviewHashAsync(HttpClient http, Guid userId)
    {
        using var response = await PreviewAsync(http, userId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("previewHash").GetString()!;
    }

    // Cuerpo tal como lo manda el frontend: solo las propiedades presentes.
    public static Task<HttpResponseMessage> ProposeAsync(
        HttpClient http, Guid userId, string? acknowledgedPreviewHash, string? currentPassword = null,
        string? googleIdToken = null)
    {
        var body = new Dictionary<string, object> { ["userId"] = userId };
        if (acknowledgedPreviewHash is not null) body["acknowledgedPreviewHash"] = acknowledgedPreviewHash;
        if (currentPassword is not null)         body["currentPassword"]         = currentPassword;
        if (googleIdToken is not null)           body["googleIdToken"]           = googleIdToken;
        return http.PostAsJsonAsync(TransferEndpoint, body);
    }

    // Aviso y propuesta seguidos, con el hash vigente.
    public static async Task<HttpResponseMessage> ProposeWithCurrentPreviewAsync(
        HttpClient http, Guid userId, string? currentPassword = null, string? googleIdToken = null)
        => await ProposeAsync(http, userId, await PreviewHashAsync(http, userId), currentPassword, googleIdToken);

    public static Task<HttpResponseMessage> CancelAsync(HttpClient http)
        => http.DeleteAsync(TransferEndpoint);

    // Propone con el aviso vigente y devuelve el id de la propuesta (202).
    public static async Task<Guid> ProposeTransferIdAsync(HttpClient http, Guid userId, string ownerPassword)
    {
        using var response = await ProposeWithCurrentPreviewAsync(http, userId, ownerPassword);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("transferId").GetGuid();
    }

    public static Task<HttpResponseMessage> MyTransfersAsync(HttpClient http)
        => http.GetAsync(MyTransfersEndpoint);

    // Cuerpo tal como lo manda el frontend: solo las propiedades presentes (sin ninguna, {}).
    public static Task<HttpResponseMessage> AcceptAsync(
        HttpClient http, Guid transferId, string? currentPassword = null, string? googleIdToken = null)
    {
        var body = new Dictionary<string, string>();
        if (currentPassword is not null) body["currentPassword"] = currentPassword;
        if (googleIdToken is not null)   body["googleIdToken"]   = googleIdToken;
        return http.PostAsJsonAsync(AcceptEndpoint(transferId), body);
    }

    public static Task<HttpResponseMessage> DeclineAsync(HttpClient http, Guid transferId)
        => http.PostAsync(DeclineEndpoint(transferId), null);

    public static async Task<OrganizationOwnershipTransfer> TransferAsync(IServiceProvider services, Guid transferId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationOwnershipTransfers.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == transferId);
    }

    // Filas de ActivityLog de la organización, sin filtros, en orden de creación.
    public static async Task<List<ActivityLog>> ActivityAsync(IServiceProvider services, Guid organizationId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.ActivityLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.OrganizationId == organizationId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
    }

    // Suscripción propia de un usuario que no es owner de nada (AssignPlanBySeedCommand solo asigna planes a owners): es su
    // plan propio, el que pasaría a sostener la organización (RF-83 a). La más reciente gana (PlanResolutionService).
    public static async Task SeedOwnSubscriptionAsync(
        IServiceProvider services, User user, Organization billingOrganization, string plan)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var billingPlan = await db.BillingPlans.SingleAsync(p => p.Name == plan);
        var customer = BillingCustomer.Create(
            billingOrganization.OrganizationId, gatewayCustomerId: $"test_{user.UserId:N}_{Guid.NewGuid():N}", user.Email,
            user.FirstName);
        var subscription = Subscription.Create(user.UserId, customer.Id, billingPlan.Id);
        subscription.Activate($"test_{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30));
        db.BillingCustomers.Add(customer);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    // Organización vigente propia del usuario (owner con su membresía), sembrada fuera de una petición: p. ej. la que el
    // destino crea después de la propuesta (CA-125) o la segunda de un destino que ya llegó a su límite.
    public static async Task<Organization> SeedOwnedOrganizationAsync(IServiceProvider services, User owner, string legalType)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var organization = Organization.Create($"Propia {Guid.NewGuid():N}", legalType);
        organization.SetOwner(owner.UserId);
        db.Organizations.Add(organization);
        db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, owner.UserId, OrgRole.Owner));
        await db.SaveChangesAsync();
        return organization;
    }

    // Propuestas de la organización, sin filtros y sin seguimiento: lo que quedó en la base.
    public static async Task<List<OrganizationOwnershipTransfer>> TransfersAsync(IServiceProvider services, Guid organizationId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationOwnershipTransfers.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.OrganizationId == organizationId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
    }

    public static async Task<int> ActivityCountAsync(IServiceProvider services, Guid organizationId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.ActivityLogs.IgnoreQueryFilters().CountAsync(a => a.OrganizationId == organizationId);
    }

    // Roles vigentes de la organización por usuario, para probar que proponer no los cambia.
    public static async Task<Dictionary<Guid, string>> RolesAsync(IServiceProvider services, Guid organizationId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationMembers.IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && m.DeletedAt == null)
            .ToDictionaryAsync(m => m.UserId, m => m.Role);
    }

    // El destino debe tener el email verificado (RF-64 a); los usuarios del TenantBuilder no lo tienen.
    public static async Task MarkEmailVerifiedAsync(IServiceProvider services, params User[] users)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var ids = users.Select(u => u.UserId).ToList();
        foreach (var user in await db.Users.Where(u => ids.Contains(u.UserId)).ToListAsync())
            user.MarkEmailVerified();
        await db.SaveChangesAsync();
    }

    // Membresía vigente de un usuario de otra organización en la organización dada (p. ej. el owner de B como member de A).
    public static async Task AddMemberAsync(IServiceProvider services, Guid organizationId, User user, string role, Guid invitedBy)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.OrganizationMembers.Add(OrganizationMember.Create(organizationId, user.UserId, role, invitedBy: invitedBy));
        await db.SaveChangesAsync();
    }

    // Organización A empresa con owner (contraseña), un admin y un member (los dos con contraseña y verificados), con el
    // catálogo de planes sembrado antes del builder (DemoScenarioSeed). B queda con su owner y el plan por defecto.
    public static async Task<TenantPair> OrganizationAsync(
        ZentoryApiFactory factory, IServiceProvider services, string ownerPassword, string memberPassword,
        Action<TenantOptions>? configureA = null, Action<TenantOptions>? configureB = null)
    {
        await DemoScenarioSeed.EnsureAsync(factory.Services);
        var tenants = await new TenantBuilder(factory)
            .ForA(a =>
            {
                a.WithLegalType(LegalType.Empresa).WithMember(OrgRole.Admin).WithMember(OrgRole.Member)
                    .WithOwnerPassword(ownerPassword).WithMemberPassword(memberPassword);
                configureA?.Invoke(a);
            })
            .ForB(b => configureB?.Invoke(b))
            .BuildAsync();
        await MarkEmailVerifiedAsync(services, tenants.A.Members.Select(m => m.User).ToArray());
        await MarkEmailVerifiedAsync(services, tenants.B.Members.Select(m => m.User).ToArray());
        return tenants;
    }

    public static async Task AssertConflictAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    public static async Task AssertReauthenticationFailedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("forbidden", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(Zentory.Application.Common.Security.ReauthenticationFailedException.Code,
            body.RootElement.GetProperty("code").GetString());
    }
}
