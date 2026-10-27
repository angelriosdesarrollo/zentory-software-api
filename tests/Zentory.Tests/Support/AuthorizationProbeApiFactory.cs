using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Authorization;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Tests.Support;

// Host de pruebas con acciones de prueba de autorización (PLAN-0018 RF-15, RF-58; CA-73, CA-74, CA-120, CA-20 en parte).
// Hay policies sin endpoint real todavía (RequiresPro, RequiresStudio, EmpresaPro, RequireOrgOwner,
// RequireFinancialAccess): estas acciones las exponen sobre el pipeline real (JwtBearer, MembershipValidationMiddleware,
// policies y ZentoryAuthorizationResultHandler). Solo existen en este host: ZentoryApiFactory y los catálogos
// (SkipMembershipCatalogTests, IdorRouteCatalogTests) no las ven.
//
// Cada acción responde con la organización de IMembershipContext cuando está resuelta, para detectar cruces entre
// peticiones concurrentes.
public sealed class AuthorizationProbeApiFactory : ZentoryApiFactory
{
    public const string BasePath               = "/api/v1/authorization-probe";
    public const string RequiresProPath        = BasePath + "/requires-pro";
    public const string RequiresStudioPath     = BasePath + "/requires-studio";
    public const string EmpresaProPath         = BasePath + "/empresa-pro";
    public const string RequireOrgOwnerPath    = BasePath + "/org-owner";
    public const string RequireOrgAdminPath    = BasePath + "/org-admin";
    public const string FinancialAccessPath    = BasePath + "/financial-access";
    public const string SkippedOrgOwnerPath    = BasePath + "/skipped-org-owner";
    public const string EmpresaProOrgAdminPath = BasePath + "/empresa-pro-org-admin";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
            services.AddControllers().ConfigureApplicationPartManager(manager =>
                manager.FeatureProviders.Add(new ProbeControllerProvider())));
    }

    // Anidada a propósito: MVC solo descubre controladores públicos de primer nivel, así que solo existe en el host al
    // que ProbeControllerProvider la agrega.
    [ApiController]
    [Authorize]
    [Route("api/v1/authorization-probe")]
    public sealed class AuthorizationProbeController(IMembershipContext membership) : ControllerBase
    {
        [Authorize(Policy = PolicyNames.RequiresPro)]
        [HttpGet("requires-pro")]
        public IActionResult RequiresPro() => Probe();

        [Authorize(Policy = PolicyNames.RequiresStudio)]
        [HttpGet("requires-studio")]
        public IActionResult RequiresStudio() => Probe();

        [Authorize(Policy = PolicyNames.EmpresaPro)]
        [HttpGet("empresa-pro")]
        public IActionResult EmpresaPro() => Probe();

        [Authorize(Policy = PolicyNames.RequireOrgOwner)]
        [HttpGet("org-owner")]
        public IActionResult OrgOwner() => Probe();

        [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
        [HttpGet("org-admin")]
        public IActionResult OrgAdmin() => Probe();

        [Authorize(Policy = PolicyNames.RequireFinancialAccess)]
        [HttpGet("financial-access")]
        public IActionResult FinancialAccess() => Probe();

        // Tipo de cuenta, plan y rol a la vez: fija la precedencia tipo → plan → rol cuando fallan varios (RF-58, D-8).
        [Authorize(Policy = PolicyNames.EmpresaPro)]
        [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
        [HttpGet("empresa-pro-org-admin")]
        public IActionResult EmpresaProOrgAdmin() => Probe();

        // Excluida del middleware de membresía: IMembershipContext queda sin poblar y RequireOrgOwner debe fallar aunque
        // el claim diga owner (SR-0018.7).
        [SkipMembershipValidation]
        [Authorize(Policy = PolicyNames.RequireOrgOwner)]
        [HttpGet("skipped-org-owner")]
        public IActionResult SkippedOrgOwner() => Probe();

        private IActionResult Probe()
            => Ok(new ProbeResponse(membership.IsResolved ? membership.OrganizationId : null));
    }

    public sealed record ProbeResponse(Guid? OrganizationId);

    private sealed class ProbeControllerProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var probe = typeof(AuthorizationProbeController).GetTypeInfo();
            if (!feature.Controllers.Contains(probe))
                feature.Controllers.Add(probe);
        }
    }
}
