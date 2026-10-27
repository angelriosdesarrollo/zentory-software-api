using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// JWT reales para los tests HTTP (PLAN-0001, RF-13; ADR-0001 opción E). Los servicios se resuelven del contenedor de
// la factory, así los tokens se firman con exactamente la configuración que valida JwtBearer en ese host.
//
// Sesión única (PLAN-0018 RF-90, D-47 "Tests", R-25; RNF-7): el access token de usuario lleva el sid de la sesión vigente
// del usuario sembrado. Si ya tiene una (un login del test u otro token de estos helpers), se reutiliza, así dos tokens
// del mismo usuario en un test (dos organizaciones, por ejemplo) no se expulsan entre sí; si no, se le inicia una, como
// lo haría un login. Un test que necesita una sesión reemplazada la abre con dos logins reales.
public sealed class TestTokens(IServiceProvider services)
{
    // Serializa la lectura y la escritura de CurrentSessionId entre helpers que corren en paralelo para el mismo usuario:
    // dos inicios de sesión simultáneos dejarían a uno de los dos tokens con una sesión ya reemplazada.
    private static readonly object SessionGate = new();

    // Plan efectivo por petición (PLAN-0018 RF-40, RF-73; D-26; R-12): el backend autoriza con el plan del owner que el
    // middleware lee de su Subscription, nunca con el claim plan. Este token lleva como claim el plan efectivo del owner
    // de la organización en el momento de emitirlo, como login y refresh; el plan para autorizar sale de la suscripción
    // que sembró el test (TenantBuilder, AssignPlanBySeedCommand).
    public string ForUser(User user, Organization organization, string role)
    {
        ArgumentNullException.ThrowIfNull(organization);
        string plan;
        using (var scope = services.CreateScope())
        {
            plan = scope.ServiceProvider.GetRequiredService<IPlanResolutionService>()
                .ResolveForOwnerAsync(organization.OwnerId).GetAwaiter().GetResult();
        }
        return ForUser(user, organization, role, plan);
    }

    // Token con un claim plan elegido por el test. El claim es informativo y no autoriza nada: el argumento solo sirve
    // para emitir un token cuyo claim no coincide con el plan efectivo (CA-96: "un access token vigente emitido con
    // plan = pro") y comprobar que el backend lo ignora.
    public string ForUser(User user, Organization organization, string role, string plan)
    {
        var sessionId = SessionOf(user);
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtService>()
            .GenerateAccessToken(user, organization, role, plan, sessionId);
    }

    // Sesión vigente del usuario en la base (users.current_session_id), iniciada si aún no tiene. Un usuario que no está
    // en la base recibe un sid nuevo que no es la sesión de nadie.
    public Guid SessionOf(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (SessionGate)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var stored = db.Users.SingleOrDefault(u => u.UserId == user.UserId);
            if (stored is null)
                return Guid.NewGuid();
            if (stored.CurrentSessionId is { } current)
                return current;

            var sessionId = stored.StartSession();
            db.SaveChanges();
            return sessionId;
        }
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
