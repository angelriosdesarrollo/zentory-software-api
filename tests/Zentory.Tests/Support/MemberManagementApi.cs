using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Apoyo de los tests de cambio de rol y baja de miembros (PLAN-0018 T28; RF-46, RF-48, RF-51): las llamadas HTTP, la
// lectura de membresías (también las borradas) y del ActivityLog, y la alteración del rol guardado del owner.
public static class MemberManagementApi
{
    public const string MembersEndpoint  = "/api/v1/organization/members";
    public const string SettingsEndpoint = "/api/v1/organization/settings";
    public const string MemberEntityType = "OrganizationMember";

    // Rutas del catálogo de autorización e IDOR (plantillas de ASP.NET Core).
    public const string ChangeRoleRoute = "api/v1/organization/members/{userId:guid}/role";
    public const string RemoveRoute     = "api/v1/organization/members/{userId:guid}";

    public static Task<HttpResponseMessage> ChangeRoleAsync(HttpClient http, Guid userId, string? roleId)
        => http.PatchAsJsonAsync($"{MembersEndpoint}/{userId}/role", new { roleId });

    public static Task<HttpResponseMessage> RemoveAsync(HttpClient http, Guid userId)
        => http.DeleteAsync($"{MembersEndpoint}/{userId}");

    // Variantes con el userId ya escrito: otra representación del mismo Guid (mayúsculas, N, B, P).
    public static Task<HttpResponseMessage> ChangeRoleAsync(HttpClient http, string userId, string? roleId)
        => http.PatchAsJsonAsync($"{MembersEndpoint}/{Uri.EscapeDataString(userId)}/role", new { roleId });

    public static Task<HttpResponseMessage> RemoveAsync(HttpClient http, string userId)
        => http.DeleteAsync($"{MembersEndpoint}/{Uri.EscapeDataString(userId)}");

    // Formatos alternativos del Guid del owner: ninguno debe esquivar la protección por identidad (RF-87 b).
    public static TheoryData<string> AlternativeGuidFormats => new() { "D-upper", "N", "B", "P" };

    public static string FormatGuid(Guid id, string format) => format switch
    {
        "D-upper" => id.ToString("D").ToUpperInvariant(),
        _         => id.ToString(format),
    };

    // Escritura de control que exige RequireOrgAdmin (CA-55): una clave de la lista blanca sin efectos de auditoría.
    public static Task<HttpResponseMessage> PatchSettingsAsync(HttpClient http)
        => http.PatchAsJsonAsync(SettingsEndpoint, new Dictionary<string, string?> { [OrgSettingKey.ProposalValidity] = "20" });

    // Organización A con owner, dos admins y un member (CA-54); empresa + pro salvo que se pida otra cosa.
    public static Task<TenantPair> FullOrganizationAsync(
        ZentoryApiFactory factory, string? legalType = null, string? plan = null)
        => new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(legalType ?? LegalType.Empresa).WithPlan(plan ?? Plan.Pro)
                .WithMember(OrgRole.Admin).WithMember(OrgRole.Admin).WithMember(OrgRole.Member))
            .BuildAsync();

    // Quien actúa en los casos de owner o admin: el owner o el primer admin.
    public static User Actor(SeededTenant tenant, string role)
        => role == OrgRole.Owner ? tenant.Owner : Admins(tenant)[0];

    // Los admins sembrados de la organización, en orden de siembra.
    public static IReadOnlyList<User> Admins(SeededTenant tenant)
        => tenant.Members.Where(m => m.Role == OrgRole.Admin).Select(m => m.User).ToList();

    // Membresía del usuario en la organización, vigente o borrada (la más reciente), sin seguimiento.
    public static async Task<OrganizationMember?> MembershipAsync(IServiceProvider services, Guid organizationId, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .OrderByDescending(m => m.DeletedAt == null)
            .FirstOrDefaultAsync();
    }

    // Altera el rol guardado de una membresía vigente sin tocar Organization.OwnerId (RF-87 b).
    public static async Task SetStoredRoleAsync(IServiceProvider services, Guid organizationId, Guid userId, string role)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var membership = await db.OrganizationMembers.IgnoreQueryFilters()
            .SingleAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.DeletedAt == null);
        membership.ChangeRole(role);
        await db.SaveChangesAsync();
    }

    // Filas de ActivityLog de miembros de la organización, con su Metadata ya leída.
    public static async Task<List<MemberLogEntry>> MemberLogsAsync(IServiceProvider services, Guid organizationId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var rows = await db.ActivityLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && l.EntityType == MemberEntityType)
            .ToListAsync();
        return rows.Select(MemberLogEntry.From).ToList();
    }
}

// Fila de ActivityLog de un cambio sobre un miembro: { actorUserId, targetUserId, before, after } (RF-51).
public sealed record MemberLogEntry(Guid EntityId, Guid? UserId, string Action, Guid ActorUserId, Guid TargetUserId,
    string? Before, string? After)
{
    public static MemberLogEntry From(ActivityLog row)
    {
        using var metadata = JsonDocument.Parse(row.Metadata ?? "{}");
        var root = metadata.RootElement;
        return new MemberLogEntry(row.EntityId, row.UserId, row.Action,
            root.GetProperty("actorUserId").GetGuid(),
            root.GetProperty("targetUserId").GetGuid(),
            root.GetProperty("before").GetString(),
            root.GetProperty("after").GetString());
    }
}
