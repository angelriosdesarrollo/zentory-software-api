using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Common.Security;
using Zentory.Domain.Common;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Apoyo de los tests de POST /auth/invitations/accept (PLAN-0018 RF-50, RF-29 f, RF-65 e): la llamada sin JWT y con JWT,
// la siembra de una invitación con su token en claro (solo el hash va a la base, como en InviteMemberCommand) y las
// lecturas de comprobación sin filtros de cuentas, membresías e invitaciones. Lo usan AcceptInvitationTests,
// AuthRateLimitTests y los tests de organización eliminada.
public static class InvitationAcceptApi
{
    public const string AcceptEndpoint = "/api/v1/auth/invitations/accept";

    // Contraseña que cumple PasswordPolicy para las cuentas nuevas.
    public const string NewAccountPassword = "Invitada-Correcta-2026!";

    public sealed record SeededInvitation(OrganizationInvitation Invitation, string RawToken)
    {
        public Guid   Id             => Invitation.Id;
        public Guid   OrganizationId => Invitation.OrganizationId;
        public string Email          => Invitation.Email;
        public string Role           => Invitation.Role;
    }

    // Sin JWT: { token, password, firstName, lastName, acceptedTerms }. La cuenta nueva acepta los términos como un registro
    // (DEC-0110); con acceptedTerms null el campo no viaja en el cuerpo.
    public static Task<HttpResponseMessage> AcceptAnonymousAsync(
        HttpClient http, string token, string? password = NewAccountPassword, string? firstName = "Ana",
        string? lastName = "Invitada", bool? acceptedTerms = true)
        => http.PostAsJsonAsync<object>(AcceptEndpoint, acceptedTerms is { } accepted
            ? new { token, password, firstName, lastName, acceptedTerms = accepted }
            : new { token, password, firstName, lastName });

    // Con JWT: { token } y, si se pide, un organizationId que el endpoint debe ignorar.
    public static async Task<HttpResponseMessage> AcceptWithBearerAsync(
        HttpClient http, string accessToken, string token, Guid? organizationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AcceptEndpoint)
        {
            Content = organizationId is null
                ? JsonContent.Create(new { token })
                : JsonContent.Create(new { token, organizationId }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await http.SendAsync(request);
    }

    // Invitación pendiente de la organización, emitida en nowUtc (por defecto ahora) por invitedBy, con el email ya
    // normalizado como lo guarda InviteMemberCommand.
    public static async Task<SeededInvitation> SeedInvitationAsync(
        IServiceProvider services, Guid organizationId, string email, string role, Guid invitedBy, DateTime? issuedAt = null)
    {
        var issued = OneTimeToken.Issue();
        var invitation = OrganizationInvitation.Issue(
            organizationId, EmailAddress.Normalize(email), role, issued.Hash, invitedBy, issuedAt ?? DateTime.UtcNow);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.OrganizationInvitations.Add(invitation);
        await db.SaveChangesAsync();
        return new SeededInvitation(invitation, issued.RawToken);
    }

    // Las cuentas de TenantBuilder nacen sin verificar (RF-20): la aceptación con JWT exige el email verificado (RF-50 b).
    public static async Task MarkEmailVerifiedAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var user = await db.Users.SingleAsync(u => u.UserId == userId);
        user.MarkEmailVerified();
        await db.SaveChangesAsync();
    }

    public static async Task<OrganizationInvitation> InvitationRowAsync(IServiceProvider services, Guid invitationId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationInvitations.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == invitationId);
    }

    public static async Task<User?> UserByEmailAsync(IServiceProvider services, string email)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var normalized = EmailAddress.Normalize(email);
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == normalized);
    }

    // Todas las membresías (vigentes y borradas) del usuario, en todas las organizaciones.
    public static async Task<List<OrganizationMember>> MembershipsOfAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationMembers.IgnoreQueryFilters().AsNoTracking().Where(m => m.UserId == userId).ToListAsync();
    }

    // Membresías vigentes y borradas de una organización con el email dado (sirve también cuando la cuenta no existe).
    public static async Task<int> MembershipCountForEmailAsync(IServiceProvider services, Guid organizationId, string email)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var normalized = EmailAddress.Normalize(email);
        return await db.OrganizationMembers.IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId)
            .Join(db.Users, m => m.UserId, u => u.UserId, (m, u) => u.Email)
            .CountAsync(memberEmail => memberEmail == normalized);
    }
}
