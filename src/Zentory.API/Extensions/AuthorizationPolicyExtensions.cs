using Microsoft.AspNetCore.Authorization;
using Zentory.API.Authorization;
using Zentory.Domain.Constants;

namespace Zentory.API.Extensions;

public static class AuthorizationPolicyExtensions
{
    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("EmpresaOnly", policy =>
                policy.RequireClaim("legal_type", LegalType.Empresa));

            options.AddPolicy("RequiresPro", policy =>
                policy.Requirements.Add(new MinimumPlanRequirement(Plan.Pro)));

            options.AddPolicy("RequiresStudio", policy =>
                policy.Requirements.Add(new MinimumPlanRequirement(Plan.Studio)));

            options.AddPolicy("EmpresaPro", policy =>
            {
                policy.RequireClaim("legal_type", LegalType.Empresa);
                policy.Requirements.Add(new MinimumPlanRequirement(Plan.Pro));
            });

            options.AddPolicy("EmpresaStudio", policy =>
            {
                policy.RequireClaim("legal_type", LegalType.Empresa);
                policy.Requirements.Add(new MinimumPlanRequirement(Plan.Studio));
            });

            options.AddPolicy("CollaboratorAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("CollaboratorScheme");
                policy.RequireClaim("token_type", "collaborator_session");
            });
        });

        services.AddSingleton<IAuthorizationHandler, MinimumPlanHandler>();

        return services;
    }
}
