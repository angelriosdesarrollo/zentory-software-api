using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Invariante de membresías (PLAN-0018 RF-48 e, SR-0018.8; CA-T30-1): cada organización tiene exactamente una membresía
// vigente owner, la de Organization.OwnerId, y como máximo una membresía vigente por (org, user). En PostgreSQL la
// segunda parte la respalda el índice único filtrado de M4b; la suite corre en InMemory, que no aplica índices únicos,
// así que este chequeo tras cada escenario es la red de seguridad de los tests.
public static class MembershipInvariant
{
    // Comprueba el invariante en las dos organizaciones del builder.
    public static Task AssertHoldsAsync(IServiceProvider services, TenantPair tenants)
        => AssertHoldsAsync(services, tenants.A.OrganizationId, tenants.B.OrganizationId);

    public static async Task AssertHoldsAsync(IServiceProvider services, params Guid[] organizationIds)
    {
        var violations = await ViolationsAsync(services, organizationIds);
        Assert.True(violations.Count == 0, "Invariante de membresías roto: " + string.Join("; ", violations));
    }

    // Las violaciones encontradas, sin lanzar: lo usan los tests que prueban que el chequeo detecta un estado roto.
    public static async Task<IReadOnlyList<string>> ViolationsAsync(IServiceProvider services, params Guid[] organizationIds)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var violations = new List<string>();

        foreach (var organizationId in organizationIds.Distinct())
        {
            var organization = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(o => o.OrganizationId == organizationId);
            var active = await db.OrganizationMembers.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.OrganizationId == organizationId && m.DeletedAt == null)
                .ToListAsync();

            var owners = active.Where(m => m.Role == OrgRole.Owner).ToList();
            if (owners.Count != 1)
                violations.Add($"{organizationId}: {owners.Count} membresías vigentes owner (se espera 1)");
            else if (owners[0].UserId != organization.OwnerId)
                violations.Add($"{organizationId}: la membresía vigente owner no es la de OwnerId");

            violations.AddRange(active
                .GroupBy(m => m.UserId)
                .Where(group => group.Count() > 1)
                .Select(group => $"{organizationId}: {group.Count()} membresías vigentes del usuario {group.Key}"));
        }

        return violations;
    }
}
