using Microsoft.AspNetCore.Authorization;
using Zentory.API.Authorization;
using Zentory.Domain.Constants;

namespace Zentory.API.Extensions;

// Nombres de las policies. Públicos para que controladores y tests no repitan el literal.
public static class PolicyNames
{
    public const string EmpresaOnly            = "EmpresaOnly";
    public const string RequiresPro            = "RequiresPro";
    public const string RequiresStudio         = "RequiresStudio";
    public const string EmpresaPro             = "EmpresaPro";
    public const string EmpresaStudio          = "EmpresaStudio";
    public const string CollaboratorAuth       = "CollaboratorAuth";
    public const string RequireOrgOwner        = "RequireOrgOwner";
    public const string RequireOrgAdmin        = "RequireOrgAdmin";
    public const string RequireFinancialAccess = "RequireFinancialAccess";
    public const string ManageMembers          = "ManageMembers";
}

public static class AuthorizationPolicyExtensions
{
    private const string LegalTypeClaim = ZentoryAuthorizationResultHandler.LegalTypeClaim;

    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyNames.EmpresaOnly, policy =>
                policy.RequireClaim(LegalTypeClaim, LegalType.Empresa));

            options.AddPolicy(PolicyNames.RequiresPro, policy =>
                policy.Requirements.Add(new MinimumPlanRequirement(Plan.Pro)));

            options.AddPolicy(PolicyNames.RequiresStudio, policy =>
                policy.Requirements.Add(new MinimumPlanRequirement(Plan.Studio)));

            options.AddPolicy(PolicyNames.EmpresaPro, policy =>
            {
                policy.RequireClaim(LegalTypeClaim, LegalType.Empresa);
                policy.Requirements.Add(new MinimumPlanRequirement(Plan.Pro));
            });

            options.AddPolicy(PolicyNames.EmpresaStudio, policy =>
            {
                policy.RequireClaim(LegalTypeClaim, LegalType.Empresa);
                policy.Requirements.Add(new MinimumPlanRequirement(Plan.Studio));
            });

            options.AddPolicy(PolicyNames.CollaboratorAuth, policy =>
            {
                policy.AuthenticationSchemes.Add("CollaboratorScheme");
                policy.RequireClaim("token_type", "collaborator_session");
            });

            // Roles de la organización activa (PLAN-0018 RF-15): leen IMembershipContext, nunca el claim.
            options.AddPolicy(PolicyNames.RequireOrgOwner, policy =>
                policy.Requirements.Add(OrgRoleRequirement.Owner()));

            options.AddPolicy(PolicyNames.RequireOrgAdmin, policy =>
                policy.Requirements.Add(OrgRoleRequirement.Admin()));

            options.AddPolicy(PolicyNames.RequireFinancialAccess, policy =>
                policy.Requirements.Add(OrgRoleRequirement.FinancialAccess()));

            // Gestión de miembros y roles (PLAN-0018 RF-47; D-19; DEC-0014): empresa + plan efectivo pro (EffectivePlan,
            // D-26) + owner o admin. ZentoryAuthorizationResultHandler aplica la precedencia tipo → plan → rol: freelance
            // → ACCESS_DENIED, empresa free → PLAN_REQUIRED con requiredPlan pro, member → ACCESS_DENIED.
            options.AddPolicy(PolicyNames.ManageMembers, policy =>
            {
                policy.RequireClaim(LegalTypeClaim, LegalType.Empresa);
                policy.Requirements.Add(new MinimumPlanRequirement(Plan.Pro));
                policy.Requirements.Add(OrgRoleRequirement.Admin());
            });
        });

        // MinimumPlanHandler y OrgRoleRequirementHandler son scoped: reciben el IMembershipContext de la petición (plan
        // efectivo del owner, D-26; rol, RF-15), nunca compartido entre peticiones (SR-0018.40).
        services.AddScoped<IAuthorizationHandler, MinimumPlanHandler>();
        services.AddScoped<IAuthorizationHandler, OrgRoleRequirementHandler>();

        // 403 con cuerpo y código en todas las policies (RF-58, D-8).
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ZentoryAuthorizationResultHandler>();

        return services;
    }
}
