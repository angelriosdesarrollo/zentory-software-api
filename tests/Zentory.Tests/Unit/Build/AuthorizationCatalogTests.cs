using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Authorization;
using Zentory.API.Controllers.V1;
using Zentory.API.Extensions;
using Zentory.Application.Collaborators.DTOs;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// Catálogo de autorización por acción (PLAN-0018 RF-87, SR-0018.36, D-43; parte de CA-52, cerrado en T31). Recorre los
// endpoints del host de pruebas (EndpointDataSource: el metadato que ve el pipeline, con los atributos de la clase y del
// método) y exige que cada acción del esquema de usuario declare exactamente una de RequireOrgOwner, RequireOrgAdmin,
// RequireFinancialAccess, ManageMembers o [AnyOrgMember]. Una policy de clase cuenta para sus acciones; las policies de
// plan (EmpresaOnly, EmpresaStudio, …) no cuentan como declaración de rol.
//
// Quedan fuera: las acciones públicas (sin [Authorize] o con [AllowAnonymous]), las del esquema de colaboradores
// (CollaboratorSelfServiceController) y las de la lista cerrada de [SkipMembershipValidation], que vigila
// SkipMembershipCatalogTests.
public sealed class AuthorizationCatalogTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private static readonly string[] RolePolicies =
    [
        PolicyNames.RequireOrgOwner,
        PolicyNames.RequireOrgAdmin,
        PolicyNames.RequireFinancialAccess,
        PolicyNames.ManageMembers,
    ];

    // Acciones con ManageMembers (RF-47, D-19): la gestión de miembros y el catálogo de roles, en orden ordinal. T28 suma
    // PATCH …/role y DELETE …/{userId}; T29 suma POST …/invite.
    private static readonly string[] ExpectedManageMembersActions =
    [
        "DELETE api/v1/organization/members/{userId:guid}",
        "GET api/v1/organization/members",
        "GET api/v1/organization/roles",
        "GET api/v1/organization/roles/permissions",
        "PATCH api/v1/organization/members/{userId:guid}/role",
        "POST api/v1/organization/members/invite",
    ];

    // Lista cerrada de RequireOrgAdmin (RF-15, RF-82 a, RF-86 d y conectar o desconectar integraciones, RF-59 c, RF-66 c
    // desde REV-0005 H-6: 8 acciones), escrita a mano como la enumera la spec.
    private static readonly string[] ExpectedOrgAdminActions =
    [
        "DELETE api/v1/collaborators/{id:guid}",
        "DELETE api/v1/integrations/{id}/connect",
        "GET api/v1/activity-log",
        "PATCH api/v1/collaborators/{id:guid}",
        "PATCH api/v1/organization/profile",
        "PATCH api/v1/organization/settings",
        "POST api/v1/collaborators",
        "POST api/v1/integrations/{id}/connect",
    ];

    // Lista cerrada de RequireFinancialAccess (RF-82 b, RF-86 a-c; deuda de DEC-0105 en /stats/cash-flow), escrita a mano:
    // las 4 acciones de flujo de caja, las 8 de cuentas de cobro, las 7 de PILA del equipo y 3 de estadísticas.
    private static readonly string[] ExpectedFinancialAccessActions =
    [
        "GET api/v1/cash-flow-entries",
        "POST api/v1/cash-flow-entries",
        "PATCH api/v1/cash-flow-entries/{id:guid}",
        "DELETE api/v1/cash-flow-entries/{id:guid}",
        "GET api/v1/team/payout-invoices",
        "POST api/v1/team/payout-invoices/generate",
        "POST api/v1/team/payout-invoices/{id:guid}/send",
        "GET api/v1/team/payout-invoices/{id:guid}/download-url",
        "POST api/v1/team/payout-invoices/request-manual-upload",
        "GET api/v1/team/payout-invoices/suggested-amount",
        "PATCH api/v1/team/payout-invoices/{id:guid}/review",
        "GET api/v1/team/payout-invoices/{id:guid}/project-breakdown",
        "GET api/v1/team/pila-verifications",
        "POST api/v1/team/pila-verifications/request",
        "GET api/v1/team/pila-verifications/{collaboratorId:guid}/history",
        "PATCH api/v1/team/pila-verifications/{id:guid}/verify",
        "GET api/v1/team/pila-verifications/{id:guid}/download-url",
        "GET api/v1/team/pila-verifications/{id:guid}/documents",
        "GET api/v1/team/pila-verifications/documents/{documentId:guid}/download-url",
        "GET api/v1/stats/profitability",
        "GET api/v1/stats/finance",
        "GET api/v1/stats/cash-flow",
    ];

    private static readonly Type[] CollaboratorSchemaControllers = [typeof(CollaboratorSelfServiceController)];

    // Lista cerrada de RequireOrgOwner (RF-87 a), escrita aquí tal como la enumera la spec y no derivada del código: son
    // exactamente seis. Llegaron por tarea: DELETE /organization (T11), las tres de transferencia (T30b) y GET
    // /organization/plan y billing-history (T31). restore y la aceptación de la transferencia quedan fuera: comprueban la
    // propiedad o el destino en el comando (RF-72 d, RF-83).
    private static readonly string[] ExpectedOrgOwnerActions =
    [
        "DELETE api/v1/organization",
        "DELETE api/v1/organization/transfer-ownership",
        "GET api/v1/organization/billing-history",
        "GET api/v1/organization/plan",
        "GET api/v1/organization/transfer-ownership/preview",
        "POST api/v1/organization/transfer-ownership",
    ];

    [Fact]
    public void PLAN0018_CA52_AccionesDelEsquemaDeUsuario_DeclaranExactamenteUnaPolicyDeRolOAnyOrgMember()
    {
        var violations = Violations(EndpointsOf(factory));

        Assert.True(violations.Count == 0,
            "Acciones del esquema de usuario sin exactamente una declaración de autorización (RF-87): "
            + string.Join(", ", violations));
    }

    [Fact]
    public void PLAN0018_CA52_AccionesConRequireOrgOwner_SonExactamenteLasSeisDeRF87a()
    {
        var actual = WithPolicy(EndpointsOf(factory), PolicyNames.RequireOrgOwner);

        Assert.Equal(6, ExpectedOrgOwnerActions.Length);
        Assert.Equal(ExpectedOrgOwnerActions.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void PLAN0018_CA52_ListaCerradaDeRequireOrgOwner_CadaAccionLaDeclaraComoUnicaPolicyDeRolEnOrganizationController()
    {
        // Cada una de las seis existe una sola vez, en OrganizationController, con RequireOrgOwner como única declaración
        // (ni [AnyOrgMember] ni otra policy de rol que la diluya).
        var endpoints = EndpointsOf(factory);

        foreach (var route in ExpectedOrgOwnerActions)
        {
            var endpoint = Assert.Single(endpoints, e => e.Routes.Contains(route));
            Assert.True(IsUserSchemaAction(endpoint), route);
            Assert.Contains(PolicyNames.RequireOrgOwner, endpoint.Policies);
            Assert.Equal(1, endpoint.Declarations);
            Assert.False(endpoint.HasAnyOrgMember, route);
            Assert.Equal(typeof(OrganizationController), endpoint.Controller);
        }
    }

    [Fact]
    public void PLAN0018_CA90_PlanYBillingHistory_SoloOwner_PlanLimitsProfileYSettings_AbiertasACualquierMiembro()
    {
        // RF-66 d: plan y billing-history pasan a RequireOrgOwner; plan-limits, profile y settings (lectura) siguen con
        // [AnyOrgMember] porque no llevan montos y los usan proyectos, propuestas y PILA.
        var endpoints = EndpointsOf(factory);

        foreach (var route in new[] { "GET api/v1/organization/plan", "GET api/v1/organization/billing-history" })
        {
            var endpoint = Assert.Single(endpoints, e => e.Routes.Contains(route));
            Assert.Contains(PolicyNames.RequireOrgOwner, endpoint.Policies);
            Assert.False(endpoint.HasAnyOrgMember, route);
        }

        foreach (var route in new[]
                 {
                     "GET api/v1/organization/plan-limits",
                     "GET api/v1/organization/profile",
                     "GET api/v1/organization/settings",
                 })
        {
            var endpoint = Assert.Single(endpoints, e => e.Routes.Contains(route));
            Assert.True(endpoint.HasAnyOrgMember, route);
            Assert.Equal(1, endpoint.Declarations);
        }
    }

    [Fact]
    public void PLAN0018_CA52_AceptarYRechazarLaTransferencia_SinRequireOrgOwner_ElDestinoLoCompruebaElComando()
    {
        // RF-87 a: la aceptación (y el rechazo) no usan RequireOrgOwner: el que llama es el destino, aún no owner, y la
        // organización de la propuesta no es necesariamente la activa. Quedan excluidas de la membresía (RF-14).
        var endpoints = EndpointsOf(factory);

        foreach (var route in new[] { OwnershipTransferApi.AcceptRoute, OwnershipTransferApi.DeclineRoute })
        {
            var endpoint = Assert.Single(endpoints, e => e.Routes.Contains(route));
            Assert.False(endpoint.IsPublic, route);
            Assert.True(endpoint.SkipsMembership, route);
            Assert.DoesNotContain(PolicyNames.RequireOrgOwner, endpoint.Policies);
            Assert.Equal(0, endpoint.Declarations);
        }
    }

    [Fact]
    public void PLAN0018_CA95_DeleteOrganization_UsaRequireOrgOwnerYEsLaUnicaDeLaListaCerrada()
    {
        // Parte de CA-95 (T11, se cierra en T27): la ruta existe con exactamente RequireOrgOwner como declaración de rol,
        // y la lista cerrada de RequireOrgOwner es la de RF-87 a (las seis desde T31).
        var endpoints = EndpointsOf(factory);
        var delete = Assert.Single(endpoints, e => e.Routes.Contains("DELETE api/v1/organization"));

        Assert.True(IsUserSchemaAction(delete));
        Assert.Contains(PolicyNames.RequireOrgOwner, delete.Policies);
        Assert.Equal(1, delete.Declarations);
        Assert.False(delete.HasAnyOrgMember);
        Assert.Equal(ExpectedOrgOwnerActions.Order(StringComparer.Ordinal), WithPolicy(endpoints, PolicyNames.RequireOrgOwner));
    }

    [Fact]
    public void PLAN0018_CA95_Restore_SinPolicyDeRolNiOrganizacionActiva_LaPropiedadLaCompruebaElComando()
    {
        // Parte de CA-95: restore no puede usar RequireOrgOwner (la organización eliminada no es la activa, RF-85); queda
        // con [Authorize], excluida de la membresía y sin declaración de rol, y el comando exige OwnerId == sub (404
        // uniforme, cubierto por CA-106). Aquí se fijan la ruta y sus metadatos.
        var restore = Assert.Single(EndpointsOf(factory),
            e => e.Routes.Contains("POST api/v1/organizations/{id:guid}/restore"));

        Assert.False(restore.IsPublic);
        Assert.True(restore.SkipsMembership);
        Assert.False(restore.HasAnyOrgMember);
        Assert.Equal(0, restore.Declarations);
        Assert.Equal(typeof(OrganizationsController), restore.Controller);
    }

    [Fact]
    public void PLAN0018_CA53_RutasDeMiembrosYRoles_DeclaranSoloManageMembers_EnSusControladores()
    {
        // Parte de CA-53 (RF-47): las tres rutas existen una sola vez, con ManageMembers como única declaración de rol,
        // y GET /organization/members ya no está en OrganizationController.
        var endpoints = EndpointsOf(factory);

        foreach (var route in ExpectedManageMembersActions)
        {
            var endpoint = Assert.Single(endpoints, e => e.Routes.Contains(route));
            Assert.True(IsUserSchemaAction(endpoint), route);
            Assert.Equal(1, endpoint.Declarations);
            Assert.Contains(PolicyNames.ManageMembers, endpoint.Policies);
            Assert.NotEqual(typeof(OrganizationController), endpoint.Controller);
        }

        Assert.Equal(typeof(OrganizationMembersController),
            Assert.Single(endpoints, e => e.Routes.Contains("GET api/v1/organization/members")).Controller);
        Assert.Equal(typeof(OrganizationRolesController),
            Assert.Single(endpoints, e => e.Routes.Contains("GET api/v1/organization/roles")).Controller);
    }

    [Fact]
    public void PLAN0018_CA52_Catalogo_EnumeraLasAccionesDelEsquemaDeUsuarioYDejaFueraLasExcluidas()
    {
        // Control de la enumeración: si el recorrido no viera las acciones, el primer test pasaría vacío.
        var endpoints = EndpointsOf(factory);
        var checkedRoutes = endpoints.Where(IsUserSchemaAction).SelectMany(e => e.Routes).ToHashSet(StringComparer.Ordinal);
        var allRoutes = endpoints.SelectMany(e => e.Routes).ToHashSet(StringComparer.Ordinal);

        Assert.True(checkedRoutes.Count >= 100, $"Solo se recorrieron {checkedRoutes.Count} acciones del esquema de usuario.");
        Assert.Contains("GET api/v1/clients", checkedRoutes);
        Assert.Contains("GET api/v1/me/ss-calculations", checkedRoutes);
        Assert.Contains("GET api/v1/organization/plan", checkedRoutes);
        Assert.Contains("GET api/v1/organization/billing-history", checkedRoutes);
        Assert.Contains("PATCH api/v1/organization/settings", checkedRoutes);
        Assert.Contains("GET api/v1/cash-flow-entries", checkedRoutes);

        // Excluidas, pero presentes en el host: colaboradores, lista cerrada de [SkipMembershipValidation] y públicas.
        Assert.Contains("GET api/v1/collaborator-portal/payout-invoices", allRoutes);
        Assert.DoesNotContain("GET api/v1/collaborator-portal/payout-invoices", checkedRoutes);
        Assert.DoesNotContain("GET api/v1/me", checkedRoutes);
        Assert.DoesNotContain("POST api/v1/organization/switch", checkedRoutes);
        Assert.DoesNotContain("POST api/v1/auth/refresh", checkedRoutes);
        // T17 (RF-30 d): logout es pública ([AllowAnonymous]) como refresh, en el controlador excluido de /auth.
        Assert.Contains("POST api/v1/auth/logout", allRoutes);
        Assert.DoesNotContain("POST api/v1/auth/logout", checkedRoutes);
        Assert.DoesNotContain(checkedRoutes, route => route.Contains(" api/v1/public/", StringComparison.Ordinal));
    }

    [Fact]
    public void PLAN0018_CA52_ListasCerradas_RequireOrgAdminYRequireFinancialAccess_SonLasDeRF15RF82YRF86()
    {
        // RequireOrgAdmin: lo de T7 (RF-15), la auditoría (RF-82 a) y la escritura de colaboradores (RF-86 d), desde T31b.
        // RequireFinancialAccess: flujo de caja, cuentas de cobro, PILA del equipo y las estadísticas financieras (RF-82 b,
        // RF-86 a-c; /stats/cash-flow por la deuda de DEC-0105). ManageMembers sigue siendo lo de T27.
        var endpoints = EndpointsOf(factory);

        Assert.Equal(ExpectedOrgAdminActions.Order(StringComparer.Ordinal), WithPolicy(endpoints, PolicyNames.RequireOrgAdmin));
        Assert.Equal(ExpectedFinancialAccessActions.Order(StringComparer.Ordinal),
            WithPolicy(endpoints, PolicyNames.RequireFinancialAccess));
        Assert.Equal(ExpectedManageMembersActions, WithPolicy(endpoints, PolicyNames.ManageMembers));

        var anyMember = endpoints.Where(IsUserSchemaAction).Where(e => e.HasAnyOrgMember)
            .SelectMany(e => e.Routes).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("GET api/v1/me/ss-calculations", anyMember);
        Assert.DoesNotContain("GET api/v1/organization/plan", anyMember);
        Assert.DoesNotContain("GET api/v1/organization/billing-history", anyMember);
        Assert.DoesNotContain("GET api/v1/stats/finance", anyMember);
        Assert.DoesNotContain("POST api/v1/collaborators", anyMember);
        Assert.DoesNotContain("GET api/v1/stats/cash-flow", anyMember);
        // T25 (DEC-0104): la vista de flujo de caja sigue siendo studio de empresa, ahora también con acceso financiero.
        Assert.Contains("GET api/v1/stats/cash-flow", WithPolicy(endpoints, PolicyNames.EmpresaStudio));
    }

    [Fact]
    public void PLAN0018_CA115_ActivityLogYPilaDelEquipo_DeclaranLasPoliciesDeRF82()
    {
        // RF-82 a: la auditoría con RequireOrgAdmin. RF-82 b: cada acción de PILA del equipo suma RequireFinancialAccess a
        // EmpresaStudio, como única declaración de rol. RF-82 c: la PILA personal sigue abierta a cualquier miembro.
        var endpoints = EndpointsOf(factory);

        var activityLog = Assert.Single(endpoints, e => e.Routes.Contains("GET api/v1/activity-log"));
        Assert.Contains(PolicyNames.RequireOrgAdmin, activityLog.Policies);
        Assert.Equal(1, activityLog.Declarations);
        Assert.Equal(typeof(ActivityLogController), activityLog.Controller);

        var pila = endpoints.Where(e => e.Controller == typeof(PilaVerificationsController)).ToList();
        Assert.Equal(7, pila.Count);
        foreach (var endpoint in pila)
        {
            var route = string.Join(", ", endpoint.Routes);
            Assert.Contains(PolicyNames.EmpresaStudio, endpoint.Policies);
            Assert.Contains(PolicyNames.RequireFinancialAccess, endpoint.Policies);
            Assert.False(endpoint.HasAnyOrgMember, route);
            Assert.Equal(1, endpoint.Declarations);
        }

        foreach (var endpoint in endpoints.Where(e => e.Controller == typeof(SsCalculationsController)))
        {
            Assert.True(endpoint.HasAnyOrgMember, string.Join(", ", endpoint.Routes));
            Assert.Equal(1, endpoint.Declarations);
        }
    }

    [Fact]
    public void PLAN0018_CA126_ControladoresFinancieros_DeclaranLasPoliciesDeRF86()
    {
        // RF-86 a, b: RequireFinancialAccess de clase sumada a la policy de plan actual (EmpresaOnly, EmpresaStudio).
        var endpoints = EndpointsOf(factory);

        foreach (var (controller, plan, count) in new[]
                 {
                     (typeof(CashFlowEntriesController), PolicyNames.EmpresaOnly, 4),
                     (typeof(PayoutInvoicesController), PolicyNames.EmpresaStudio, 8),
                 })
        {
            var actions = endpoints.Where(e => e.Controller == controller).ToList();
            Assert.Equal(count, actions.Count);
            foreach (var endpoint in actions)
            {
                var route = string.Join(", ", endpoint.Routes);
                Assert.Contains(plan, endpoint.Policies);
                Assert.Contains(PolicyNames.RequireFinancialAccess, endpoint.Policies);
                Assert.False(endpoint.HasAnyOrgMember, route);
                Assert.Equal(1, endpoint.Declarations);
            }

            // A nivel de clase: una acción nueva hereda el cierre.
            var policies = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Select(a => a.Policy).ToList();
            Assert.Contains(plan, policies);
            Assert.Contains(PolicyNames.RequireFinancialAccess, policies);
        }

        // RF-86 c: profitability, finance y cash-flow con RequireFinancialAccess; projects e invoices con [AnyOrgMember].
        foreach (var route in new[] { "GET api/v1/stats/profitability", "GET api/v1/stats/finance", "GET api/v1/stats/cash-flow" })
        {
            var endpoint = Assert.Single(endpoints, e => e.Routes.Contains(route));
            Assert.Contains(PolicyNames.RequireFinancialAccess, endpoint.Policies);
            Assert.Equal(1, endpoint.Declarations);
        }

        foreach (var route in new[] { "GET api/v1/stats/projects", "GET api/v1/stats/invoices" })
        {
            var endpoint = Assert.Single(endpoints, e => e.Routes.Contains(route));
            Assert.True(endpoint.HasAnyOrgMember, route);
            Assert.Equal(1, endpoint.Declarations);
        }

        // RF-86 d: colaboradores, lectura con [AnyOrgMember] y escritura con RequireOrgAdmin, sobre EmpresaStudio.
        foreach (var route in new[] { "GET api/v1/collaborators", "GET api/v1/collaborators/{id:guid}" })
        {
            var endpoint = Assert.Single(endpoints, e => e.Routes.Contains(route));
            Assert.True(endpoint.HasAnyOrgMember, route);
            Assert.Equal(1, endpoint.Declarations);
            Assert.Contains(PolicyNames.EmpresaStudio, endpoint.Policies);
        }

        foreach (var route in new[]
                 {
                     "POST api/v1/collaborators",
                     "PATCH api/v1/collaborators/{id:guid}",
                     "DELETE api/v1/collaborators/{id:guid}",
                 })
        {
            var endpoint = Assert.Single(endpoints, e => e.Routes.Contains(route));
            Assert.Contains(PolicyNames.RequireOrgAdmin, endpoint.Policies);
            Assert.False(endpoint.HasAnyOrgMember, route);
            Assert.Equal(1, endpoint.Declarations);
            Assert.Contains(PolicyNames.EmpresaStudio, endpoint.Policies);
        }
    }

    [Fact]
    public void PLAN0018_CA126_DtosDeColaboradores_TarifasNullablesConWhenWritingNull()
    {
        // D-29 en los dos DTO de colaboradores: las tarifas se omiten del JSON cuando el handler las deja en null.
        foreach (var dto in new[] { typeof(CollaboratorSummaryDto), typeof(CollaboratorDto) })
        foreach (var name in new[] { nameof(CollaboratorDto.HourlyRate), nameof(CollaboratorDto.MonthlyRate) })
        {
            var property = dto.GetProperty(name)!;
            Assert.Equal(typeof(decimal?), property.PropertyType);
            Assert.Equal(JsonIgnoreCondition.WhenWritingNull, property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition);
        }
    }

    [Fact]
    public void PLAN0018_CA52_AccionDePruebaConSoloElAuthorizeDeLaClase_LaSuiteFalla()
    {
        using var withProbe = WithProbes(factory);

        var violations = Violations(EndpointsOf(withProbe));

        // Las sondas bien declaradas (policy de clase, [AnyOrgMember] con policy de plan) no aparecen; las mal declaradas sí.
        Assert.Equal(
            [
                $"GET {ProbeBase}/both-any-member-and-admin",
                $"GET {ProbeBase}/class-authorize-only",
                $"GET {ProbeBase}/plan-policy-only",
            ],
            violations);
    }

    [Fact]
    public void PLAN0018_CA52_AccionDePruebaConRequireOrgOwnerFueraDeLaLista_LaSuiteFalla()
    {
        using var withProbe = WithProbes(factory);

        var actual = WithPolicy(EndpointsOf(withProbe), PolicyNames.RequireOrgOwner);

        Assert.NotEqual(ExpectedOrgOwnerActions.Order(StringComparer.Ordinal), actual);
        Assert.Equal([$"GET {ProbeBase}/org-owner"], actual.Except(ExpectedOrgOwnerActions));
    }

    [Fact]
    public void PLAN0018_CA52_AnyOrgMember_EsSoloMetadatoDeMetodo_SinPolicyNiIAuthorizeData()
    {
        var usage = typeof(AnyOrgMemberAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(typeof(IAuthorizeData).IsAssignableFrom(typeof(AnyOrgMemberAttribute)));
        Assert.False(typeof(IAllowAnonymous).IsAssignableFrom(typeof(AnyOrgMemberAttribute)));
    }

    [Fact]
    public void PLAN0018_CA52_AnyOrgMember_NoApareceEnAccionesExcluidasDeLaMembresia()
    {
        // En una acción sin membresía resuelta "cualquier miembro" no significa nada: sería una declaración engañosa.
        var offending = EndpointsOf(factory)
            .Where(e => e.HasAnyOrgMember && (e.SkipsMembership || CollaboratorSchemaControllers.Contains(e.Controller)))
            .SelectMany(e => e.Routes)
            .ToList();

        Assert.True(offending.Count == 0, "[AnyOrgMember] en acciones excluidas: " + string.Join(", ", offending));
    }

    // ── Clasificación ───────────────────────────────────────────────────────────────────────────────────────────

    private sealed record ActionEndpoint(
        Type? Controller,
        IReadOnlyList<string> Routes,
        bool IsPublic,
        bool SkipsMembership,
        bool HasAnyOrgMember,
        IReadOnlySet<string> Policies)
    {
        public int Declarations => RolePolicies.Count(Policies.Contains) + (HasAnyOrgMember ? 1 : 0);
    }

    private static bool IsUserSchemaAction(ActionEndpoint endpoint)
        => endpoint.Controller is not null
           && !endpoint.IsPublic
           && !endpoint.SkipsMembership
           && !CollaboratorSchemaControllers.Contains(endpoint.Controller);

    private static List<string> Violations(IEnumerable<ActionEndpoint> endpoints)
        => endpoints.Where(IsUserSchemaAction)
            .Where(e => e.Declarations != 1)
            .SelectMany(e => e.Routes)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static List<string> WithPolicy(IEnumerable<ActionEndpoint> endpoints, string policy)
        => endpoints.Where(IsUserSchemaAction)
            .Where(e => e.Policies.Contains(policy))
            .SelectMany(e => e.Routes)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<ActionEndpoint> EndpointsOf(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .Select(ToActionEndpoint)
            .ToList();

    private static ActionEndpoint ToActionEndpoint(RouteEndpoint endpoint)
    {
        var metadata = endpoint.Metadata;
        var authorize = metadata.GetOrderedMetadata<IAuthorizeData>();
        var methods = metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];

        return new ActionEndpoint(
            Controller: metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerTypeInfo.AsType(),
            Routes: methods.Select(method => $"{method} {endpoint.RoutePattern.RawText}").ToList(),
            IsPublic: authorize.Count == 0 || metadata.GetMetadata<IAllowAnonymous>() is not null,
            SkipsMembership: metadata.GetMetadata<SkipMembershipValidationAttribute>() is not null,
            HasAnyOrgMember: metadata.GetMetadata<AnyOrgMemberAttribute>() is not null,
            Policies: authorize.Select(a => a.Policy).OfType<string>().ToHashSet(StringComparer.Ordinal));
    }

    // ── Acciones de prueba ──────────────────────────────────────────────────────────────────────────────────────

    private const string ProbeBase = "api/v1/authorization-catalog-probe";

    private static WebApplicationFactory<Program> WithProbes(ZentoryApiFactory host)
        => host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().ConfigureApplicationPartManager(manager =>
                manager.FeatureProviders.Add(new ProbeControllerProvider()))));

    // Anidadas a propósito: MVC solo descubre controladores públicos de primer nivel, así que solo existen en el host al
    // que ProbeControllerProvider las agrega.
    [ApiController]
    [Authorize]
    [Route(ProbeBase)]
    public sealed class CatalogProbeController : ControllerBase
    {
        // Solo el [Authorize] de la clase: la omisión que el catálogo debe detectar.
        [HttpGet("class-authorize-only")]
        public IActionResult ClassAuthorizeOnly() => Ok();

        // Una policy de plan no declara rol.
        [Authorize(Policy = PolicyNames.EmpresaStudio)]
        [HttpGet("plan-policy-only")]
        public IActionResult PlanPolicyOnly() => Ok();

        // Dos declaraciones contradictorias: debe ser exactamente una.
        [AnyOrgMember]
        [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
        [HttpGet("both-any-member-and-admin")]
        public IActionResult BothAnyMemberAndAdmin() => Ok();

        [AnyOrgMember]
        [Authorize(Policy = PolicyNames.EmpresaStudio)]
        [HttpGet("any-member-with-plan")]
        public IActionResult AnyMemberWithPlan() => Ok();

        [Authorize(Policy = PolicyNames.RequireOrgOwner)]
        [HttpGet("org-owner")]
        public IActionResult OrgOwner() => Ok();

        [AllowAnonymous]
        [HttpGet("anonymous")]
        public IActionResult Anonymous() => Ok();
    }

    // Una policy de rol a nivel de clase cuenta para todas sus acciones (D-43).
    [ApiController]
    [Authorize(Policy = PolicyNames.RequireFinancialAccess)]
    [Route(ProbeBase + "/class-policy")]
    public sealed class ClassPolicyProbeController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    private sealed class ProbeControllerProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var probe in new[] { typeof(CatalogProbeController), typeof(ClassPolicyProbeController) })
            {
                var info = probe.GetTypeInfo();
                if (!feature.Controllers.Contains(info))
                    feature.Controllers.Add(info);
            }
        }
    }
}
