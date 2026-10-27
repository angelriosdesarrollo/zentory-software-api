using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Authorization;
using Zentory.Application.Common.Interfaces;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 SR-0018.40 (CA-120): ningún handler de autorización singleton recibe IMembershipContext (ni otro servicio
// scoped) por constructor; si lo hiciera, la membresía de una petición se compartiría con las siguientes. Se inspeccionan
// los registros del host real, capturados después de Program.cs.
public sealed class AuthorizationHandlerScopePolicyTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    [Fact]
    public void PLAN0018_CA120_HandlersDeAutorizacionSingleton_NoRecibenIMembershipContextNiScopedPorConstructor()
    {
        var descriptors = HostDescriptors();

        var offending = OffendingSingletonHandlers(descriptors);

        Assert.True(offending.Count == 0,
            "Handlers de autorización singleton con dependencias scoped: " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA120_OrgRoleRequirementHandler_EstaRegistradoComoScoped()
    {
        var descriptors = HostDescriptors();

        var handler = Assert.Single(descriptors, d =>
            d.ServiceType == typeof(IAuthorizationHandler) && d.ImplementationType == typeof(OrgRoleRequirementHandler));
        Assert.Equal(ServiceLifetime.Scoped, handler.Lifetime);
        Assert.Contains(descriptors, d => d.ServiceType == typeof(IMembershipContext) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void PLAN0018_CA120_Detector_EncuentraUnHandlerSingletonConIMembershipContext()
    {
        // Control del primero: el mismo detector señala un singleton que captura la membresía, y uno registrado por
        // fábrica (no inspeccionable); uno sin dependencias no cuenta.
        var descriptors = new List<ServiceDescriptor>
        {
            ServiceDescriptor.Scoped<IMembershipContext, StubMembership>(),
            ServiceDescriptor.Singleton<IAuthorizationHandler, CapturingMembershipHandler>(),
            ServiceDescriptor.Singleton<IAuthorizationHandler>(_ => new CapturingMembershipHandler(new StubMembership())),
            ServiceDescriptor.Singleton<IAuthorizationHandler, StatelessHandler>(),
        };

        var offending = OffendingSingletonHandlers(descriptors);

        Assert.Equal(2, offending.Count);
        Assert.Contains(nameof(CapturingMembershipHandler), offending[0]);
        Assert.Contains("fábrica", offending[1]);
    }

    // ── Detector ────────────────────────────────────────────────────────────────────────────────────────────────

    private static List<string> OffendingSingletonHandlers(IReadOnlyList<ServiceDescriptor> descriptors)
    {
        var scoped = descriptors.Where(d => d.Lifetime == ServiceLifetime.Scoped).Select(d => d.ServiceType).ToHashSet();
        var offending = new List<string>();

        foreach (var descriptor in descriptors.Where(d =>
                     d.ServiceType == typeof(IAuthorizationHandler) && d.Lifetime == ServiceLifetime.Singleton))
        {
            var type = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            if (type is null)
            {
                // Una fábrica no se puede inspeccionar: puede capturar lo que sea.
                var factoryType = descriptor.ImplementationFactory?.Method.ReturnType.Name ?? "?";
                offending.Add($"{factoryType} (registrado por fábrica; no inspeccionable)");
                continue;
            }

            var scopedParameters = type.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType)
                .Where(p => typeof(IMembershipContext).IsAssignableFrom(p) || scoped.Contains(p))
                .Select(p => p.Name)
                .Distinct()
                .ToList();
            if (scopedParameters.Count > 0)
                offending.Add($"{type.Name} ({string.Join(", ", scopedParameters)})");
        }

        return offending;
    }

    private IReadOnlyList<ServiceDescriptor> HostDescriptors()
    {
        List<ServiceDescriptor>? captured = null;
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            captured = services.ToList()));
        _ = host.Services;
        return captured ?? throw new InvalidOperationException("No se capturaron los registros del host.");
    }

    private sealed class StubMembership : IMembershipContext
    {
        public bool   IsResolved     => false;
        public Guid   OrganizationId => Guid.Empty;
        public Guid   UserId         => Guid.Empty;
        public string Role           => string.Empty;
        public Guid?  OwnerUserId    => null;
        public string EffectivePlan  => string.Empty;
        public string? OwnerDisplayName => null;
    }

    private sealed class CapturingMembershipHandler(IMembershipContext membership) : AuthorizationHandler<OrgRoleRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OrgRoleRequirement requirement)
        {
            if (membership.IsResolved) context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    // Handler singleton sin dependencias: el detector no lo señala. MinimumPlanHandler ya no sirve de ejemplo, porque
    // desde T25 recibe IMembershipContext y es scoped (D-26).
    private sealed class StatelessHandler : AuthorizationHandler<OrgRoleRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OrgRoleRequirement requirement)
            => Task.CompletedTask;
    }
}
