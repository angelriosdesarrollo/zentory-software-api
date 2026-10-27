using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// Red de seguridad de la comparación de sesión (PLAN-0018 RF-90 d, DEC-0098; parte de CA-133). El middleware solo compara
// el sid en las acciones que exigen autorización (algún [Authorize] y ningún [AllowAnonymous]), y el host no tiene
// FallbackPolicy: una acción de negocio sin [Authorize] quedaría anónima y sin comparar la sesión. Recorre los endpoints
// del host de pruebas (EndpointDataSource, con los atributos de la clase y del método).
public sealed class SessionComparisonCatalogTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string PublicPrefix = "api/v1/public";

    [Fact]
    public void PLAN0018_CA133_TodaAccionFueraDeApiV1Public_DeclaraAuthorize()
    {
        var offenders = NonPublicActions()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        Assert.True(offenders.Count == 0, "Acciones fuera de api/v1/public sin [Authorize]: " + string.Join(", ", offenders));
    }

    [Fact]
    public void PLAN0018_CA133_AccionesAllowAnonymousFueraDeApiV1Public_SoloEnAuthController()
    {
        var controllers = NonPublicActions()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName)
            .Distinct()
            .ToList();

        Assert.Equal(["Auth"], controllers);
    }

    private List<RouteEndpoint> NonPublicActions()
    {
        var actions = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToList();
        Assert.NotEmpty(actions);
        return actions
            .Where(e => !(e.RoutePattern.RawText ?? "").StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
