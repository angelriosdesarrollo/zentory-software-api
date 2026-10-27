using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
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
using Zentory.API.Controllers.V1;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;

namespace Zentory.Tests.Security.Idor;

// Catálogo IDOR (PLAN-0018 RF-13; ADR-0001 §6; CA-17, CA-18; cierre de CA-2 y CA-18 en T31). Toda ruta autenticada con un parámetro de recurso
// está en el catálogo (un test marcado con [IdorCase], en esta clase o en cualquier otra de la suite) o en Exceptions,
// con el plan que la cubrirá. Una ruta nueva sin caso ni excepción rompe la suite.
//
// Los casos de esta clase son los de CA-17: listado (GET /clients), agregado (GET /stats/invoices) y acciones y lecturas
// por id sobre facturas y clientes de B, todos con el token de A y pasando por los filtros de tenant de la API.
public sealed partial class IdorRouteCatalogTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    // ── CA-17: listado, agregado y acción ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA17_IdorAssert_ListadoClientesConTokenDeA_NoIncluyeClientesDeB()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var clientOfA = NewClient(tenants.A, "A");
        var clientOfB = NewClient(tenants.B, "B");
        await tenants.SeedAsync(clientOfA, clientOfB);
        using var http = tenants.ClientFor(tenants.A);

        var body = await IdorAssert.ListExcludesAsync(http, "/api/v1/clients", [clientOfB.Id],
            clientOfB.Name, clientOfB.Nit!, clientOfB.Email!);

        Assert.Contains(clientOfA.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PLAN0018_CA17_IdorAssert_AgregadoStatsInvoicesConTokenDeA_FacturasDeB_NoLoCambian()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var clientOfA = NewClient(tenants.A, "A");
        var clientOfB = NewClient(tenants.B, "B");
        await tenants.SeedAsync(clientOfA, clientOfB, NewInvoice(tenants.A, clientOfA, 100m));
        using var http = tenants.ClientFor(tenants.A);

        var unaffected = await IdorAssert.AggregateUnaffectedAsync(http, "/api/v1/stats/invoices",
            () => tenants.SeedAsync(NewInvoice(tenants.B, clientOfB, 7_000m), NewInvoice(tenants.B, clientOfB, 9_000m)));

        // Control positivo: el agregado sí refleja una factura nueva de A, así que no es constante.
        await tenants.SeedAsync(NewInvoice(tenants.A, clientOfA, 50m));
        Assert.NotEqual(unaffected, await IdorAssert.GetOkAsync(http, "/api/v1/stats/invoices"));
    }

    [Fact]
    [IdorCase("POST", "api/v1/invoices/{id:guid}/send")]
    public async Task PLAN0018_CA17_IdorAssert_SendFacturaDeBConTokenDeA_Responde404SinCambiarla()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var clientOfA = NewClient(tenants.A, "A");
        var clientOfB = NewClient(tenants.B, "B");
        var invoiceOfA = NewInvoice(tenants.A, clientOfA, 100m);
        var invoiceOfB = NewInvoice(tenants.B, clientOfB, 100m);
        await tenants.SeedAsync(clientOfA, clientOfB, invoiceOfA, invoiceOfB);
        using var http = tenants.ClientFor(tenants.A);

        var status = await IdorAssert.CannotAccessAsync<Invoice>(factory, http,
            new HttpRequestMessage(HttpMethod.Post, $"/api/v1/invoices/{invoiceOfB.Id}/send"), invoiceOfB.Id);

        Assert.Equal(HttpStatusCode.NotFound, status);
        // Control positivo: la misma acción sobre la factura propia sí surte efecto.
        using var own = await http.PostAsync($"/api/v1/invoices/{invoiceOfA.Id}/send", null);
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
    }

    [Theory]
    [IdorCase("GET",    "api/v1/invoices/{id:guid}")]
    [IdorCase("POST",   "api/v1/invoices/{id:guid}/payments")]
    [IdorCase("DELETE", "api/v1/invoices/{id:guid}")]
    [InlineData("GET",    "api/v1/invoices/{id:guid}",          null)]
    [InlineData("POST",   "api/v1/invoices/{id:guid}/payments", """{"amount":10}""")]
    [InlineData("DELETE", "api/v1/invoices/{id:guid}",          null)]
    public async Task PLAN0018_CA17_IdorAssert_FacturaDeBPorIdConTokenDeA_Responde404SinEfecto(
        string method, string template, string? body)
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var clientOfB = NewClient(tenants.B, "B");
        var invoiceOfB = NewInvoice(tenants.B, clientOfB, 100m);
        await tenants.SeedAsync(clientOfB, invoiceOfB);
        using var http = tenants.ClientFor(tenants.A);

        var status = await IdorAssert.CannotAccessAsync<Invoice>(factory, http,
            Request(method, template, invoiceOfB.Id, body), invoiceOfB.Id);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Theory]
    [IdorCase("GET",    "api/v1/clients/{id:guid}")]
    [IdorCase("PATCH",  "api/v1/clients/{id:guid}")]
    [IdorCase("DELETE", "api/v1/clients/{id:guid}")]
    [InlineData("GET",    "api/v1/clients/{id:guid}", null)]
    [InlineData("PATCH",  "api/v1/clients/{id:guid}", """{"name":"Cambiado por A","contactName":"A"}""")]
    [InlineData("DELETE", "api/v1/clients/{id:guid}", null)]
    public async Task PLAN0018_CA17_IdorAssert_ClienteDeBPorIdConTokenDeA_Responde404SinEfecto(
        string method, string template, string? body)
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var clientOfB = NewClient(tenants.B, "B");
        await tenants.SeedAsync(clientOfB);
        using var http = tenants.ClientFor(tenants.A);

        var status = await IdorAssert.CannotAccessAsync<Client>(factory, http,
            Request(method, template, clientOfB.Id, body), clientOfB.Id);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    // ── CA-18 (parte de T6): catálogo y excepciones ─────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA18_RutasAutenticadasConParametro_EstanEnElCatalogoOEnLasExcepciones()
    {
        var uncovered = UncoveredRoutes(ActionsOf(factory), CatalogRoutes(), Exceptions);

        Assert.True(uncovered.Count == 0,
            "Rutas autenticadas con parámetro de recurso sin caso IDOR ([IdorCase]) ni excepción: " +
            string.Join(", ", uncovered));
    }

    [Fact]
    public void PLAN0018_CA18_AccionDePruebaSinCatalogoNiExcepcion_LaSuiteFalla()
    {
        using var withProbe = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().ConfigureApplicationPartManager(manager =>
                manager.FeatureProviders.Add(new ProbeControllerProvider()))));
        var actions = ActionsOf(withProbe);

        var uncovered = UncoveredRoutes(actions, CatalogRoutes(), Exceptions);
        Assert.Equal([ProbeRoute], uncovered);

        // Con un caso o una excepción para la ruta nueva, deja de fallar.
        Assert.Empty(UncoveredRoutes(actions, CatalogRoutes().Append(ProbeRoute).ToHashSet(), Exceptions));
        var excepted = new Dictionary<string, IdorException>(Exceptions)
        {
            [ProbeRoute] = new("PLAN-0018", "Acción de prueba."),
        };
        Assert.Empty(UncoveredRoutes(actions, CatalogRoutes(), excepted));
    }

    [Fact]
    public void PLAN0018_CA18_CadaExcepcion_NombraElPlanQueLaCubriraYSuMotivo()
    {
        var invalid = Exceptions
            .Where(entry => !PlanId().IsMatch(entry.Value.Plan) || string.IsNullOrWhiteSpace(entry.Value.Reason))
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(invalid.Count == 0, "Excepciones sin plan (PLAN-nnnn) o sin motivo: " + string.Join(", ", invalid));
    }

    [Fact]
    public void PLAN0018_CA18_CatalogoYExcepciones_SoloNombranRutasAutenticadasExistentesSinSolaparse()
    {
        var routes = AuthenticatedResourceRoutes(ActionsOf(factory)).ToHashSet();
        var authenticated = AuthenticatedRoutes(ActionsOf(factory)).ToHashSet();
        var catalog = CatalogRoutes();

        // Un caso puede cubrir también una ruta autenticada sin parámetro (p. ej. un listado acotado al usuario); las
        // excepciones solo valen para rutas con parámetro de recurso.
        var staleCases = catalog.Where(route => !authenticated.Contains(route)).ToList();
        var staleExceptions = Exceptions.Keys.Where(route => !routes.Contains(route)).ToList();
        var overlapping = Exceptions.Keys.Where(catalog.Contains).ToList();

        Assert.True(staleCases.Count == 0, "[IdorCase] sobre rutas que no existen o no son autenticadas: " + string.Join(", ", staleCases));
        Assert.True(staleExceptions.Count == 0, "Excepciones sobre rutas que no existen o no son autenticadas: " + string.Join(", ", staleExceptions));
        Assert.True(overlapping.Count == 0, "Rutas con caso IDOR que siguen en las excepciones: " + string.Join(", ", overlapping));
    }

    [Fact]
    public void PLAN0018_CA18_Enumeracion_DistingueRutasAnonimasYSinParametro()
    {
        var routes = AuthenticatedResourceRoutes(ActionsOf(factory)).ToHashSet();

        // Control de la enumeración: incluye las rutas autenticadas de los dos esquemas y deja fuera las públicas
        // (AllowAnonymous o sin [Authorize]) y las que no tienen parámetro de recurso.
        Assert.Contains("POST api/v1/invoices/{id:guid}/send", routes);
        Assert.Contains("POST api/v1/collaborator-portal/payout-invoices/{id:guid}/sign", routes);
        Assert.DoesNotContain("GET api/v1/public/share/{token}", routes);
        Assert.DoesNotContain("GET api/v1/public/proposals/{token:guid}", routes);
        Assert.DoesNotContain("GET api/v1/clients", routes);
    }

    [Fact]
    public void PLAN0018_CA106_RestoreYListadoDeEliminadas_FiguranEnElCatalogoIdor()
    {
        // T11c (RF-72 d): restore tiene parámetro de recurso y queda vigilada por el recorrido; el listado no lo tiene,
        // pero lleva su caso (un admin de la organización eliminada no la ve) en el mismo catálogo.
        var catalog = CatalogRoutes();

        Assert.Contains(RestoreRoute, catalog);
        Assert.Contains(DeletedListRoute, catalog);
        Assert.Contains(RestoreRoute, AuthenticatedResourceRoutes(ActionsOf(factory)));
        Assert.DoesNotContain(RestoreRoute, Exceptions.Keys);
    }

    private const string RestoreRoute     = "POST api/v1/organizations/{id:guid}/restore";
    private const string DeletedListRoute = "GET api/v1/me/organizations/deleted";

    // ── CA-2 y CA-18 (cierre en T31): identidad y acceso ────────────────────────────────────────────────────────

    // Los seis controladores cuyas rutas CA-18 prohíbe en las excepciones al cerrar el plan.
    private static readonly Type[] ClosedControllers =
    [
        typeof(AuthController),
        typeof(ProfileController),
        typeof(OrganizationController),
        typeof(OrganizationsController),
        typeof(OrganizationMembersController),
        typeof(OrganizationRolesController),
    ];

    // Controladores de los cuatro módulos (MOD-0001, MOD-0002, MOD-0003, MOD-0019): los seis de CA-18 y el de la
    // aceptación de la transferencia por el destino (T30c).
    private static readonly Type[] IdentityAccessControllers = [.. ClosedControllers, typeof(OwnershipTransfersController)];

    // Endpoints de esos controladores que reciben o resuelven un recurso (CA-2): cada uno tiene su caso IDOR con
    // IdorAssert. Los que no tienen parámetro de ruta resuelven el recurso desde la organización activa o el usuario
    // (lecturas, escrituras de la organización activa, listados) o lo reciben en el cuerpo o la query (switch, preview,
    // proponer, invitar).
    private static readonly string[] IdentityAccessCatalog =
    [
        "DELETE api/v1/organization",
        "DELETE api/v1/organization/members/{userId:guid}",
        "DELETE api/v1/organization/transfer-ownership",
        "GET api/v1/me/organizations",
        "GET api/v1/me/organizations/deleted",
        "GET api/v1/me/ownership-transfers",
        "GET api/v1/organization/billing-history",
        "GET api/v1/organization/members",
        "GET api/v1/organization/plan",
        "GET api/v1/organization/plan-limits",
        "GET api/v1/organization/profile",
        "GET api/v1/organization/roles",
        "GET api/v1/organization/roles/permissions",
        "GET api/v1/organization/settings",
        "GET api/v1/organization/transfer-ownership/preview",
        "PATCH api/v1/organization/members/{userId:guid}/role",
        "PATCH api/v1/organization/profile",
        "PATCH api/v1/organization/settings",
        "POST api/v1/organization/members/invite",
        "POST api/v1/organization/switch",
        "POST api/v1/organization/transfer-ownership",
        "POST api/v1/organizations/{id:guid}/restore",
        "POST api/v1/ownership-transfers/{id:guid}/accept",
        "POST api/v1/ownership-transfers/{id:guid}/decline",
    ];

    // Rutas autenticadas de esos controladores que no reciben ni resuelven un recurso de una organización: solo actúan
    // sobre el usuario del token o crean algo nuevo suyo. No llevan caso IDOR; un endpoint nuevo tiene que entrar en el
    // catálogo o aquí, con su motivo.
    private static readonly IReadOnlyDictionary<string, string> IdentityAccessWithoutResource = new Dictionary<string, string>
    {
        ["GET api/v1/me"]                        = "Perfil del usuario del token (sub); no hay recurso de otra organización.",
        ["POST api/v1/organizations"]            = "Crea una organización nueva del que llama; no recibe ni resuelve una existente.",
        ["POST api/v1/auth/resend-verification"] = "Reenvía la verificación del email del usuario del token.",
        ["GET api/v1/auth/reauth-nonce"]         = "Emite un nonce ligado al usuario del token; la acción es un valor de ReauthAction.",
    };

    [Fact]
    public void PLAN0018_CA18_Cierre_LasExcepcionesNoTienenRutasDeAuthProfileOrganizationNiMiembrosNiRoles()
    {
        var byController = RoutesByController(ActionsOf(factory));

        var offending = Exceptions.Keys
            .Where(route => byController.TryGetValue(route, out var controllers) && controllers.Overlaps(ClosedControllers))
            .ToList();
        Assert.True(offending.Count == 0, "Excepciones IDOR de controladores cerrados por CA-18: " + string.Join(", ", offending));

        // Control: cada excepción se resuelve a su controlador, así que la comprobación anterior no pasa en vacío.
        Assert.All(Exceptions.Keys, route => Assert.True(byController.ContainsKey(route), route));
    }

    [Fact]
    public void PLAN0018_CA2_EndpointsConRecursoDeLosCuatroModulos_TienenCasoIdorYNoEstanEnLasExcepciones()
    {
        var catalog = CatalogRoutes();

        var missing = IdentityAccessCatalog.Where(route => !catalog.Contains(route)).ToList();
        var excepted = IdentityAccessCatalog.Where(Exceptions.ContainsKey).ToList();

        Assert.True(missing.Count == 0, "Endpoints de identidad y acceso sin caso IDOR ([IdorCase]): " + string.Join(", ", missing));
        Assert.True(excepted.Count == 0, "Endpoints de identidad y acceso en las excepciones: " + string.Join(", ", excepted));
    }

    [Fact]
    public void PLAN0018_CA2_RutasAutenticadasDeLosCuatroModulos_EstanEnElCatalogoOSonSinRecurso()
    {
        // El conjunto queda cerrado: toda ruta autenticada de esos controladores está en el catálogo de CA-2 o en la lista
        // sin recurso, y ninguna de las dos listas nombra rutas que no existen.
        var actions = ActionsOf(factory);
        var authenticated = AuthenticatedRoutes(actions).ToHashSet(StringComparer.Ordinal);
        var routes = RoutesByController(actions)
            .Where(entry => entry.Value.Overlaps(IdentityAccessControllers) && authenticated.Contains(entry.Key))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        var unclassified = routes
            .Where(route => !IdentityAccessCatalog.Contains(route) && !IdentityAccessWithoutResource.ContainsKey(route))
            .Order(StringComparer.Ordinal)
            .ToList();
        var stale = IdentityAccessCatalog.Concat(IdentityAccessWithoutResource.Keys).Where(route => !routes.Contains(route)).ToList();

        Assert.True(unclassified.Count == 0, "Rutas de identidad y acceso sin clasificar (caso IDOR o sin recurso): " + string.Join(", ", unclassified));
        Assert.True(stale.Count == 0, "Rutas de identidad y acceso que ya no existen o no son autenticadas: " + string.Join(", ", stale));
        Assert.Empty(IdentityAccessCatalog.Intersect(IdentityAccessWithoutResource.Keys));
    }

    // ── Excepciones ─────────────────────────────────────────────────────────────────────────────────────────────

    public sealed record IdorException(string Plan, string Reason);

    private const string CommercialPlan    = "PLAN-0019"; // facturación, propuestas y finanzas (MOD-0006, 0009, 0011)
    private const string CollaboratorsPlan = "PLAN-0020"; // PILA, cuentas de cobro y portal (MOD-0013, 0014)
    private const string ConsolidationPlan = "PLAN-0021"; // IDOR de MOD-0005, 0007, 0008, 0012, 0015, 0016, 0018

    private const string ProposalsReason     = "Propuestas (MOD-0006): su caso IDOR llega con el cierre del módulo.";
    private const string FinanceReason       = "Finanzas y rentabilidad (MOD-0011): su caso IDOR llega con el cierre del módulo.";
    private const string PilaReason          = "Seguridad social, PILA y cuentas de cobro (MOD-0013).";
    private const string PortalReason        = "Portal de colaboradores (MOD-0014), esquema CollaboratorScheme.";
    private const string ProjectsReason      = "Proyectos y sus subrecursos (MOD-0007): IDOR en la consolidación.";
    private const string TimeReason          = "Time tracking (MOD-0008): IDOR en la consolidación.";
    private const string CollaboratorsReason = "Colaboradores (MOD-0012): IDOR en la consolidación.";
    private const string AuditReason         = "Auditoría (MOD-0015): IDOR en la consolidación.";
    private const string IntegrationsReason  = "Integraciones (MOD-0016): {id} es del catálogo global; la conexión se busca por la organización del token.";

    // Rutas autenticadas con parámetro de recurso que aún no tienen caso IDOR, cada una con el plan que lo agregará.
    // Al cerrar PLAN-0018 no queda ninguna de Auth, Profile, Organization*, OrganizationMembers ni OrganizationRoles.
    public static readonly IReadOnlyDictionary<string, IdorException> Exceptions = new Dictionary<string, IdorException>
    {
        // PLAN-0019
        ["PATCH api/v1/cash-flow-entries/{id:guid}"]                                    = new(CommercialPlan, FinanceReason),
        ["DELETE api/v1/cash-flow-entries/{id:guid}"]                                   = new(CommercialPlan, FinanceReason),
        ["PATCH api/v1/expenses/{id:guid}"]                                             = new(CommercialPlan, FinanceReason),
        ["GET api/v1/projects/{projectId:guid}/billing"]                                = new(CommercialPlan, FinanceReason),
        ["GET api/v1/projects/{projectId:guid}/expenses"]                               = new(CommercialPlan, FinanceReason),
        ["POST api/v1/projects/{projectId:guid}/expenses"]                              = new(CommercialPlan, FinanceReason),
        ["GET api/v1/proposals/{id:guid}"]                                              = new(CommercialPlan, ProposalsReason),
        ["PATCH api/v1/proposals/{id:guid}"]                                            = new(CommercialPlan, ProposalsReason),
        ["PUT api/v1/proposals/{id:guid}/sections"]                                     = new(CommercialPlan, ProposalsReason),
        ["PUT api/v1/proposals/{id:guid}/items"]                                        = new(CommercialPlan, ProposalsReason),
        ["POST api/v1/proposals/{id:guid}/sections/{sectionType}/enrich"]               = new(CommercialPlan, ProposalsReason),
        ["POST api/v1/proposals/{id:guid}/send"]                                        = new(CommercialPlan, ProposalsReason),
        ["PATCH api/v1/proposals/{id:guid}/status"]                                     = new(CommercialPlan, ProposalsReason),
        ["POST api/v1/proposals/{id:guid}/convert-to-project"]                          = new(CommercialPlan, ProposalsReason),
        ["DELETE api/v1/proposals/{id:guid}"]                                           = new(CommercialPlan, ProposalsReason),

        // PLAN-0020
        ["GET api/v1/collaborator-portal/pila/{id:guid}/download-url"]                  = new(CollaboratorsPlan, PortalReason),
        ["GET api/v1/collaborator-portal/payout-invoices/{id:guid}/download-url"]       = new(CollaboratorsPlan, PortalReason),
        ["POST api/v1/collaborator-portal/payout-invoices/{id:guid}/sign"]              = new(CollaboratorsPlan, PortalReason),
        ["POST api/v1/collaborator-portal/payout-invoices/{id:guid}/dispute"]           = new(CollaboratorsPlan, PortalReason),
        ["POST api/v1/team/payout-invoices/{id:guid}/send"]                             = new(CollaboratorsPlan, PilaReason),
        ["GET api/v1/team/payout-invoices/{id:guid}/download-url"]                      = new(CollaboratorsPlan, PilaReason),
        ["PATCH api/v1/team/payout-invoices/{id:guid}/review"]                          = new(CollaboratorsPlan, PilaReason),
        ["GET api/v1/team/payout-invoices/{id:guid}/project-breakdown"]                 = new(CollaboratorsPlan, PilaReason),
        ["GET api/v1/team/pila-verifications/{collaboratorId:guid}/history"]            = new(CollaboratorsPlan, PilaReason),
        ["PATCH api/v1/team/pila-verifications/{id:guid}/verify"]                       = new(CollaboratorsPlan, PilaReason),
        ["GET api/v1/team/pila-verifications/{id:guid}/download-url"]                   = new(CollaboratorsPlan, PilaReason),
        ["GET api/v1/team/pila-verifications/{id:guid}/documents"]                      = new(CollaboratorsPlan, PilaReason),
        ["GET api/v1/team/pila-verifications/documents/{documentId:guid}/download-url"] = new(CollaboratorsPlan, PilaReason),
        ["PATCH api/v1/me/ss-calculations/{id:guid}/mark-filed"]                        = new(CollaboratorsPlan, PilaReason),
        ["GET api/v1/me/ss-calculations/{id:guid}/download-url"]                        = new(CollaboratorsPlan, PilaReason),

        // PLAN-0021
        ["GET api/v1/collaborators/{id:guid}"]                                          = new(ConsolidationPlan, CollaboratorsReason),
        ["PATCH api/v1/collaborators/{id:guid}"]                                        = new(ConsolidationPlan, CollaboratorsReason),
        ["DELETE api/v1/collaborators/{id:guid}"]                                       = new(ConsolidationPlan, CollaboratorsReason),
        ["POST api/v1/integrations/{id}/connect"]                                       = new(ConsolidationPlan, IntegrationsReason),
        ["DELETE api/v1/integrations/{id}/connect"]                                     = new(ConsolidationPlan, IntegrationsReason),
        ["GET api/v1/proposals/{proposalId:guid}/activity-log"]                         = new(ConsolidationPlan, AuditReason),
        ["GET api/v1/projects/{projectId:guid}/log"]                                    = new(ConsolidationPlan, AuditReason),
        ["GET api/v1/projects/{id:guid}"]                                               = new(ConsolidationPlan, ProjectsReason),
        ["PATCH api/v1/projects/{id:guid}"]                                             = new(ConsolidationPlan, ProjectsReason),
        ["PATCH api/v1/projects/{id:guid}/status"]                                      = new(ConsolidationPlan, ProjectsReason),
        ["DELETE api/v1/projects/{id:guid}"]                                            = new(ConsolidationPlan, ProjectsReason),
        ["GET api/v1/projects/{projectId:guid}/deliverables"]                           = new(ConsolidationPlan, ProjectsReason),
        ["POST api/v1/projects/{projectId:guid}/deliverables"]                          = new(ConsolidationPlan, ProjectsReason),
        ["PATCH api/v1/projects/{projectId:guid}/deliverables/{deliverableId:guid}/status"] = new(ConsolidationPlan, ProjectsReason),
        ["GET api/v1/projects/{projectId:guid}/files"]                                  = new(ConsolidationPlan, ProjectsReason),
        ["POST api/v1/projects/{projectId:guid}/files/upload-url"]                      = new(ConsolidationPlan, ProjectsReason),
        ["POST api/v1/projects/{projectId:guid}/files"]                                 = new(ConsolidationPlan, ProjectsReason),
        ["GET api/v1/projects/{projectId:guid}/files/{fileId:guid}/download-url"]       = new(ConsolidationPlan, ProjectsReason),
        ["GET api/v1/projects/{projectId:guid}/milestones"]                             = new(ConsolidationPlan, ProjectsReason),
        ["POST api/v1/projects/{projectId:guid}/milestones"]                            = new(ConsolidationPlan, ProjectsReason),
        ["PATCH api/v1/projects/{projectId:guid}/milestones/{milestoneId:guid}/status"] = new(ConsolidationPlan, ProjectsReason),
        ["GET api/v1/projects/{projectId:guid}/share"]                                  = new(ConsolidationPlan, ProjectsReason),
        ["POST api/v1/projects/{projectId:guid}/share"]                                 = new(ConsolidationPlan, ProjectsReason),
        ["DELETE api/v1/projects/{projectId:guid}/share/{shareId:guid}"]                = new(ConsolidationPlan, ProjectsReason),
        ["GET api/v1/projects/{projectId:guid}/tasks"]                                  = new(ConsolidationPlan, ProjectsReason),
        ["POST api/v1/projects/{projectId:guid}/tasks"]                                 = new(ConsolidationPlan, ProjectsReason),
        ["PATCH api/v1/projects/{projectId:guid}/tasks/{taskId:guid}"]                  = new(ConsolidationPlan, ProjectsReason),
        ["PATCH api/v1/projects/{projectId:guid}/tasks/{taskId:guid}/move"]             = new(ConsolidationPlan, ProjectsReason),
        ["PATCH api/v1/projects/{projectId:guid}/tasks/{taskId:guid}/gantt"]            = new(ConsolidationPlan, ProjectsReason),
        ["DELETE api/v1/projects/{projectId:guid}/tasks/{taskId:guid}"]                 = new(ConsolidationPlan, ProjectsReason),
        ["GET api/v1/time-entries/{id:guid}"]                                           = new(ConsolidationPlan, TimeReason),
        ["PATCH api/v1/time-entries/{id:guid}"]                                         = new(ConsolidationPlan, TimeReason),
        ["POST api/v1/time-entries/{id:guid}/approve"]                                  = new(ConsolidationPlan, TimeReason),
        ["DELETE api/v1/time-entries/{id:guid}"]                                        = new(ConsolidationPlan, TimeReason),
    };

    // ── Enumeración de rutas ────────────────────────────────────────────────────────────────────────────────────

    private const string ProbeRoute = "GET api/v1/idor-probe/{id:guid}";

    // "MÉTODO plantilla" de cada acción autenticada (con [Authorize] en el controlador o la acción y sin
    // AllowAnonymous) cuya plantilla tiene un parámetro de ruta.
    private static IEnumerable<string> AuthenticatedResourceRoutes(IEnumerable<ActionDescriptor> actions)
        => actions
            .Where(action => action.AttributeRouteInfo?.Template?.Contains('{') == true)
            .Where(action => action.EndpointMetadata.OfType<IAuthorizeData>().Any()
                          && !action.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            .SelectMany(action => (action.ActionConstraints ?? [])
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods)
                .Select(method => $"{method} {action.AttributeRouteInfo!.Template}"))
            .Distinct()
            .Order(StringComparer.Ordinal);

    // "MÉTODO plantilla" de cada acción autenticada, con o sin parámetro de ruta.
    private static IEnumerable<string> AuthenticatedRoutes(IEnumerable<ActionDescriptor> actions)
        => actions
            .Where(action => action.AttributeRouteInfo?.Template is not null)
            .Where(action => action.EndpointMetadata.OfType<IAuthorizeData>().Any()
                          && !action.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            .SelectMany(action => (action.ActionConstraints ?? [])
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods)
                .Select(method => $"{method} {action.AttributeRouteInfo!.Template}"))
            .Distinct();

    private static List<string> UncoveredRoutes(
        IEnumerable<ActionDescriptor>                    actions,
        IReadOnlySet<string>                             catalog,
        IReadOnlyDictionary<string, IdorException>       exceptions)
        => AuthenticatedResourceRoutes(actions)
            .Where(route => !catalog.Contains(route) && !exceptions.ContainsKey(route))
            .ToList();

    // El catálogo son los tests marcados con [IdorCase] en toda la suite.
    private static HashSet<string> CatalogRoutes()
        => typeof(IdorRouteCatalogTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<FactAttribute>().Any())
            .SelectMany(method => method.GetCustomAttributes<IdorCaseAttribute>())
            .Select(attribute => attribute.Route)
            .ToHashSet(StringComparer.Ordinal);

    // "MÉTODO plantilla" → controladores que la declaran (normalmente uno).
    private static Dictionary<string, HashSet<Type>> RoutesByController(IEnumerable<ActionDescriptor> actions)
        => actions
            .OfType<ControllerActionDescriptor>()
            .Where(action => action.AttributeRouteInfo?.Template is not null)
            .SelectMany(action => (action.ActionConstraints ?? [])
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods)
                .Select(method => (Route: $"{method} {action.AttributeRouteInfo!.Template}",
                                   Controller: action.ControllerTypeInfo.AsType())))
            .GroupBy(item => item.Route, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Controller).ToHashSet(), StringComparer.Ordinal);

    private static IReadOnlyList<ActionDescriptor> ActionsOf(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;

    [GeneratedRegex(@"^PLAN-\d{4}$")]
    private static partial Regex PlanId();

    // ── Apoyo de los casos ──────────────────────────────────────────────────────────────────────────────────────

    private static Client NewClient(SeededTenant tenant, string label)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return Client.Create(tenant.OrganizationId, $"Cliente {label} {suffix}", $"Contacto {label}",
            email: $"cliente-{suffix}@zentory.test", nit: $"9{suffix[..9]}");
    }

    private static Invoice NewInvoice(SeededTenant tenant, Client client, decimal unitPrice)
    {
        var invoice = Invoice.Create(tenant.OrganizationId, client.Id, $"F-{Guid.NewGuid():N}",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        invoice.AddItem(InvoiceItem.Create(tenant.OrganizationId, invoice.Id, "Servicio", 1m, unitPrice));
        return invoice;
    }

    private static HttpRequestMessage Request(string method, string template, Guid id, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/" + RouteParameter().Replace(template, id.ToString()));
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();

    // ── Acción de prueba ────────────────────────────────────────────────────────────────────────────────────────

    // Anidada a propósito: MVC solo descubre controladores públicos de primer nivel, así que solo existe en el host
    // al que ProbeControllerProvider la agrega.
    [ApiController]
    [Authorize]
    [Route("api/v1/idor-probe")]
    public sealed class IdorProbeController : ControllerBase
    {
        [HttpGet("{id:guid}")]
        public IActionResult Get(Guid id) => Ok(id);
    }

    private sealed class ProbeControllerProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var probe = typeof(IdorProbeController).GetTypeInfo();
            if (!feature.Controllers.Contains(probe))
                feature.Controllers.Add(probe);
        }
    }
}
