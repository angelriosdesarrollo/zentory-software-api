using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zentory.API.Cli;
using Zentory.API.Controllers.V1;
using Zentory.Application.Common.Security;
using Zentory.Application.Projects;
using Zentory.Domain.Common;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding;
using Zentory.Infrastructure.Persistence.Seeding.Demo;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Seeding;

// PLAN-0018 (RF-56, RF-57, RF-6, RF-68; D-5, D-35, D-37): los seis escenarios demo de §3.0.A (Agencia, marca freelance
// de la persona principal, Constructora, Consultora, freelance «otro» y freelance nueva) y la parte 4 (invitado con una
// invitación pendiente a la Agencia). Cada test siembra su propia base
// InMemory (catálogos y escenarios) con un reloj fijo. Las contraseñas se generan en cada corrida y se comparan sin
// imprimirlas. La idempotencia de catálogos y escenarios juntos (CA-12) está en SeedIdempotencyTests.
public sealed class DemoScenariosTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 10, 24, 14, 30, 0, TimeSpan.Zero);

    // IDs nuevos de T4c: ninguno existía antes de PLAN-0018 (a1…0005, Pulso Digital, se retira y no se reutiliza).
    private static readonly Guid[] NewOrganizations = [FreelanceOtherScenario.OrganizationId, FreelanceNewScenario.OrganizationId];

    // ── CA-69 (RF-56 g): identificadores conservados ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA69_Escenarios_AgenciaYPersonaPrincipal_ConservanSusIdsDeDesarrollo()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var agency = await db.Organizations.SingleAsync(o => o.OrganizationId == new Guid("a1000000-0000-0000-0000-000000000001"));
        var principal = await db.Users.SingleAsync(u => u.UserId == new Guid("b2000000-0000-0000-0000-000000000002"));

        Assert.Equal(AgencyScenario.OrganizationName, agency.Name);
        Assert.Equal(principal.UserId, agency.OwnerId);
        Assert.Equal(DemoPeople.Principal.Email, principal.Email);
        Assert.Equal(AgencyScenario.OrganizationId, ReadPrivateStaticGuid(typeof(DevDataSeeder), "DevOrgId"));
        Assert.Equal(AgencyScenario.OrganizationId, ReadPrivateStaticGuid(typeof(DevToolsController), "DevOrgId"));
    }

    [Fact]
    public async Task PLAN0018_CA69_ProyectosQueLeeProjectExpenseStore_ExistenConSuIdEnLaAgencia()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var storeOrganization = ReadPrivateStaticGuid(typeof(ProjectExpenseStore), "DevOrg");
        var storeProjects = new[] { "PrjPortal", "PrjMobile", "PrjData" }
            .Select(field => ReadPrivateStaticGuid(typeof(ProjectExpenseStore), field))
            .Concat(new ProjectExpenseStore().GetAll(storeOrganization).Select(e => e.ProjectId).OfType<Guid>())
            .Distinct()
            .ToList();

        Assert.Equal(AgencyScenario.OrganizationId, storeOrganization);
        foreach (var projectId in storeProjects)
        {
            var project = await db.Projects.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.Id == projectId);
            Assert.True(project is not null, $"Falta el proyecto {projectId} que lee ProjectExpenseStore.");
            Assert.Equal(AgencyScenario.OrganizationId, project!.OrganizationId);
        }
    }

    [Fact]
    public async Task PLAN0018_CA69_DevToolsController_EncuentraEnLaAgenciaUnColaboradorActivoConCorreo()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        // Lo mismo que pide GetSeedCollaboratorAsync (no empleado y activo) más el correo del magic link.
        var collaborators = await db.Collaborators
            .Where(c => c.OrganizationId == AgencyScenario.OrganizationId && c.Status == "active" && c.Type != "employee")
            .ToListAsync();

        Assert.NotEmpty(collaborators);
        Assert.All(collaborators, c => Assert.False(string.IsNullOrWhiteSpace(c.Email)));
    }

    [Fact]
    public async Task PLAN0018_CA69_IdsReutilizados_ConservanElLegalTypeQueTenian()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        // legalType de cada ID antes de PLAN-0018 (DevDataSeeder retirado).
        var before = new Dictionary<Guid, string>
        {
            [new Guid("a1000000-0000-0000-0000-000000000001")] = LegalType.Empresa,
            [new Guid("a1000000-0000-0000-0000-000000000002")] = LegalType.Freelance,
            [new Guid("a1000000-0000-0000-0000-000000000003")] = LegalType.Empresa,
            [new Guid("a1000000-0000-0000-0000-000000000004")] = LegalType.Empresa,
        };
        var now = await db.Organizations.Where(o => before.Keys.Contains(o.OrganizationId))
            .ToDictionaryAsync(o => o.OrganizationId, o => o.LegalType);
        var retired = new Guid("a1000000-0000-0000-0000-000000000005");

        Assert.Equal(before, now);
        Assert.Equal(before.Keys.Order(), DemoScenarioOrganizations.All.Except(NewOrganizations).Order());
        Assert.DoesNotContain(retired, DemoScenarioOrganizations.All);
        Assert.False(await db.Organizations.AnyAsync(o => o.OrganizationId == retired), "Pulso Digital se retira de la demo.");
    }

    [Fact]
    public async Task PLAN0018_CA69_ProyectosDeLaConstructora_ConservanSusIdsEnEsaOrganizacion()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        Guid[] ids = [ConstructionScenario.AqueductProjectId, ConstructionScenario.RoadsProjectId, ConstructionScenario.HospitalProjectId];
        var organizations = await db.Projects.Where(p => ids.Contains(p.Id)).Select(p => p.OrganizationId).ToListAsync();

        Assert.Equal(ids.Length, organizations.Count);
        Assert.All(organizations, id => Assert.Equal(ConstructionScenario.OrganizationId, id));
    }

    // ── CA-71 (RF-57): contraseña de las personas demo ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA71_Escenarios_TodasLasPersonasDemo_UsanLaContrasenaDada()
    {
        var password = NewValidPassword();
        var databaseName = await SeedAsync(ReferenceNow, password);
        await using var db = NewContext(databaseName);

        var hashes = await db.Users.Select(u => u.PasswordHash).ToListAsync();

        // Persona principal, admin y member de la Agencia, los owners de Constructora, Consultora y las dos freelance y el
        // invitado sin organización (RF-68).
        Assert.Equal(8, hashes.Count);
        Assert.All(hashes, hash => Assert.True(hash is not null && BCrypt.Net.BCrypt.Verify(password, hash),
            "Una persona demo no tiene la contraseña de la corrida."));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(0)]
    public async Task PLAN0018_CA71_Runner_ConContrasenaFueraDePolitica_FallaAntesDeEscribirSinElValor(int length)
    {
        var password = NewValidPassword()[..length];
        var databaseName = NewDatabaseName();
        await using (var db = NewContext(databaseName))
        {
            await new ReferenceDataSeeder(db).SeedAsync();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new DemoScenarioRunner(DemoScenarioRunner.DefaultScenarios).RunAsync(db, new FakeTimeProvider(ReferenceNow), password));

            Assert.Contains(DemoDataSettings.PasswordKey, error.Message, StringComparison.Ordinal);
            if (length > 0)
                Assert.False(error.Message.Contains(password, StringComparison.Ordinal), "El mensaje contiene la contraseña.");
        }

        await using var check = NewContext(databaseName);
        Assert.False(await check.Organizations.AnyAsync(), "La siembra escribió organizaciones.");
        Assert.False(await check.Users.AnyAsync(), "La siembra escribió usuarios.");
    }

    // ── CA-68 parcial (CA-T4B-1): personas y membresías ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA68_PersonaPrincipal_TieneCuatroMembresias_OwnerEnAgenciaYMarcaAdminEnConstructoraMemberEnConsultora()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var memberships = await db.OrganizationMembers
            .Where(m => m.UserId == DemoPeople.Principal.Id)
            .Select(m => new { m.OrganizationId, m.Role })
            .ToListAsync();

        var expected = new[]
        {
            new { OrganizationId = AgencyScenario.OrganizationId,         Role = OrgRole.Owner },
            new { OrganizationId = FreelanceBrandScenario.OrganizationId, Role = OrgRole.Owner },
            new { OrganizationId = ConstructionScenario.OrganizationId,   Role = OrgRole.Admin },
            new { OrganizationId = ConsultancyScenario.OrganizationId,    Role = OrgRole.Member },
        };
        Assert.Equal(expected.OrderBy(m => m.OrganizationId), memberships.OrderBy(m => m.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA68_Agencia_TieneUnAdminYDosMembers_ElDeLaAgenciaYLaPersonaFree()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var roles = (await db.OrganizationMembers
                .Where(m => m.OrganizationId == AgencyScenario.OrganizationId)
                .ToListAsync())
            .GroupBy(m => m.Role)
            .ToDictionary(g => g.Key, g => g.Select(m => m.UserId).ToList());

        Assert.Equal(new[] { DemoPeople.Principal.Id }, roles[OrgRole.Owner]);
        Assert.Equal(new[] { DemoPeople.AgencyAdmin.Id }, roles[OrgRole.Admin]);
        Assert.Equal(new[] { DemoPeople.AgencyMember.Id, ConsultancyScenario.Owner.Id }.Order(), roles[OrgRole.Member].Order());
    }

    [Fact]
    public async Task PLAN0018_CA68_PersonaFree_EsOwnerDeExactamenteUnaOrganizacionYMemberEnLaAgencia()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);
        var free = ConsultancyScenario.Owner;

        var owned = await db.Organizations.Where(o => o.OwnerId == free.Id).Select(o => o.OrganizationId).ToListAsync();
        var memberships = await db.OrganizationMembers
            .Where(m => m.UserId == free.Id)
            .Select(m => new { m.OrganizationId, m.Role })
            .ToListAsync();

        Assert.Equal(new[] { ConsultancyScenario.OrganizationId }, owned);
        var expected = new[]
        {
            new { OrganizationId = AgencyScenario.OrganizationId,      Role = OrgRole.Member },
            new { OrganizationId = ConsultancyScenario.OrganizationId, Role = OrgRole.Owner },
        };
        Assert.Equal(expected.OrderBy(m => m.OrganizationId), memberships.OrderBy(m => m.OrganizationId));
        Assert.Equal(new Guid("b2000000-0000-0000-0000-000000000003"), free.Id);
        Assert.EndsWith("@" + DemoPeople.EmailDomain, (await db.Users.SingleAsync(u => u.UserId == free.Id)).Email);

        // Con plan free ya está en su máximo de organizaciones propias: crear otra queda bloqueado (RF-74).
        Assert.Equal(Plan.Free, await new PlanResolutionService(db).ResolveForOwnerAsync(free.Id));
        Assert.Equal(owned.Count, await LimitAsync(db, Plan.Free, LegalType.Empresa, PlanLimits.FeatureKeys.MaxOwnedOrgs));
    }

    [Fact]
    public async Task PLAN0018_CA68_Constructora_TieneOwnerPropioConPlanProYLaPersonaPrincipalVeStudioEnLasSuyas()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var owners = await db.Organizations.Where(o => DemoScenarioOrganizations.All.Contains(o.OrganizationId))
            .ToDictionaryAsync(o => o.OrganizationId, o => o.OwnerId);
        var plans = new PlanResolutionService(db);

        var constructionOwner = Assert.IsType<Guid>(owners[ConstructionScenario.OrganizationId]);
        Assert.NotEqual(DemoPeople.Principal.Id, constructionOwner);
        Assert.EndsWith("@" + DemoPeople.EmailDomain, (await db.Users.SingleAsync(u => u.UserId == constructionOwner)).Email);
        Assert.Equal(Plan.Pro, await plans.ResolveForOwnerAsync(constructionOwner));
        Assert.Equal(Plan.Studio, await plans.ResolveForOwnerAsync(owners[AgencyScenario.OrganizationId]));
        Assert.Equal(Plan.Studio, await plans.ResolveForOwnerAsync(owners[FreelanceBrandScenario.OrganizationId]));

        // RF-56 e: la suscripción de cada owner de pago es manual y está activa.
        var gateways = await db.BillingCustomers.Select(c => c.PaymentGateway).Distinct().ToListAsync();
        Assert.Equal(new[] { "manual" }, gateways);
    }

    [Fact]
    public async Task PLAN0018_CA68_ColaboradorDelPortal_ExisteConElMismoEmailEnDosOrganizaciones()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var organizations = await db.Collaborators
            .Where(c => c.Email == DemoPeople.PortalCollaboratorEmail)
            .Select(c => c.OrganizationId)
            .ToListAsync();

        Assert.Equal(
            new[] { AgencyScenario.OrganizationId, ConstructionScenario.OrganizationId }.Order(),
            organizations.Order());
    }

    [Fact]
    public async Task PLAN0018_CA68_EmailsSembrados_TerminanEnElDominioDemoSalvoLaPersonaPrincipal()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var emails = new List<string?>();
        emails.AddRange(await db.Users.Select(u => u.Email).ToListAsync());
        emails.AddRange(await db.Collaborators.IgnoreQueryFilters().Select(c => c.Email).ToListAsync());
        emails.AddRange(await db.Clients.IgnoreQueryFilters().Select(c => c.Email).ToListAsync());
        emails.AddRange(await db.ClientContacts.IgnoreQueryFilters().Select(c => c.Email).ToListAsync());
        emails.AddRange(await db.BillingCustomers.Select(c => c.Email).ToListAsync());
        emails.AddRange(await db.OrganizationInvitations.IgnoreQueryFilters().Select(i => i.Email).ToListAsync());

        var seeded = emails.Where(e => !string.IsNullOrEmpty(e)).Select(e => e!).ToList();
        var offending = seeded
            .Where(e => e != DemoPeople.Principal.Email && !e.EndsWith("@" + DemoPeople.EmailDomain, StringComparison.Ordinal))
            .ToList();

        Assert.True(seeded.Count > 20, "La demo debería sembrar correos de usuarios, colaboradores y clientes.");
        Assert.True(offending.Count == 0, "Correos fuera del dominio demo: " + string.Join(", ", offending));
    }

    // ── CA-68 (T15, RF-20): personas verificadas ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA68_PersonasSembradas_TienenEmailVerifiedAtEnUtcYEmailNormalizado()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var users = await db.Users.Select(u => new { u.UserId, u.Email, u.EmailVerifiedAt }).ToListAsync();

        Assert.True(users.Count >= 6, "La demo debería sembrar a todas las personas de los escenarios.");
        Assert.Contains(users, u => u.UserId == DemoPeople.Principal.Id);
        var unverified = users.Where(u => u.EmailVerifiedAt is null).Select(u => u.UserId).ToList();
        Assert.True(unverified.Count == 0, "Personas sin EmailVerifiedAt: " + string.Join(", ", unverified));

        // La fecha es relativa al día de referencia, en UTC (D-5), y el email está en su forma normalizada (RF-27).
        var expected = DateOnly.FromDateTime(ReferenceNow.UtcDateTime).AddDays(-365);
        Assert.All(users, u =>
        {
            Assert.Equal(DateTimeKind.Utc, u.EmailVerifiedAt!.Value.Kind);
            Assert.Equal(expected, DateOnly.FromDateTime(u.EmailVerifiedAt.Value));
            Assert.Equal(EmailAddress.Normalize(u.Email), u.Email);
        });
    }

    // ── CA-70 parcial (CA-T4B-2): fechas relativas al TimeProvider y en UTC ────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA70_DosFechasDeReferencia_ProyectosHorasYFacturasSeDesplazanIgual()
    {
        var first  = new DateTimeOffset(2026, 3, 10, 15, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
        var shiftDays = second.UtcDateTime.Date.Subtract(first.UtcDateTime.Date).Days;

        var early = await SnapshotDatesAsync(await SeedAsync(first));
        var late  = await SnapshotDatesAsync(await SeedAsync(second));

        Assert.Equal(early.Keys.Order(), late.Keys.Order());
        Assert.Contains(early.Keys, k => k.StartsWith("Project.", StringComparison.Ordinal));
        Assert.Contains(early.Keys, k => k.StartsWith("TimeEntry.", StringComparison.Ordinal));
        Assert.Contains(early.Keys, k => k.StartsWith("Invoice.", StringComparison.Ordinal));
        Assert.Contains(early.Keys, k => k.StartsWith("OrganizationInvitation.", StringComparison.Ordinal));
        var offending = early.Keys.Where(k => late[k].DayNumber - early[k].DayNumber != shiftDays).ToList();
        Assert.True(offending.Count == 0, "Fechas que no se desplazan con la referencia: " + string.Join(", ", offending));
    }

    [Fact]
    public async Task PLAN0018_CA70_FechasSembradas_TienenKindUtc()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var scope = DemoScenarioOrganizations.All.Concat(await db.Users.Select(u => u.UserId).ToListAsync()).ToHashSet();
        var (checkedValues, offending) = await NonUtcDateTimesAsync(db, scope);

        Assert.True(checkedValues > 200, $"Se revisaron muy pocas fechas ({checkedValues}).");
        Assert.True(offending.Count == 0, "Fechas sin Kind = Utc: " + string.Join(", ", offending.Distinct()));
    }

    [Fact]
    public async Task PLAN0018_CA70_Proyectos_ArrancanYTerminanRelativosAlDiaDeReferencia()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var portal = await db.Projects.SingleAsync(p => p.Id == AgencyScenario.PortalProjectId);
        var today  = ReferenceNow.UtcDateTime.Date;

        Assert.Equal(today.AddDays(-120), portal.StartDate);
        Assert.Equal(today.AddDays(90), portal.EndDate);
        Assert.Equal(DateTimeKind.Utc, portal.StartDate!.Value.Kind);
    }

    // ── RF-6 y D-5 (adelanto de CA-12, que cierra T4c): idempotencia ─────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA12_SegundaCorridaOtroDia_NoDuplicaFilasNiMueveFechas()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        var countsBefore = await CountScenarioRowsAsync(databaseName);
        var datesBefore  = await SnapshotDatesAsync(databaseName);

        await RunScenariosAsync(databaseName, ReferenceNow.AddDays(37), NewValidPassword());

        Assert.Equal(countsBefore, await CountScenarioRowsAsync(databaseName));
        Assert.Equal(datesBefore, await SnapshotDatesAsync(databaseName));
    }

    [Fact]
    public async Task PLAN0018_CA12_FilasExistentes_NoSeModificanEnLaSegundaCorrida()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        var edited = $"editado-{Guid.NewGuid():N}";
        await using (var db = NewContext(databaseName))
        {
            var project = await db.Projects.SingleAsync(p => p.Id == AgencyScenario.DataProjectId);
            db.Entry(project).Property(p => p.Name).CurrentValue = edited;
            var setting = await db.OrganizationSettings.SingleAsync(s =>
                s.OrganizationId == FreelanceBrandScenario.OrganizationId && s.Key == "profile.work_type");
            setting.Update(WorkType.Otro);
            await db.SaveChangesAsync();
        }

        await RunScenariosAsync(databaseName, ReferenceNow.AddDays(3), NewValidPassword());

        await using var check = NewContext(databaseName);
        Assert.Equal(edited, (await check.Projects.SingleAsync(p => p.Id == AgencyScenario.DataProjectId)).Name);
        Assert.Equal(WorkType.Otro, (await check.OrganizationSettings.SingleAsync(s =>
            s.OrganizationId == FreelanceBrandScenario.OrganizationId && s.Key == "profile.work_type")).Value);
    }

    // ── CA-67 (RF-56 b, c, e): seis organizaciones, planes, sectores y contenido ─────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA67_Escenarios_SonSeisOrganizacionesConElLegalTypeYPlanDeLaTabla_CubriendoLosSeisEstados()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        // §3.0.A: legalType · plan del owner.
        var expected = new Dictionary<Guid, (string LegalType, string Plan)>
        {
            [AgencyScenario.OrganizationId]         = (LegalType.Empresa,   Plan.Studio),
            [FreelanceBrandScenario.OrganizationId] = (LegalType.Freelance, Plan.Studio),
            [ConstructionScenario.OrganizationId]   = (LegalType.Empresa,   Plan.Pro),
            [ConsultancyScenario.OrganizationId]    = (LegalType.Empresa,   Plan.Free),
            [FreelanceOtherScenario.OrganizationId] = (LegalType.Freelance, Plan.Pro),
            [FreelanceNewScenario.OrganizationId]   = (LegalType.Freelance, Plan.Free),
        };
        var organizations = await db.Organizations.ToListAsync();
        var plans = new PlanResolutionService(db);
        var actual = new Dictionary<Guid, (string LegalType, string Plan)>();
        foreach (var organization in organizations)
            actual[organization.OrganizationId] = (organization.LegalType, await plans.ResolveForOwnerAsync(organization.OwnerId));

        Assert.Equal(6, organizations.Count);
        Assert.Equal(expected.OrderBy(e => e.Key), actual.OrderBy(a => a.Key));
        Assert.Equal(6, actual.Values.Distinct().Count());
        Assert.Equal(DemoScenarioOrganizations.All.Order(), actual.Keys.Order());
    }

    [Fact]
    public async Task PLAN0018_CA67_Escenarios_CadaOrganizacionTieneElWorkTypeDeLaTabla_YLaFreelanceNuevaNinguno()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var workTypes = await db.OrganizationSettings.Where(s => s.Key == "profile.work_type")
            .ToDictionaryAsync(s => s.OrganizationId, s => s.Value);

        var expected = new Dictionary<Guid, string?>
        {
            [AgencyScenario.OrganizationId]         = WorkType.Software,
            [FreelanceBrandScenario.OrganizationId] = WorkType.Marketing,
            [ConstructionScenario.OrganizationId]   = WorkType.ObraPublica,
            [ConsultancyScenario.OrganizationId]    = WorkType.Consultoria,
            [FreelanceOtherScenario.OrganizationId] = WorkType.Otro,
        };
        Assert.Equal(expected.OrderBy(e => e.Key), workTypes.OrderBy(w => w.Key));
        Assert.False(workTypes.ContainsKey(FreelanceNewScenario.OrganizationId), "La freelance nueva no debe tener profile.work_type.");
        // Sectores distintos: cada WorkType del catálogo aparece una vez.
        Assert.Equal(WorkType.All.Order(), workTypes.Values.Order());
    }

    [Fact]
    public async Task PLAN0018_CA67_Proyectos_CubrenLosTresBillingTypeTresOMasEstadosYMontosEnCopYUsd()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var projects  = await db.Projects.ToListAsync();
        var invoices  = await db.Invoices.ToListAsync();
        var proposals = await db.Proposals.ToListAsync();
        var hours     = await db.TimeEntries.Select(t => t.Status).Distinct().ToListAsync();

        Assert.Equal(Enum.GetValues<BillingType>().Order(), projects.Select(p => p.BillingType).Distinct().Order());
        Assert.True(projects.Select(p => p.Status).Distinct().Count() >= 3, "Faltan etapas de proyecto distintas.");
        Assert.Equal(Enum.GetValues<ProjectStatus>().Order(), projects.Select(p => p.Status).Distinct().Order());
        // Activo y atrasado: fecha de fin ya vencida.
        Assert.Contains(projects, p => p.Status == ProjectStatus.Active && p.EndDate < ReferenceNow.UtcDateTime);

        var currencies = projects.Select(p => p.Currency).Concat(invoices.Select(i => i.Currency))
            .Concat(proposals.Select(p => p.Currency)).Distinct().Order();
        Assert.Equal(new[] { "COP", "USD" }, currencies);
        Assert.Superset(new HashSet<string> { "draft", "sent", "paid", "overdue", "partial" }, invoices.Select(i => i.Status).ToHashSet());
        Assert.Superset(new HashSet<string> { "draft", "sent", "viewed", "accepted", "rejected", "expired" },
            proposals.Select(p => p.Status).ToHashSet());
        Assert.Superset(new HashSet<string> { "pending", "approved", "billed" }, hours.ToHashSet());
        Assert.Contains(projects, p => p.OrganizationId == AgencyScenario.OrganizationId && p.Type == WorkType.Marketing);
    }

    [Fact]
    public async Task PLAN0018_CA67_CadaOrganizacionSalvoLaNueva_TieneClientesProyectosYCadaEntidadHijaQueAplicaASuLegalType()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);

        var organizations = await db.Organizations.Where(o => o.OrganizationId != FreelanceNewScenario.OrganizationId).ToListAsync();
        var missing = new List<string>();
        foreach (var organization in organizations)
        {
            var present = await ChildEntitiesAsync(db, organization.OrganizationId, organization.OwnerId);
            missing.AddRange(ChildEntitiesFor(organization.LegalType)
                .Where(kind => !present[kind])
                .Select(kind => $"{organization.Name}: {kind}"));
        }

        Assert.Equal(5, organizations.Count);
        Assert.True(missing.Count == 0, "Entidades hijas que faltan: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task PLAN0018_CA67_FreelanceNueva_NoTieneClientesProyectosNiSuscripcion()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);
        var organizationId = FreelanceNewScenario.OrganizationId;
        var owner = FreelanceNewScenario.Owner;

        var present = await ChildEntitiesAsync(db, organizationId, owner.Id);

        Assert.True(present.Values.All(p => !p), "La freelance nueva tiene datos: " +
            string.Join(", ", present.Where(p => p.Value).Select(p => p.Key)));
        Assert.Equal(owner.Id, (await db.Organizations.SingleAsync(o => o.OrganizationId == organizationId)).OwnerId);
        Assert.Equal(new[] { OrgRole.Owner },
            await db.OrganizationMembers.Where(m => m.OrganizationId == organizationId).Select(m => m.Role).ToListAsync());
        Assert.False(await db.Subscriptions.AnyAsync(s => s.UserId == owner.Id), "La owner de la freelance nueva nunca tuvo suscripción.");
    }

    [Fact]
    public async Task PLAN0018_CA67_FreelanceOtro_TieneOwnerConIdConservadoPlanProYPilaDeLosUltimosPeriodos()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);
        var organizationId = FreelanceOtherScenario.OrganizationId;
        var owner = FreelanceOtherScenario.Owner;

        var organization = await db.Organizations.SingleAsync(o => o.OrganizationId == organizationId);
        var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == owner.Id);
        var projects = await db.Projects.Where(p => p.OrganizationId == organizationId).ToListAsync();
        var pila = await db.SsCalculationLogs.Where(s => s.OrganizationId == organizationId).ToListAsync();

        Assert.Equal(new Guid("b2000000-0000-0000-0000-000000000004"), owner.Id);
        Assert.Equal(owner.Id, organization.OwnerId);
        Assert.EndsWith("@" + DemoPeople.EmailDomain, (await db.Users.SingleAsync(u => u.UserId == owner.Id)).Email);
        Assert.Equal("active", subscription.Status);
        Assert.Equal(Plan.Pro, await new PlanResolutionService(db).ResolveForOwnerAsync(owner.Id));

        // Proyectos cortos de precio fijo (semanas), con uno en USD.
        Assert.All(projects, p => Assert.Equal(BillingType.FixedPrice, p.BillingType));
        Assert.All(projects, p => Assert.True((p.EndDate!.Value - p.StartDate!.Value).TotalDays <= 31, $"{p.Name} no es corto."));
        Assert.Contains(projects, p => p.Currency == "USD");

        // PILA propia: tres periodos distintos, los cerrados radicados y el del mes en curso solo calculado.
        Assert.Equal(3, pila.Select(p => p.Period).Distinct().Count());
        Assert.All(pila, p => Assert.Null(p.CollaboratorId));
        Assert.All(pila, p => Assert.True(p.TotalContribution > 0));
        Assert.Contains(pila, p => p.Period == ReferenceNow.UtcDateTime.ToString("yyyy-MM") && p.FiledAt is null);
        Assert.Equal(2, pila.Count(p => p.FiledAt is not null));
        Assert.False(await db.Collaborators.AnyAsync(c => c.OrganizationId == organizationId), "La freelance «otro» trabaja sola.");
    }

    // ── RF-56 e y RF-75 (decisión 6): la Consultora bajó de pro y quedó por encima de Free ───────────────────

    [Fact]
    public async Task PLAN0018_CA67_Consultora_BajoDeProAFreeYQuedoPorEncimaDeLosLimitesDeFree()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);
        var organizationId = ConsultancyScenario.OrganizationId;
        var owner = ConsultancyScenario.Owner;

        var subscriptions = await db.Subscriptions.Where(s => s.UserId == owner.Id).ToListAsync();
        var proPlanId = await db.BillingPlans.Where(p => p.Name == Plan.Pro).Select(p => p.Id).SingleAsync();
        var clients = await db.Clients.CountAsync(c => c.OrganizationId == organizationId);
        var members = await db.OrganizationMembers.CountAsync(m => m.OrganizationId == organizationId);
        var invoices = await db.Invoices.Where(i => i.OrganizationId == organizationId).ToListAsync();
        var monthStart = new DateTime(ReferenceNow.Year, ReferenceNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var invoicesThisMonth = invoices.Count(i => i.CreatedAt >= monthStart && i.CreatedAt < monthStart.AddMonths(1));

        // Tuvo pro, hoy cancelada con el motivo de la bajada, y ninguna activa: el plan resuelto es free.
        var previous = Assert.Single(subscriptions);
        Assert.Equal(proPlanId, previous.PlanId);
        Assert.Equal("cancelled", previous.Status);
        Assert.Equal(DemoSeedContext.DowngradedBySeedReason, previous.CancellationReason);
        Assert.Equal(DateTimeKind.Utc, previous.CancelledAt!.Value.Kind);
        Assert.Equal(Plan.Free, await new PlanResolutionService(db).ResolveForOwnerAsync(owner.Id));

        // Por encima de cada límite de Free (empresa), leído del catálogo.
        Assert.Equal(3, clients);
        Assert.Equal(2, members);
        Assert.Equal(4, invoicesThisMonth);
        Assert.True(clients > await LimitAsync(db, Plan.Free, LegalType.Empresa, PlanLimits.FeatureKeys.MaxClients));
        Assert.True(members > await LimitAsync(db, Plan.Free, LegalType.Empresa, PlanLimits.FeatureKeys.MaxOrgMembers));
        Assert.True(invoicesThisMonth > await LimitAsync(db, Plan.Free, LegalType.Empresa, PlanLimits.FeatureKeys.MaxInvoicesMonth));
        // Dato de una función de pago conservado: una factura en USD (multimoneda de pro).
        Assert.Single(invoices, i => i.Currency == "USD");
    }

    // ── CA-70 parcial (CA-T4C-2): las facturas de la Consultora caen en el mes en curso ──────────────────────────

    [Theory]
    [InlineData("2026-03-01T00:30:00Z", "2026-12-31T23:10:00Z")]
    [InlineData("2026-10-24T14:30:00Z", "2027-02-15T08:00:00Z")]
    public async Task PLAN0018_CA70_FacturasDeLaConsultora_CaenEnElMesEnCursoDeCadaReferencia(string first, string second)
    {
        foreach (var reference in new[] { DateTimeOffset.Parse(first), DateTimeOffset.Parse(second) })
        {
            var databaseName = await SeedAsync(reference);
            await using var db = NewContext(databaseName);
            var today = DateOnly.FromDateTime(reference.UtcDateTime);
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            var invoices = await db.Invoices.Where(i => i.OrganizationId == ConsultancyScenario.OrganizationId).ToListAsync();

            Assert.Equal(4, invoices.Count);
            Assert.All(invoices, i =>
            {
                Assert.InRange(i.IssuedAt, monthStart, today);
                Assert.Equal(today.Year, i.CreatedAt.Year);
                Assert.Equal(today.Month, i.CreatedAt.Month);
                Assert.Equal(DateTimeKind.Utc, i.CreatedAt.Kind);
            });
        }
    }

    // ── CA-92 (RF-68) y CA-70: invitado sin organización con una invitación pendiente a la Agencia ───────────────

    [Fact]
    public async Task PLAN0018_CA92_Invitado_SinMembresiasConInvitacionPendienteALaAgenciaConRolMember()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);
        var invitee = PendingInvitationScenario.Invitee;

        var user = await db.Users.SingleAsync(u => u.UserId == invitee.Id);
        var invitation = await db.OrganizationInvitations.IgnoreQueryFilters()
            .SingleAsync(i => i.Id == PendingInvitationScenario.InvitationId);
        var inviterRole = await db.OrganizationMembers
            .Where(m => m.OrganizationId == AgencyScenario.OrganizationId && m.UserId == invitation.InvitedBy)
            .Select(m => m.Role)
            .SingleAsync();

        // Persona sin organización: ni membresías (tampoco borradas) ni organizaciones propias.
        Assert.False(await db.OrganizationMembers.IgnoreQueryFilters().AnyAsync(m => m.UserId == invitee.Id),
            "El invitado tiene membresías.");
        Assert.False(await db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.OwnerId == invitee.Id),
            "El invitado es owner de una organización.");
        Assert.EndsWith("@" + DemoPeople.EmailDomain, user.Email);

        // Pendiente a la Agencia con rol member: no aceptada, no revocada, no borrada y vigente en la fecha de referencia.
        Assert.Equal(AgencyScenario.OrganizationId, invitation.OrganizationId);
        Assert.Equal(user.Email, invitation.Email);
        Assert.Equal(OrgRole.Member, invitation.Role);
        Assert.Null(invitation.AcceptedAt);
        Assert.Null(invitation.RevokedAt);
        Assert.Null(invitation.DeletedAt);
        Assert.True(invitation.ExpiresAt > ReferenceNow.UtcDateTime, "La invitación ya venció en la fecha de referencia.");
        Assert.True(invitation.IsPendingAt(ReferenceNow.UtcDateTime), "La invitación no está pendiente.");
        Assert.Equal(OrgRole.Admin, inviterRole);
        Assert.Single(await db.OrganizationInvitations.IgnoreQueryFilters().Where(i => i.Email == user.Email).ToListAsync());
    }

    [Theory]
    [InlineData("2026-03-01T00:30:00Z")]
    [InlineData("2026-12-31T23:10:00Z")]
    public async Task PLAN0018_CA70_Invitacion_SeEmiteRelativaALaReferenciaYSigueVigente(string reference)
    {
        var now = DateTimeOffset.Parse(reference);
        var databaseName = await SeedAsync(now);
        await using var db = NewContext(databaseName);
        var issued = DateOnly.FromDateTime(now.UtcDateTime).AddDays(PendingInvitationScenario.IssuedOffsetDays);

        var invitation = await db.OrganizationInvitations.IgnoreQueryFilters()
            .SingleAsync(i => i.Id == PendingInvitationScenario.InvitationId);

        Assert.Equal(issued, DateOnly.FromDateTime(invitation.CreatedAt));
        Assert.Equal(invitation.CreatedAt.Add(OrganizationInvitation.Lifetime), invitation.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, invitation.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, invitation.ExpiresAt.Kind);
        Assert.True(invitation.IsPendingAt(now.UtcDateTime), "La invitación no está vigente en la fecha de referencia.");
    }

    [Fact]
    public async Task PLAN0018_CA92_TokenDeLaInvitacion_SoloSeGuardaSuHashYNingunaColumnaTieneElValorEnClaro()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        await using var db = NewContext(databaseName);
        var tokenHash = await InvitationTokenHashAsync(db);

        // El formato de OneTimeToken.HashOf: SHA-256 en hexadecimal.
        Assert.Matches("^[0-9A-F]{64}$", tokenHash);
        AssertDetectorFindsTokens();

        var offending = new List<string>();
        var load = typeof(DemoScenariosTests).GetMethod(nameof(LoadAllAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var clrType in db.Model.GetEntityTypes().Where(t => !t.IsOwned() && !t.HasSharedClrType).Select(t => t.ClrType).Distinct())
        {
            var textProperties = clrType.GetProperties().Where(p => p.PropertyType == typeof(string)).ToList();
            if (textProperties.Count == 0) continue;

            var rows = await (Task<List<object>>)load.MakeGenericMethod(clrType).Invoke(null, [db])!;
            foreach (var row in rows)
            foreach (var property in textProperties)
                if (property.GetValue(row) is string value && RevealsToken(value, tokenHash))
                    offending.Add($"{clrType.Name}.{property.Name}");
        }

        Assert.True(offending.Count == 0, "Columnas con el token en claro: " + string.Join(", ", offending.Distinct()));
    }

    [Fact]
    public async Task PLAN0018_CA92_SiembraConLogsDeEfYDatosSensibles_NoRegistraElTokenEnClaro()
    {
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        // Otro logger factory es otro proveedor interno de EF: las tres corridas comparten la raíz InMemory.
        var options = new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseInMemoryDatabase(NewDatabaseName(), new InMemoryDatabaseRoot())
            .UseLoggerFactory(loggerFactory)
            .EnableSensitiveDataLogging()
            .Options;

        await using (var db = new ZentoryDbContext(options))
            await new ReferenceDataSeeder(db).SeedAsync();
        await using (var logged = new ZentoryDbContext(options))
            await new DemoScenarioRunner(DemoScenarioRunner.DefaultScenarios)
                .RunAsync(logged, new FakeTimeProvider(ReferenceNow), NewValidPassword());

        await using var check = new ZentoryDbContext(options);
        var tokenHash = await InvitationTokenHashAsync(check);
        var entries = logs.Entries;

        // La captura funciona: hubo logs de la siembra. Ninguno revela el token en claro.
        Assert.NotEmpty(entries);
        Assert.DoesNotContain(entries, e => RevealsToken(e.Message, tokenHash));
    }

    [Fact]
    public async Task PLAN0018_CA92_DbInitDemo_NiLaSalidaNiLosLogsTienenElTokenEnClaro()
    {
        var database = new CliTestDatabase();
        var logs = new CapturingLoggerProvider();

        var run = await CliTestHost.RunAsync(
            [DbCli.Verb, DbCli.Init, DbInitPolicy.DemoFlag, CliResults.YesFlag],
            CliTestHost.Environment(CliTestHost.LocalConnection),
            database,
            configure: services => services.AddLogging(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace)));

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        await using var db = database.NewContext();
        var tokenHash = await InvitationTokenHashAsync(db);

        Assert.False(RevealsToken(run.Output, tokenHash), "La salida de db init --demo contiene el token en claro.");
        Assert.DoesNotContain(logs.Entries, e => RevealsToken(e.Message, tokenHash));
    }

    [Fact]
    public async Task PLAN0018_CA92_SegundaCorridaTrasAceptarLaInvitacion_NoEmiteOtraNiLaModifica()
    {
        var databaseName = await SeedAsync(ReferenceNow);
        var acceptedAt = ReferenceNow.UtcDateTime.AddHours(1);
        string tokenHash;
        await using (var db = NewContext(databaseName))
        {
            var invitation = await db.OrganizationInvitations.IgnoreQueryFilters()
                .SingleAsync(i => i.Id == PendingInvitationScenario.InvitationId);
            invitation.Accept(acceptedAt);
            tokenHash = invitation.TokenHash;
            await db.SaveChangesAsync();
        }

        // Otro día, con la invitación original ya vencida: no se emite otra para ese email ni se toca la aceptada.
        await RunScenariosAsync(databaseName, ReferenceNow.AddDays(30), NewValidPassword());

        await using var check = NewContext(databaseName);
        var invitations = await check.OrganizationInvitations.IgnoreQueryFilters()
            .Where(i => i.OrganizationId == AgencyScenario.OrganizationId)
            .ToListAsync();
        var only = Assert.Single(invitations);
        Assert.Equal(PendingInvitationScenario.InvitationId, only.Id);
        Assert.Equal(acceptedAt, only.AcceptedAt);
        Assert.Equal(tokenHash, only.TokenHash);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Tipos de entidad hija de RF-56 c por legalType. Los colaboradores son el equipo de una empresa: el freelance
    // trabaja solo (free y pro no admiten colaboradores), así que no se le exigen.
    private static IEnumerable<string> ChildEntitiesFor(string legalType)
    {
        string[] common =
        [
            "clientes", "proyectos", "tareas", "hitos", "entregables", "archivos", "horas", "facturas", "propuestas",
            "flujo de caja", "suscripciones",
        ];
        return legalType == LegalType.Empresa ? [.. common, "colaboradores"] : common;
    }

    // Presencia de cada tipo de entidad hija en la organización; la suscripción es del owner (RF-56 e).
    private static async Task<Dictionary<string, bool>> ChildEntitiesAsync(ZentoryDbContext db, Guid organizationId, Guid? ownerId)
        => new()
        {
            ["clientes"]      = await db.Clients.AnyAsync(c => c.OrganizationId == organizationId),
            ["colaboradores"] = await db.Collaborators.AnyAsync(c => c.OrganizationId == organizationId),
            ["proyectos"]     = await db.Projects.AnyAsync(p => p.OrganizationId == organizationId),
            ["tareas"]        = await db.ProjectTasks.AnyAsync(t => t.OrganizationId == organizationId),
            ["hitos"]         = await db.ProjectMilestones.AnyAsync(m => m.OrganizationId == organizationId),
            ["entregables"]   = await db.ProjectDeliverables.AnyAsync(d => d.OrganizationId == organizationId),
            ["archivos"]      = await db.ProjectFiles.AnyAsync(f => f.OrganizationId == organizationId),
            ["horas"]         = await db.TimeEntries.AnyAsync(t => t.OrganizationId == organizationId),
            ["facturas"]      = await db.Invoices.AnyAsync(i => i.OrganizationId == organizationId),
            ["propuestas"]    = await db.Proposals.AnyAsync(p => p.OrganizationId == organizationId),
            ["flujo de caja"] = await db.CashFlowEntries.AnyAsync(c => c.OrganizationId == organizationId),
            ["suscripciones"] = await db.Subscriptions.AnyAsync(s => s.UserId == ownerId),
        };

    private static async Task<int?> LimitAsync(ZentoryDbContext db, string plan, string legalType, string featureKey)
    {
        var planId = await db.BillingPlans.Where(p => p.Name == plan).Select(p => p.Id).SingleAsync();
        return await db.PlanLimits
            .Where(l => l.PlanId == planId && l.LegalType == legalType && l.FeatureKey == featureKey)
            .Select(l => l.LimitValue)
            .SingleAsync();
    }

    // Hash del token de la invitación sembrada: el único rastro del token que puede existir.
    private static async Task<string> InvitationTokenHashAsync(ZentoryDbContext db)
        => await db.OrganizationInvitations.IgnoreQueryFilters()
            .Where(i => i.Id == PendingInvitationScenario.InvitationId)
            .Select(i => i.TokenHash)
            .SingleAsync();

    // El token en claro de OneTimeToken es base64url de 32 bytes (43 caracteres). Sin conocer el valor, un texto lo
    // revela si alguna ventana de 43 caracteres base64url tiene el hash guardado; el valor nunca se imprime.
    private static bool RevealsToken(string text, string tokenHash)
    {
        const int length = 43;
        foreach (Match run in Base64UrlRun.Matches(text))
            for (var start = 0; start + length <= run.Length; start++)
                if (OneTimeToken.HashOf(run.Value.Substring(start, length)) == tokenHash)
                    return true;
        return false;
    }

    private static readonly Regex Base64UrlRun = new("[A-Za-z0-9_-]{43,}", RegexOptions.CultureInvariant);

    // Control positivo del detector: un token recién emitido, solo o dentro de una URL, se detecta por su hash.
    private static void AssertDetectorFindsTokens()
    {
        var sample = OneTimeToken.Issue();
        Assert.True(RevealsToken($"token={Uri.EscapeDataString(sample.RawToken)}&x=1", sample.Hash), "El detector no ve el token.");
        Assert.True(RevealsToken($"ab{sample.RawToken}cd", sample.Hash), "El detector no ve el token dentro de otro texto.");
        Assert.False(RevealsToken(sample.Hash, sample.Hash), "El hash no revela el token.");
    }

    private static string NewDatabaseName() => $"zentory-demo-{Guid.NewGuid():N}";

    // Contraseña válida y distinta en cada corrida (≥ 8 caracteres, ASCII): nunca es un literal del archivo.
    private static string NewValidPassword() => $"Demo-{Guid.NewGuid():N}"[..20];

    private static ZentoryDbContext NewContext(string databaseName)
        => new(new DbContextOptionsBuilder<ZentoryDbContext>().UseInMemoryDatabase(databaseName).Options);

    private static async Task<string> SeedAsync(DateTimeOffset now, string? password = null)
    {
        var databaseName = NewDatabaseName();
        await using (var db = NewContext(databaseName))
            await new ReferenceDataSeeder(db).SeedAsync();

        await RunScenariosAsync(databaseName, now, password ?? NewValidPassword());
        return databaseName;
    }

    // Contexto nuevo en cada corrida: el change tracker no puede esconder duplicados.
    private static async Task RunScenariosAsync(string databaseName, DateTimeOffset now, string password)
    {
        await using var db = NewContext(databaseName);
        await new DemoScenarioRunner(DemoScenarioRunner.DefaultScenarios).RunAsync(db, new FakeTimeProvider(now), password);
    }

    // Fechas de negocio por fila (ID fijo) de proyectos, horas, facturas, hitos, tareas y flujo de caja.
    private static async Task<Dictionary<string, DateOnly>> SnapshotDatesAsync(string databaseName)
    {
        await using var db = NewContext(databaseName);
        var dates = new Dictionary<string, DateOnly>();

        foreach (var p in await db.Projects.ToListAsync())
        {
            dates[$"Project.{p.Id}.StartDate"] = DateOnly.FromDateTime(p.StartDate!.Value);
            dates[$"Project.{p.Id}.EndDate"]   = DateOnly.FromDateTime(p.EndDate!.Value);
        }
        foreach (var t in await db.TimeEntries.ToListAsync()) dates[$"TimeEntry.{t.Id}.Date"] = t.Date;
        // Las facturas de la Consultora (y lo que cuelga de ellas) se anclan al mes en curso (CA-70): cuánto se desplazan
        // depende del día del mes. Las cubre PLAN0018_CA70_FacturasDeLaConsultora_CaenEnElMesEnCursoDeCadaReferencia.
        foreach (var i in await db.Invoices.Where(i => i.OrganizationId != ConsultancyScenario.OrganizationId).ToListAsync())
        {
            dates[$"Invoice.{i.Id}.IssuedAt"] = i.IssuedAt;
            dates[$"Invoice.{i.Id}.DueAt"]    = i.DueAt;
        }
        foreach (var m in await db.ProjectMilestones.ToListAsync()) dates[$"ProjectMilestone.{m.Id}.DueDate"] = m.DueDate!.Value;
        foreach (var t in await db.ProjectTasks.ToListAsync()) dates[$"ProjectTask.{t.Id}.DueDate"] = t.DueDate!.Value;
        foreach (var c in await db.CashFlowEntries
                     .Where(c => c.OrganizationId != ConsultancyScenario.OrganizationId || c.InvoiceId == null).ToListAsync())
            dates[$"CashFlowEntry.{c.Id}.TransactionDate"] = c.TransactionDate;
        foreach (var i in await db.OrganizationInvitations.IgnoreQueryFilters().ToListAsync())
        {
            dates[$"OrganizationInvitation.{i.Id}.CreatedAt"] = DateOnly.FromDateTime(i.CreatedAt);
            dates[$"OrganizationInvitation.{i.Id}.ExpiresAt"] = DateOnly.FromDateTime(i.ExpiresAt);
        }
        return dates;
    }

    private static async Task<Dictionary<string, int>> CountScenarioRowsAsync(string databaseName)
    {
        await using var db = NewContext(databaseName);
        return new Dictionary<string, int>
        {
            ["Organizations"]        = await db.Organizations.CountAsync(),
            ["Users"]                = await db.Users.CountAsync(),
            ["OrganizationMembers"]  = await db.OrganizationMembers.IgnoreQueryFilters().CountAsync(),
            ["OrganizationSettings"] = await db.OrganizationSettings.CountAsync(),
            ["Clients"]              = await db.Clients.IgnoreQueryFilters().CountAsync(),
            ["Collaborators"]        = await db.Collaborators.IgnoreQueryFilters().CountAsync(),
            ["Projects"]             = await db.Projects.IgnoreQueryFilters().CountAsync(),
            ["ProjectCollaborators"] = await db.ProjectCollaborators.CountAsync(),
            ["Proposals"]            = await db.Proposals.IgnoreQueryFilters().CountAsync(),
            ["ProposalItems"]        = await db.ProposalItems.CountAsync(),
            ["Invoices"]             = await db.Invoices.IgnoreQueryFilters().CountAsync(),
            ["InvoiceItems"]         = await db.InvoiceItems.CountAsync(),
            ["InvoicePayments"]      = await db.InvoicePayments.CountAsync(),
            ["TimeEntries"]          = await db.TimeEntries.IgnoreQueryFilters().CountAsync(),
            ["ProjectTasks"]         = await db.ProjectTasks.IgnoreQueryFilters().CountAsync(),
            ["ProjectMilestones"]    = await db.ProjectMilestones.CountAsync(),
            ["ProjectDeliverables"]  = await db.ProjectDeliverables.CountAsync(),
            ["ProjectBillingEntries"] = await db.ProjectBillingEntries.CountAsync(),
            ["ProjectFiles"]         = await db.ProjectFiles.CountAsync(),
            ["ProjectActivityLogs"]  = await db.ProjectActivityLogs.CountAsync(),
            ["CashFlowEntries"]      = await db.CashFlowEntries.IgnoreQueryFilters().CountAsync(),
            ["Integrations"]         = await db.OrganizationIntegrations.IgnoreQueryFilters().CountAsync(),
            ["BillingCustomers"]     = await db.BillingCustomers.CountAsync(),
            ["Subscriptions"]        = await db.Subscriptions.CountAsync(),
            ["BillingInvoices"]      = await db.BillingInvoices.CountAsync(),
            ["OrganizationInvitations"] = await db.OrganizationInvitations.IgnoreQueryFilters().CountAsync(),
        };
    }

    // Recorre por reflexión cada propiedad DateTime de las entidades del modelo que pertenecen a las organizaciones
    // o personas de los escenarios (por OrganizationId, o por UserId en usuarios y suscripciones).
    private static async Task<(int Checked, List<string> Offending)> NonUtcDateTimesAsync(ZentoryDbContext db, HashSet<Guid> scope)
    {
        var checkedValues = 0;
        var offending = new List<string>();
        var load = typeof(DemoScenariosTests).GetMethod(nameof(LoadAllAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var clrType in db.Model.GetEntityTypes().Where(t => !t.IsOwned() && !t.HasSharedClrType).Select(t => t.ClrType).Distinct())
        {
            var dateProperties = clrType.GetProperties()
                .Where(p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?))
                .ToList();
            var owner = clrType == typeof(User)
                ? clrType.GetProperty(nameof(User.UserId))
                : clrType.GetProperty("OrganizationId") ?? clrType.GetProperty("UserId");
            if (dateProperties.Count == 0 || owner is null) continue;

            var rows = await (Task<List<object>>)load.MakeGenericMethod(clrType).Invoke(null, [db])!;
            foreach (var row in rows.Where(r => owner.GetValue(r) is Guid id && scope.Contains(id)))
            foreach (var property in dateProperties)
            {
                if (property.GetValue(row) is not DateTime value) continue;
                checkedValues++;
                if (value.Kind != DateTimeKind.Utc) offending.Add($"{clrType.Name}.{property.Name}");
            }
        }
        return (checkedValues, offending);
    }

    private static async Task<List<object>> LoadAllAsync<T>(ZentoryDbContext db) where T : class
        => await db.Set<T>().IgnoreQueryFilters().AsNoTracking().Cast<object>().ToListAsync();

    private static Guid ReadPrivateStaticGuid(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsType<Guid>(field!.GetValue(null));
    }
}
