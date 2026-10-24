using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;

namespace Zentory.Tests.Support;

// JWT reales para los tests HTTP (PLAN-0001, RF-13; ADR-0001 opción E). Los servicios se resuelven del contenedor de
// la factory, así los tokens se firman con exactamente la configuración que valida JwtBearer en ese host.
public sealed class TestTokens(IServiceProvider services)
{
    public string ForUser(User user, Organization organization, string role, string plan)
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtService>()
            .GenerateAccessToken(user, organization, role, plan);
    }

    public string ForCollaborator(
        string              email,
        IReadOnlyList<Guid> collaboratorIds,
        Guid                activeCollaboratorId,
        Guid                activeOrganizationId,
        string              activeOrganizationName,
        string              authMethod)
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ICollaboratorJwtService>()
            .GenerateSessionToken(email, collaboratorIds, activeCollaboratorId, activeOrganizationId,
                activeOrganizationName, authMethod);
    }
}
