using Zentory.API.Middleware;
using Zentory.Application.Common.Interfaces;

namespace Zentory.API.Extensions;

public static class RequestContextExtensions
{
    public static IServiceCollection AddRequestContext(this IServiceCollection services)
    {
        // IMemoryCache — used by PlanLimitService to cache plan limits (10-min TTL)
        services.AddMemoryCache();

        services.AddHttpContextAccessor();
        // Una sola instancia por petición: el middleware la puebla por el tipo concreto y el resto la lee por la
        // interfaz (PLAN-0018 RF-14).
        services.AddScoped<MembershipContext>();
        services.AddScoped<IMembershipContext>(sp => sp.GetRequiredService<MembershipContext>());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICollaboratorPortalContext, CollaboratorPortalContext>();

        return services;
    }
}
