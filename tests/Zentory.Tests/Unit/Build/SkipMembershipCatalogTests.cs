using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Authorization;
using Zentory.API.Controllers.V1;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// Lista cerrada de [SkipMembershipValidation] (PLAN-0018 RF-14, SR-0018.7, architecture.md §2.2; parte de CA-20, se cierra
// en T30c). Las acciones que el host expone con el metadato (ActionDescriptor.EndpointMetadata, lo mismo que lee el
// middleware tras el routing) deben ser exactamente las de esta lista. Una acción nueva excluida rompe la suite hasta
// que se agregue aquí; T11c sumó deleted y restore, y T30c las tres rutas del destino de la transferencia.
public sealed class SkipMembershipCatalogTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    // Controladores excluidos completos: el atributo va en la clase, así sus acciones nuevas también quedan excluidas.
    private static readonly Type[] ExcludedControllers =
    [
        typeof(AuthController),
        typeof(PublicController),
        typeof(PublicProposalsController),
        typeof(CollaboratorPortalAuthController),
        typeof(DevToolsController),
        typeof(DevToolsPayoutGenerationController),
        typeof(DevLocalStorageController),
    ];

    // Acciones de usuario excluidas una a una: partes 1 y 4 (RF-14 rev. 4, lista cerrada completa desde T30c).
    private static readonly string[] ExcludedUserActions =
    [
        "GET api/v1/me",
        "GET api/v1/me/organizations",
        "GET api/v1/me/organizations/deleted",
        "POST api/v1/organizations",
        "POST api/v1/organizations/{id:guid}/restore",
        "POST api/v1/organization/switch",
        "GET api/v1/me/ownership-transfers",
        "POST api/v1/ownership-transfers/{id:guid}/accept",
        "POST api/v1/ownership-transfers/{id:guid}/decline",
    ];

    [Fact]
    public void PLAN0018_CA20_AccionesConSkipMembershipValidation_CoincidenConLaListaCerrada()
    {
        var actions = ActionsOf(factory);

        var expected = Routes(actions.Where(a => ExcludedControllers.Contains(ControllerOf(a))))
            .Concat(ExcludedUserActions)
            .ToHashSet(StringComparer.Ordinal);
        var actual = Routes(actions.Where(IsExcluded)).ToHashSet(StringComparer.Ordinal);

        var unexpected = actual.Except(expected).Order(StringComparer.Ordinal).ToList();
        var missing = expected.Except(actual).Order(StringComparer.Ordinal).ToList();
        Assert.True(unexpected.Count == 0, "Acciones con [SkipMembershipValidation] fuera de la lista cerrada: " + string.Join(", ", unexpected));
        Assert.True(missing.Count == 0, "Acciones de la lista cerrada sin [SkipMembershipValidation]: " + string.Join(", ", missing));
    }

    [Fact]
    public void PLAN0018_CA106_ListadoDeEliminadasYRestore_EstanEnLaListaCerradaDeSkipMembershipValidation()
    {
        // T11c (RF-72 d, RF-85 a): las dos rutas sirven a la sesión sin organización y validan la propiedad por sí mismas.
        var actual = Routes(ActionsOf(factory).Where(IsExcluded)).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("GET api/v1/me/organizations/deleted", actual);
        Assert.Contains("POST api/v1/organizations/{id:guid}/restore", actual);
    }

    [Fact]
    public void PLAN0018_CA20_RutasDelDestinoDeLaTransferencia_EstanEnLaListaCerradaYSusControladoresNoSeExcluyenEnteros()
    {
        // T30c (RF-14 rev. 4, RF-83): las tres rutas del destino van excluidas una a una; ProfileController y
        // OwnershipTransfersController no llevan el atributo en la clase, así una acción nueva suya pasa por el middleware.
        var actual = Routes(ActionsOf(factory).Where(IsExcluded)).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("GET api/v1/me/ownership-transfers", actual);
        Assert.Contains("POST api/v1/ownership-transfers/{id:guid}/accept", actual);
        Assert.Contains("POST api/v1/ownership-transfers/{id:guid}/decline", actual);
        Assert.Null(typeof(ProfileController).GetCustomAttribute<SkipMembershipValidationAttribute>());
        Assert.Null(typeof(OwnershipTransfersController).GetCustomAttribute<SkipMembershipValidationAttribute>());
        Assert.NotNull(typeof(OwnershipTransfersController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void PLAN0018_CA20_ControladoresExcluidos_LlevanElAtributoEnLaClaseYTienenAcciones()
    {
        var actions = ActionsOf(factory);

        foreach (var controller in ExcludedControllers)
        {
            Assert.NotNull(controller.GetCustomAttribute<SkipMembershipValidationAttribute>());
            Assert.Contains(actions, action => ControllerOf(action) == controller);
        }
    }

    [Fact]
    public void PLAN0018_CA20_ListaCerrada_NoIncluyeRutasQueExigenOrganizacion()
    {
        var actual = Routes(ActionsOf(factory).Where(IsExcluded)).ToHashSet(StringComparer.Ordinal);

        // Control de la enumeración: requieren organización activa y siguen pasando por el middleware (§2.2).
        Assert.DoesNotContain("GET api/v1/clients", actual);
        Assert.DoesNotContain("GET api/v1/me/ss-calculations", actual);
        Assert.DoesNotContain("GET api/v1/organization/profile", actual);
        Assert.DoesNotContain("GET api/v1/collaborator-portal/payout-invoices", actual);
        Assert.Contains("POST api/v1/auth/refresh", actual);
    }

    [Fact]
    public void PLAN0018_CA20_AccionDePruebaExcluidaFueraDeLaLista_LaSuiteFalla()
    {
        using var withProbe = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().ConfigureApplicationPartManager(manager =>
                manager.FeatureProviders.Add(new ProbeControllerProvider()))));

        var actual = Routes(ActionsOf(withProbe).Where(IsExcluded)).ToHashSet(StringComparer.Ordinal);
        var expected = Routes(ActionsOf(withProbe).Where(a => ExcludedControllers.Contains(ControllerOf(a))))
            .Concat(ExcludedUserActions)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal([ProbeRoute], actual.Except(expected));
    }

    // ── Enumeración ─────────────────────────────────────────────────────────────────────────────────────────────

    private const string ProbeRoute = "GET api/v1/skip-membership-probe";

    private static bool IsExcluded(ActionDescriptor action)
        => action.EndpointMetadata.OfType<SkipMembershipValidationAttribute>().Any();

    private static Type? ControllerOf(ActionDescriptor action)
        => (action as ControllerActionDescriptor)?.ControllerTypeInfo.AsType();

    private static IEnumerable<string> Routes(IEnumerable<ActionDescriptor> actions)
        => actions.SelectMany(action => (action.ActionConstraints ?? [])
            .OfType<HttpMethodActionConstraint>()
            .SelectMany(constraint => constraint.HttpMethods)
            .Select(method => $"{method} {action.AttributeRouteInfo?.Template}"));

    private static IReadOnlyList<ActionDescriptor> ActionsOf(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;

    // Anidada a propósito: MVC solo descubre controladores públicos de primer nivel, así que solo existe en el host al
    // que ProbeControllerProvider la agrega.
    [ApiController]
    [Authorize]
    [Route("api/v1/skip-membership-probe")]
    public sealed class SkipMembershipProbeController : ControllerBase
    {
        [SkipMembershipValidation]
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    private sealed class ProbeControllerProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var probe = typeof(SkipMembershipProbeController).GetTypeInfo();
            if (!feature.Controllers.Contains(probe))
                feature.Controllers.Add(probe);
        }
    }
}
