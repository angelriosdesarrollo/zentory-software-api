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
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICollaboratorPortalContext, CollaboratorPortalContext>();

        return services;
    }
}
