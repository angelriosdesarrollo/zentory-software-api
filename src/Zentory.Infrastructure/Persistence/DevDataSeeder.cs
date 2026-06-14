using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence;

/// <summary>
/// Seeds deterministic development data. Runs only in Development; each section is idempotent.
/// Bypasses ITenantContext — injects ZentoryDbContext directly.
/// </summary>
public sealed class DevDataSeeder
{
    private readonly ZentoryDbContext _db;

    public DevDataSeeder(ZentoryDbContext db) => _db = db;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedMasterDataAsync(ct);
        await SeedDevOrgAsync(ct);
        await SeedBillingAsync(ct);
    }

    // ── Master / reference data (global — not tenant-specific) ──────────────

    private async Task SeedMasterDataAsync(CancellationToken ct)
    {
        await SeedBillingPlansAsync(ct);
        await SeedSsRulesAsync(ct);
        await SeedExpenseCategoriesAsync(ct);
        await SeedIntegrationCatalogAsync(ct);
    }

    private async Task SeedBillingPlansAsync(CancellationToken ct)
    {
        if (await _db.BillingPlans.AnyAsync(ct)) return;

        var free   = BillingPlan.Create(Plan.Free,   "Free",   0m,    0m,    sortOrder: 0);
        var pro    = BillingPlan.Create(Plan.Pro,    "Pro",    36m,   29m,   sortOrder: 1);
        var studio = BillingPlan.Create(Plan.Studio, "Studio", 109m,  89m,   sortOrder: 2);

        _db.BillingPlans.AddRange(free, pro, studio);
        await _db.SaveChangesAsync(ct);

        // ── Plan Marketing ───────────────────────────────────────────────────
        _db.PlanMarketing.AddRange(
            // Freelance
            PlanMarketing.Create(free.Id,   AccountType.Freelance, "Para empezar a ordenar tu operación sin costo.",         "Empezar gratis",   isPopular: false, featuresHeading: null),
            PlanMarketing.Create(pro.Id,    AccountType.Freelance, "Para el independiente que ya vive de esto.",              "Comenzar Pro",     isPopular: true,  featuresHeading: "Todo lo de Free, más"),
            PlanMarketing.Create(studio.Id, AccountType.Freelance, "Para el freelance que quiere dominar su cash flow.",      "Comenzar Studio",  isPopular: false, featuresHeading: "Todo lo de Pro, más"),
            // Empresa
            PlanMarketing.Create(free.Id,   AccountType.Empresa,   "Para empezar a ordenar la operación sin costo.",         "Empezar gratis",   isPopular: false, featuresHeading: null),
            PlanMarketing.Create(pro.Id,    AccountType.Empresa,   "Para la empresa que factura formalmente.",                "Comenzar Pro",     isPopular: true,  featuresHeading: "Todo lo de Free, más"),
            PlanMarketing.Create(studio.Id, AccountType.Empresa,   "Para agencias y software factories con equipo.",          "Comenzar Studio",  isPopular: false, featuresHeading: "Todo lo de Pro, más")
        );

        // ── Plan Features ────────────────────────────────────────────────────
        _db.PlanFeatures.AddRange(
            // Freelance Free
            PlanFeature.Create(free.Id,   AccountType.Freelance, "Hasta 2 clientes",                         sortOrder: 0),
            PlanFeature.Create(free.Id,   AccountType.Freelance, "3 cobros / mes",                           sortOrder: 1),
            PlanFeature.Create(free.Id,   AccountType.Freelance, "Proyectos básicos",                        sortOrder: 2),
            PlanFeature.Create(free.Id,   AccountType.Freelance, "Calculadora PILA (seguridad social)", isHighlight: true, badgeText: "siempre gratis", sortOrder: 3),
            // Freelance Pro
            PlanFeature.Create(pro.Id,    AccountType.Freelance, "Clientes y cobros ilimitados",             sortOrder: 0),
            PlanFeature.Create(pro.Id,    AccountType.Freelance, "Sin marca Zentory en documentos",          sortOrder: 1),
            PlanFeature.Create(pro.Id,    AccountType.Freelance, "Tracking de apertura de propuestas",       sortOrder: 2),
            PlanFeature.Create(pro.Id,    AccountType.Freelance, "Multimoneda (USD, COP, EUR…)",             sortOrder: 3),
            PlanFeature.Create(pro.Id,    AccountType.Freelance, "Recordatorios automáticos de cobro",       sortOrder: 4),
            // Freelance Studio
            PlanFeature.Create(studio.Id, AccountType.Freelance, "Vista Gantt / Timeline",                  sortOrder: 0),
            PlanFeature.Create(studio.Id, AccountType.Freelance, "Flujo de caja y runway",                  sortOrder: 1),
            PlanFeature.Create(studio.Id, AccountType.Freelance, "Flujo proyectado a 3 meses",              sortOrder: 2),
            PlanFeature.Create(studio.Id, AccountType.Freelance, "Portal cliente completo",                  sortOrder: 3),
            // Empresa Free
            PlanFeature.Create(free.Id,   AccountType.Empresa,   "Hasta 2 clientes",                        sortOrder: 0),
            PlanFeature.Create(free.Id,   AccountType.Empresa,   "3 facturas / mes",                        sortOrder: 1),
            PlanFeature.Create(free.Id,   AccountType.Empresa,   "Proyectos básicos",                       sortOrder: 2),
            PlanFeature.Create(free.Id,   AccountType.Empresa,   "Calculadora PILA (seguridad social)", isHighlight: true, badgeText: "siempre gratis", sortOrder: 3),
            // Empresa Pro
            PlanFeature.Create(pro.Id,    AccountType.Empresa,   "Clientes y facturas ilimitados",          sortOrder: 0),
            PlanFeature.Create(pro.Id,    AccountType.Empresa,   "Sin marca Zentory en documentos",         sortOrder: 1),
            PlanFeature.Create(pro.Id,    AccountType.Empresa,   "Facturación Electrónica DIAN", isHighlight: true, sortOrder: 2),
            PlanFeature.Create(pro.Id,    AccountType.Empresa,   "Tracking de apertura de propuestas",      sortOrder: 3),
            PlanFeature.Create(pro.Id,    AccountType.Empresa,   "Multimoneda (USD, COP, EUR…)",            sortOrder: 4),
            PlanFeature.Create(pro.Id,    AccountType.Empresa,   "Recordatorios automáticos de cobro",      sortOrder: 5),
            // Empresa Studio
            PlanFeature.Create(studio.Id, AccountType.Empresa,   "Rentabilidad real por proyecto",          sortOrder: 0),
            PlanFeature.Create(studio.Id, AccountType.Empresa,   "Flujo de caja y runway",                  sortOrder: 1),
            PlanFeature.Create(studio.Id, AccountType.Empresa,   "Colaboradores y cuentas por pagar",       sortOrder: 2),
            PlanFeature.Create(studio.Id, AccountType.Empresa,   "Multi-usuario y roles de acceso",         sortOrder: 3),
            PlanFeature.Create(studio.Id, AccountType.Empresa,   "Propuestas con margen y equipo",          sortOrder: 4)
        );

        // ── Plan Limits ──────────────────────────────────────────────────────
        _db.PlanLimits.AddRange(
            // Freelance Free
            PlanLimit.Create(free.Id,   AccountType.Freelance, "max_clients",         2),
            PlanLimit.Create(free.Id,   AccountType.Freelance, "max_invoices_month",  3),
            PlanLimit.Create(free.Id,   AccountType.Freelance, "max_projects",        5),
            PlanLimit.Create(free.Id,   AccountType.Freelance, "max_collaborators",   0),
            // Freelance Pro / Studio — sin límite (null)
            PlanLimit.Create(pro.Id,    AccountType.Freelance, "max_clients",         null),
            PlanLimit.Create(pro.Id,    AccountType.Freelance, "max_invoices_month",  null),
            PlanLimit.Create(pro.Id,    AccountType.Freelance, "max_projects",        null),
            PlanLimit.Create(pro.Id,    AccountType.Freelance, "max_collaborators",   0),
            PlanLimit.Create(studio.Id, AccountType.Freelance, "max_clients",         null),
            PlanLimit.Create(studio.Id, AccountType.Freelance, "max_invoices_month",  null),
            PlanLimit.Create(studio.Id, AccountType.Freelance, "max_projects",        null),
            PlanLimit.Create(studio.Id, AccountType.Freelance, "max_collaborators",   null),
            // Empresa Free
            PlanLimit.Create(free.Id,   AccountType.Empresa,   "max_clients",         2),
            PlanLimit.Create(free.Id,   AccountType.Empresa,   "max_invoices_month",  3),
            PlanLimit.Create(free.Id,   AccountType.Empresa,   "max_projects",        5),
            PlanLimit.Create(free.Id,   AccountType.Empresa,   "max_collaborators",   0),
            // Empresa Pro / Studio
            PlanLimit.Create(pro.Id,    AccountType.Empresa,   "max_clients",         null),
            PlanLimit.Create(pro.Id,    AccountType.Empresa,   "max_invoices_month",  null),
            PlanLimit.Create(pro.Id,    AccountType.Empresa,   "max_projects",        null),
            PlanLimit.Create(pro.Id,    AccountType.Empresa,   "max_collaborators",   10),
            PlanLimit.Create(studio.Id, AccountType.Empresa,   "max_clients",         null),
            PlanLimit.Create(studio.Id, AccountType.Empresa,   "max_invoices_month",  null),
            PlanLimit.Create(studio.Id, AccountType.Empresa,   "max_projects",        null),
            PlanLimit.Create(studio.Id, AccountType.Empresa,   "max_collaborators",   null)
        );

        // ── Plan Compare Items ───────────────────────────────────────────────
        _db.PlanCompareItems.AddRange(
            // Shared (both account types)
            PlanCompareItem.Create("Clientes",                   freeValue: "Hasta 2", proValue: "Ilimitados", studioValue: "Ilimitados", sortOrder: 0),
            PlanCompareItem.Create("Proyectos",                  freeValue: "Básicos", proValue: "true",        studioValue: "true",        sortOrder: 2),
            PlanCompareItem.Create("Calculadora PILA",           freeValue: "true",    proValue: "true",        studioValue: "true",        sortOrder: 3),
            PlanCompareItem.Create("Sin marca Zentory",          freeValue: "false",   proValue: "true",        studioValue: "true",        sortOrder: 4),
            PlanCompareItem.Create("Tracking de apertura",       freeValue: "false",   proValue: "true",        studioValue: "true",        sortOrder: 5),
            PlanCompareItem.Create("Multimoneda",                freeValue: "false",   proValue: "true",        studioValue: "true",        sortOrder: 6),
            PlanCompareItem.Create("Flujo de caja / runway",     freeValue: "false",   proValue: "false",       studioValue: "true",        sortOrder: 8),
            PlanCompareItem.Create("Vista Gantt / Timeline",     freeValue: "false",   proValue: "false",       studioValue: "true",        sortOrder: 9, accountType: AccountType.Freelance),
            PlanCompareItem.Create("Portal cliente completo",    freeValue: "false",   proValue: "false",       studioValue: "true",        sortOrder: 10, accountType: AccountType.Freelance),
            // Freelance specific
            PlanCompareItem.Create("Cobros / mes",               freeValue: "3",       proValue: "Ilimitados",  studioValue: "Ilimitados",  sortOrder: 1, accountType: AccountType.Freelance),
            // Empresa specific
            PlanCompareItem.Create("Facturas / mes",             freeValue: "3",       proValue: "Ilimitados",  studioValue: "Ilimitados",  sortOrder: 1, accountType: AccountType.Empresa),
            PlanCompareItem.Create("Facturación Electrónica DIAN", freeValue: "false", proValue: "true",        studioValue: "true",        sortOrder: 7, accountType: AccountType.Empresa, isEmpresaOnly: true),
            PlanCompareItem.Create("Rentabilidad real por proyecto", freeValue: "false",proValue: "false",       studioValue: "true",        sortOrder: 11, accountType: AccountType.Empresa, isEmpresaOnly: true),
            PlanCompareItem.Create("Colaboradores y cuentas por pagar", freeValue: "false", proValue: "false",  studioValue: "true",        sortOrder: 12, accountType: AccountType.Empresa, isEmpresaOnly: true),
            PlanCompareItem.Create("Multi-usuario y roles",      freeValue: "false",   proValue: "false",       studioValue: "true",        sortOrder: 13, accountType: AccountType.Empresa, isEmpresaOnly: true)
        );

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedSsRulesAsync(CancellationToken ct)
    {
        if (await _db.SsRules.AnyAsync(r => r.CountryCode == "CO" && r.EffectiveYear == 2026, ct)) return;

        // SMMLV Colombia 2026: $1.423.500 COP
        const decimal smlv2026 = 1_423_500m;

        _db.SsRules.AddRange(
            // Salud — independiente (cotiza el total: 12.5%)
            new SsRule { CountryCode = "CO", EffectiveYear = 2026, FundType = "salud",    ContributorType = "independiente", EmployeePct = 0.125m, EmployerPct = 0m,     TotalPct = 0.125m, MinBaseSmlv = 1m, SmlvCop = smlv2026, Notes = "IBC mínimo = 1 SMMLV" },
            // Pensión — independiente
            new SsRule { CountryCode = "CO", EffectiveYear = 2026, FundType = "pension",  ContributorType = "independiente", EmployeePct = 0.16m,  EmployerPct = 0m,     TotalPct = 0.16m,  MinBaseSmlv = 1m, SmlvCop = smlv2026, Notes = "Sin fondo de solidaridad si IBC < 4 SMMLV" },
            // ARL — riesgo 1 (0.522%)
            new SsRule { CountryCode = "CO", EffectiveYear = 2026, FundType = "arl",      ContributorType = "independiente", EmployeePct = 0.00522m,EmployerPct = 0m,     TotalPct = 0.00522m, MinBaseSmlv = 1m, SmlvCop = smlv2026, ArlLevel = 1, Notes = "Riesgo 1: actividades de oficina" },
            // ARL — riesgo 2
            new SsRule { CountryCode = "CO", EffectiveYear = 2026, FundType = "arl",      ContributorType = "independiente", EmployeePct = 0.01044m,EmployerPct = 0m,     TotalPct = 0.01044m, MinBaseSmlv = 1m, SmlvCop = smlv2026, ArlLevel = 2, Notes = "Riesgo 2" },
            // ARL — riesgo 3
            new SsRule { CountryCode = "CO", EffectiveYear = 2026, FundType = "arl",      ContributorType = "independiente", EmployeePct = 0.02436m,EmployerPct = 0m,     TotalPct = 0.02436m, MinBaseSmlv = 1m, SmlvCop = smlv2026, ArlLevel = 3, Notes = "Riesgo 3" },
            // Salud — empleado
            new SsRule { CountryCode = "CO", EffectiveYear = 2026, FundType = "salud",    ContributorType = "empleado",      EmployeePct = 0.04m,  EmployerPct = 0.085m, TotalPct = 0.125m, MinBaseSmlv = 1m, SmlvCop = smlv2026 },
            // Pensión — empleado
            new SsRule { CountryCode = "CO", EffectiveYear = 2026, FundType = "pension",  ContributorType = "empleado",      EmployeePct = 0.04m,  EmployerPct = 0.12m,  TotalPct = 0.16m,  MinBaseSmlv = 1m, SmlvCop = smlv2026 },
            // Caja de compensación — empleador
            new SsRule { CountryCode = "CO", EffectiveYear = 2026, FundType = "caja_compensacion", ContributorType = "empleador", EmployeePct = 0m, EmployerPct = 0.04m,  TotalPct = 0.04m,  MinBaseSmlv = 1m, SmlvCop = smlv2026 }
        );

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedIntegrationCatalogAsync(CancellationToken ct)
    {
        if (await _db.IntegrationCatalog.AnyAsync(ct)) return;

        _db.IntegrationCatalog.AddRange(
            IntegrationCatalog.Create("google-meet", "Google Meet", "Videollamadas integradas en propuestas y proyectos",         sortOrder: 1),
            IntegrationCatalog.Create("slack",       "Slack",       "Notificaciones de proyectos e invoices en tu workspace",     sortOrder: 2),
            IntegrationCatalog.Create("github",      "GitHub",      "Vincula repositorios con proyectos y time entries",          sortOrder: 3),
            IntegrationCatalog.Create("figma",       "Figma",       "Adjunta entregables de diseño directamente en propuestas",   sortOrder: 4)
        );

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedExpenseCategoriesAsync(CancellationToken ct)
    {
        if (await _db.ExpenseCategories.AnyAsync(ct)) return;

        _db.ExpenseCategories.AddRange(
            new ExpenseCategory { Id = 1,  Name = "Nómina",                     Slug = "nomina",                   Type = "expense" },
            new ExpenseCategory { Id = 2,  Name = "Honorarios",                 Slug = "honorarios",               Type = "expense" },
            new ExpenseCategory { Id = 3,  Name = "Compras y suministros",      Slug = "compras",                  Type = "expense" },
            new ExpenseCategory { Id = 4,  Name = "Suscripciones y licencias",  Slug = "suscripciones",            Type = "expense" },
            new ExpenseCategory { Id = 5,  Name = "Arriendo y servicios",       Slug = "arriendo",                 Type = "expense" },
            new ExpenseCategory { Id = 6,  Name = "Impuestos y parafiscales",   Slug = "impuestos_parafiscales",   Type = "expense" },
            new ExpenseCategory { Id = 7,  Name = "Servicios profesionales",    Slug = "servicios_profesionales",  Type = "expense" },
            new ExpenseCategory { Id = 8,  Name = "Marketing y publicidad",     Slug = "marketing_publicidad",     Type = "expense" },
            new ExpenseCategory { Id = 9,  Name = "Otros gastos",               Slug = "otros",                    Type = "expense" },
            new ExpenseCategory { Id = 10, Name = "Ingresos por servicios",     Slug = "ingresos_servicios",       Type = "income"  },
            new ExpenseCategory { Id = 11, Name = "Ingresos por consultoría",   Slug = "ingresos_consultoria",     Type = "income"  },
            new ExpenseCategory { Id = 12, Name = "Otros ingresos",             Slug = "otros_ingresos",           Type = "income"  }
        );

        await _db.SaveChangesAsync(ct);
    }

    // ── Dev org + tenant data ────────────────────────────────────────────────

    // Fixed GUIDs so JWT tokens stay valid across in-memory DB restarts.
    private static readonly Guid DevOrgId  = new("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid DevUserId = new("b2000000-0000-0000-0000-000000000002");

    private async Task SeedDevOrgAsync(CancellationToken ct)
    {
        if (await _db.Organizations.AnyAsync(o => o.Name == "TechFactory CO", ct))
            return;

        // ── Org + User ──────────────────────────────────────────────────────
        var org = Organization.Create("TechFactory CO", AccountType.Empresa, id: DevOrgId);
        org.SetPlan(Plan.Studio);
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync(ct);

        var user = User.Create(
            org.OrganizationId,
            "dev@zentorysoftware.com",
            "Camila",
            "Perez",
            "owner",
            BCrypt.Net.BCrypt.HashPassword("Zentory2026*"),
            id: DevUserId);
        _db.Users.Add(user);

        _db.OrganizationMembers.Add(OrganizationMember.Create(org.OrganizationId, user.UserId, "owner"));
        await _db.SaveChangesAsync(ct);

        var oid = org.OrganizationId;

        // ── Org Settings defaults ────────────────────────────────────────────
        _db.OrganizationSettings.AddRange(
            OrganizationSettings.Set(oid, "currency.primary",      "COP"),
            OrganizationSettings.Set(oid, "currency.reports",      "USD"),
            OrganizationSettings.Set(oid, "number.format",         "latam"),
            OrganizationSettings.Set(oid, "symbol.position",       "before"),
            OrganizationSettings.Set(oid, "timezone",              "America/Bogota"),
            OrganizationSettings.Set(oid, "date.format",           "DD/MM/YYYY"),
            OrganizationSettings.Set(oid, "language",              "es"),
            OrganizationSettings.Set(oid, "week.first_day",        "monday"),
            OrganizationSettings.Set(oid, "invoice.payment_method","Transferencia bancaria"),
            OrganizationSettings.Set(oid, "proposal.validity_days","30"),
            OrganizationSettings.Set(oid, "proposal.revision_count","3"),
            OrganizationSettings.Set(oid, "invoice.currency",      "USD")
        );
        await _db.SaveChangesAsync(ct);

        // ── Clients ─────────────────────────────────────────────────────────
        var cTechCorp   = Client.Create(oid, "TechCorp Solutions",   "Ana Martínez",  "ana@techcorp.com",      "+57 310 111 1111", "Bogotá",       "900.111.222-1");
        var cBancoGreen = Client.Create(oid, "BancoGreen",           "Pedro Sánchez", "pedro@bancogreen.com",  "+57 310 222 2222", "Medellín",     "900.222.333-2");
        var cLogistica  = Client.Create(oid, "Logística Express",    "María López",   "maria@logistica.com",   "+57 310 333 3333", "Cali",         "900.333.444-3");
        var cMediaSol   = Client.Create(oid, "MediaSol",             "Juan Torres",   "juan@mediasol.com",     "+57 310 444 4444", "Barranquilla", "900.444.555-4");
        var cInnova     = Client.Create(oid, "InnovaGroup",          "Sofía Ramírez", "sofia@innovagroup.com", "+57 310 555 5555", "Bogotá",       "900.555.666-5");
        var cRetail     = Client.Create(oid, "RetailMax",            "Carlos Díaz",   "carlos@retailmax.com",  "+57 310 666 6666", "Medellín",     "900.666.777-6");

        _db.Clients.AddRange(cTechCorp, cBancoGreen, cLogistica, cMediaSol, cInnova, cRetail);
        await _db.SaveChangesAsync(ct);

        // ── Collaborators ────────────────────────────────────────────────────
        var colMateo  = Collaborator.Create(oid, "Mateo Vélez",  "hourly_contractor", "COP", role: "Frontend Dev",   hourlyRate: 25m);
        var colLaura  = Collaborator.Create(oid, "Laura Patiño", "hourly_contractor", "COP", role: "Backend Dev",    hourlyRate: 20m);
        var colJuan   = Collaborator.Create(oid, "Juan Sánchez", "hourly_contractor", "COP", role: "Full Stack Dev", hourlyRate: 22m);
        var colCarlos = Collaborator.Create(oid, "Carlos Reyes", "fixed_contractor",  "COP", role: "DevOps",         monthlyRate: 3200m);

        _db.Collaborators.AddRange(colMateo, colLaura, colJuan, colCarlos);
        await _db.SaveChangesAsync(ct);

        // ── Projects ─────────────────────────────────────────────────────────
        var prjPortal = Project.Create(oid, cTechCorp.Id,   "Portal Corporativo",    BillingType.FixedPrice, 85_000m,  "USD", 400, new DateTime(2025, 10, 1), new DateTime(2026, 3, 31));
        var prjMobile = Project.Create(oid, cBancoGreen.Id, "App Móvil Fintech",     BillingType.Hourly,    120_000m, "USD", 600, new DateTime(2025,  9, 1), new DateTime(2026, 6, 30));
        var prjData   = Project.Create(oid, cLogistica.Id,  "Plataforma de Datos",   BillingType.Milestone,  95_000m,  "USD", 500, new DateTime(2025, 11, 1), new DateTime(2026, 5, 31));
        var prjEcomm  = Project.Create(oid, cMediaSol.Id,   "E-commerce Renovation", BillingType.FixedPrice, 45_000m,  "USD", 250, new DateTime(2025, 12, 1), new DateTime(2026, 4, 30));
        var prjAudit  = Project.Create(oid, cInnova.Id,     "Security Audit",        BillingType.Hourly,     32_000m,  "USD", 200, new DateTime(2026,  1, 1), new DateTime(2026, 3, 31));
        var prjMktg   = Project.Create(oid, cRetail.Id,     "Marketing Dashboard",   BillingType.FixedPrice, 28_000m,  "USD", 150, new DateTime(2025,  8, 1), new DateTime(2025,12, 31));

        prjMobile.ChangeStatus(ProjectStatus.Paused);

        _db.Projects.AddRange(prjPortal, prjMobile, prjData, prjEcomm, prjAudit, prjMktg);
        await _db.SaveChangesAsync(ct);

        // ── Proposals ────────────────────────────────────────────────────────
        var propPortal = Proposal.Create(oid, cTechCorp.Id,   "Propuesta Portal Corporativo v2",      "USD");
        propPortal.AddItem(ProposalItem.Create(oid, propPortal.Id, "Diseño UX/UI",             1m,  8_000m, 0));
        propPortal.AddItem(ProposalItem.Create(oid, propPortal.Id, "Desarrollo Frontend",      1m, 30_000m, 1));
        propPortal.AddItem(ProposalItem.Create(oid, propPortal.Id, "Integración Backend",      1m, 20_000m, 2));
        propPortal.MarkAsSent();
        propPortal.MarkAsAccepted();

        var propSaas = Proposal.Create(oid, cBancoGreen.Id, "Plataforma SaaS Fintech",          "USD");
        propSaas.AddItem(ProposalItem.Create(oid, propSaas.Id, "Arquitectura Cloud",           1m, 15_000m, 0));
        propSaas.AddItem(ProposalItem.Create(oid, propSaas.Id, "Desarrollo API REST",          1m, 45_000m, 1));
        propSaas.AddItem(ProposalItem.Create(oid, propSaas.Id, "App Móvil iOS/Android",        1m, 35_000m, 2));
        propSaas.MarkAsSent();
        propSaas.RecordView();

        var propLogistics = Proposal.Create(oid, cLogistica.Id, "Sistema Gestión Logística",  "USD");
        propLogistics.AddItem(ProposalItem.Create(oid, propLogistics.Id, "Análisis requerimientos", 1m,  5_000m, 0));
        propLogistics.AddItem(ProposalItem.Create(oid, propLogistics.Id, "Desarrollo plataforma",  1m, 60_000m, 1));
        propLogistics.AddItem(ProposalItem.Create(oid, propLogistics.Id, "Capacitación y soporte", 1m, 10_000m, 2));
        propLogistics.MarkAsSent();
        propLogistics.MarkAsAccepted();

        var propInnova = Proposal.Create(oid, cInnova.Id, "Consultoría Transformación Digital", "USD");
        propInnova.AddItem(ProposalItem.Create(oid, propInnova.Id, "Diagnóstico Tecnológico",  1m,  8_000m, 0));
        propInnova.AddItem(ProposalItem.Create(oid, propInnova.Id, "Roadmap Digital",           1m, 12_000m, 1));
        propInnova.MarkAsSent();
        propInnova.MarkAsRejected();

        var propRetail = Proposal.Create(oid, cRetail.Id, "Dashboard Analytics RetailMax",     "USD");
        propRetail.AddItem(ProposalItem.Create(oid, propRetail.Id, "Diseño Dashboard",         1m,  5_000m, 0));
        propRetail.AddItem(ProposalItem.Create(oid, propRetail.Id, "Integración datos",        1m, 18_000m, 1));

        _db.Proposals.AddRange(propPortal, propSaas, propLogistics, propInnova, propRetail);
        await _db.SaveChangesAsync(ct);

        // ── Invoices ─────────────────────────────────────────────────────────
        var inv1 = Invoice.Create(oid, cTechCorp.Id,   "INV-202501-001", DateOnly.FromDateTime(new DateTime(2025, 2, 28)), "USD", projectId: prjPortal.Id);
        inv1.AddItem(InvoiceItem.Create(oid, inv1.Id, "Diseño UX/UI — Sprint 1",        1m, 12_000m, 0));
        inv1.AddItem(InvoiceItem.Create(oid, inv1.Id, "Desarrollo Frontend — Sprint 1", 1m, 18_000m, 1));
        inv1.MarkAsSent();
        inv1.RecordPayment(inv1.Total);

        var inv2 = Invoice.Create(oid, cBancoGreen.Id, "INV-202502-002", DateOnly.FromDateTime(new DateTime(2025, 3, 31)), "USD", projectId: prjMobile.Id);
        inv2.AddItem(InvoiceItem.Create(oid, inv2.Id, "App Móvil — 80 horas", 80m, 150m, 0));
        inv2.AddItem(InvoiceItem.Create(oid, inv2.Id, "Revisiones UX",        20m, 100m, 1));
        inv2.MarkAsSent();
        inv2.RecordPayment(inv2.Total);

        var inv3 = Invoice.Create(oid, cLogistica.Id,  "INV-202503-003", DateOnly.FromDateTime(new DateTime(2025, 4, 30)), "USD", projectId: prjData.Id);
        inv3.AddItem(InvoiceItem.Create(oid, inv3.Id, "Milestone 1 — Arquitectura", 1m, 25_000m, 0));
        inv3.MarkAsSent();

        var inv4 = Invoice.Create(oid, cMediaSol.Id,   "INV-202504-004", DateOnly.FromDateTime(new DateTime(2025, 5, 31)), "USD", projectId: prjEcomm.Id);
        inv4.AddItem(InvoiceItem.Create(oid, inv4.Id, "E-commerce — Fase 1", 1m, 22_500m, 0));
        inv4.MarkAsSent();
        inv4.MarkAsOverdue();

        var inv5 = Invoice.Create(oid, cInnova.Id,     "INV-202505-005", DateOnly.FromDateTime(new DateTime(2025, 6, 30)), "USD", projectId: prjAudit.Id);
        inv5.AddItem(InvoiceItem.Create(oid, inv5.Id, "Security Audit — 60 horas", 60m, 120m, 0));

        var inv6 = Invoice.Create(oid, cRetail.Id,     "INV-202506-006", DateOnly.FromDateTime(new DateTime(2025, 7, 31)), "USD", projectId: prjMktg.Id);
        inv6.AddItem(InvoiceItem.Create(oid, inv6.Id, "Dashboard Analytics — Fase final", 1m, 28_000m, 0));
        inv6.MarkAsSent();
        inv6.RecordPayment(inv6.Total);

        _db.Invoices.AddRange(inv1, inv2, inv3, inv4, inv5, inv6);
        await _db.SaveChangesAsync(ct);

        // ── Time Entries ─────────────────────────────────────────────────────
        var entries = new List<TimeEntry>
        {
            // pending
            Make(oid, prjPortal.Id, colMateo.Id,  new DateOnly(2026, 6, 3), 8m, 25m, "Implementación navbar responsive"),
            Make(oid, prjPortal.Id, colLaura.Id,  new DateOnly(2026, 6, 4), 6m, 20m, "API de autenticación"),
            Make(oid, prjData.Id,   colJuan.Id,   new DateOnly(2026, 6, 2), 7m, 22m, "Pipelines ETL fase 1"),
            Make(oid, prjEcomm.Id,  colMateo.Id,  new DateOnly(2026, 6, 5), 5m, 25m, "Carrito de compras UI"),
            Make(oid, prjAudit.Id,  colLaura.Id,  new DateOnly(2026, 6, 6), 4m, 20m, "Análisis de vulnerabilidades"),
            Make(oid, prjMktg.Id,   colCarlos.Id, new DateOnly(2026, 6, 1), 8m, 22m, "CI/CD pipeline setup"),
        };

        // approved
        var approved = new[]
        {
            Make(oid, prjPortal.Id, colJuan.Id,   new DateOnly(2026, 5, 28), 8m, 22m, "Backend endpoints autenticación"),
            Make(oid, prjMobile.Id, colMateo.Id,  new DateOnly(2026, 5, 27), 6m, 25m, "Pantallas login fintech"),
            Make(oid, prjData.Id,   colCarlos.Id, new DateOnly(2026, 5, 26), 7m, 22m, "Infraestructura Kubernetes"),
            Make(oid, prjEcomm.Id,  colLaura.Id,  new DateOnly(2026, 5, 25), 5m, 20m, "Microservicio pagos"),
        };
        foreach (var e in approved) e.Approve();
        entries.AddRange(approved);

        // billed
        var billed = new[]
        {
            Make(oid, prjPortal.Id, colMateo.Id, new DateOnly(2026, 5, 15), 8m, 25m, "Sprint 1 — Homepage"),
            Make(oid, prjMobile.Id, colLaura.Id, new DateOnly(2026, 5, 14), 7m, 20m, "Sprint 1 — Auth module"),
        };
        foreach (var e in billed) { e.Approve(); e.MarkBilled(); }
        entries.AddRange(billed);

        _db.TimeEntries.AddRange(entries);
        await _db.SaveChangesAsync(ct);

        // ── Project Tasks ────────────────────────────────────────────────────
        var tasks = new List<ProjectTask>
        {
            ProjectTask.Create(oid, prjPortal.Id, "Diseñar onboarding KYC paso 3",           "todo",        "high",   assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 6, 15)),
            ProjectTask.Create(oid, prjPortal.Id, "Configurar push notifications",            "todo",        "medium", assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6, 18)),
            ProjectTask.Create(oid, prjPortal.Id, "Definir esquema de base de datos v2",     "todo",        "low",    assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6, 22)),
            ProjectTask.Create(oid, prjPortal.Id, "Auth flow con biométricos",               "in_progress", "high",   assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6, 10)),
            ProjectTask.Create(oid, prjPortal.Id, "Integración API Open Finance",            "in_progress", "high",   assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6, 12)),
            ProjectTask.Create(oid, prjPortal.Id, "Pantallas dashboard usuario final",       "in_progress", "medium", assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 6, 14)),
            ProjectTask.Create(oid, prjPortal.Id, "Configuración CI/CD pipeline",            "in_progress", "low",    assigneeId: colCarlos.Id, dueDate: new DateOnly(2026, 6, 16)),
            ProjectTask.Create(oid, prjPortal.Id, "Login pantalla principal v2",             "review",      "high",   assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 6,  6)),
            ProjectTask.Create(oid, prjPortal.Id, "Pruebas de seguridad JWT",                "review",      "high",   assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6,  7)),
            ProjectTask.Create(oid, prjPortal.Id, "Documentación endpoints REST",            "review",      "medium", assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6,  8)),
            ProjectTask.Create(oid, prjPortal.Id, "Setup del proyecto Next.js + TypeScript", "done",        "medium", assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6,  1)),
            ProjectTask.Create(oid, prjPortal.Id, "Wireframes flujo de pagos",               "done",        "high",   assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 6,  2)),
            ProjectTask.Create(oid, prjPortal.Id, "Definición de contratos API",             "done",        "medium", assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6,  3)),

            ProjectTask.Create(oid, prjData.Id, "Diseñar módulo de transferencias",          "todo",        "high",   assigneeId: colCarlos.Id, dueDate: new DateOnly(2026, 6, 20)),
            ProjectTask.Create(oid, prjData.Id, "Revisar compliance PCI-DSS",                "todo",        "high",   assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6, 22)),
            ProjectTask.Create(oid, prjData.Id, "Migración esquema v3 → v4",                "in_progress", "high",   assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6, 12)),
            ProjectTask.Create(oid, prjData.Id, "Integración Swift MT103",                   "in_progress", "medium", assigneeId: colCarlos.Id, dueDate: new DateOnly(2026, 6, 15)),
            ProjectTask.Create(oid, prjData.Id, "Auditoría de logs de transacciones",        "review",      "high",   assigneeId: colCarlos.Id, dueDate: new DateOnly(2026, 6,  9)),
            ProjectTask.Create(oid, prjData.Id, "Setup ambiente de certificación",           "done",        "medium", assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 5, 28)),

            // prjMobile — App Móvil Fintech
            ProjectTask.Create(oid, prjMobile.Id, "Diseño UI pantalla principal",            "todo",        "high",   assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 6, 25)),
            ProjectTask.Create(oid, prjMobile.Id, "Implementar notificaciones push",         "todo",        "medium", assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6, 28)),
            ProjectTask.Create(oid, prjMobile.Id, "Módulo de transferencias QR",             "in_progress", "high",   assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6, 18)),
            ProjectTask.Create(oid, prjMobile.Id, "Integración biometría (Touch ID)",        "in_progress", "high",   assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6, 20)),
            ProjectTask.Create(oid, prjMobile.Id, "Tests de rendimiento en Android",         "review",      "medium", assigneeId: colCarlos.Id, dueDate: new DateOnly(2026, 6, 11)),
            ProjectTask.Create(oid, prjMobile.Id, "Setup React Native + TypeScript",         "done",        "medium", assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 5, 20)),
            ProjectTask.Create(oid, prjMobile.Id, "Configuración CI/CD Fastlane",            "done",        "low",    assigneeId: colCarlos.Id, dueDate: new DateOnly(2026, 5, 25)),

            // prjEcomm — E-commerce Renovation
            ProjectTask.Create(oid, prjEcomm.Id, "Rediseño página de checkout",              "todo",        "high",   assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 6, 24)),
            ProjectTask.Create(oid, prjEcomm.Id, "Integración pasarela Stripe",              "todo",        "high",   assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6, 26)),
            ProjectTask.Create(oid, prjEcomm.Id, "Optimización SEO y meta tags",             "in_progress", "medium", assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6, 16)),
            ProjectTask.Create(oid, prjEcomm.Id, "Carrito de compras persistente",           "in_progress", "high",   assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 6, 14)),
            ProjectTask.Create(oid, prjEcomm.Id, "Tests de usabilidad con usuarios reales",  "review",      "medium", assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6,  9)),
            ProjectTask.Create(oid, prjEcomm.Id, "Auditoría del sitio actual",               "done",        "low",    assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 5, 30)),

            // prjAudit — Security Audit
            ProjectTask.Create(oid, prjAudit.Id, "Análisis de vulnerabilidades OWASP Top 10","todo",        "high",   assigneeId: colCarlos.Id, dueDate: new DateOnly(2026, 6, 20)),
            ProjectTask.Create(oid, prjAudit.Id, "Pruebas de penetración endpoints API",    "in_progress", "high",   assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6, 15)),
            ProjectTask.Create(oid, prjAudit.Id, "Revisión de configuración WAF",            "in_progress", "medium", assigneeId: colCarlos.Id, dueDate: new DateOnly(2026, 6, 17)),
            ProjectTask.Create(oid, prjAudit.Id, "Reporte ejecutivo de hallazgos",           "review",      "high",   assigneeId: colJuan.Id,   dueDate: new DateOnly(2026, 6, 10)),
            ProjectTask.Create(oid, prjAudit.Id, "Inventario de activos críticos",           "done",        "medium", assigneeId: colCarlos.Id, dueDate: new DateOnly(2026, 6,  2)),

            // prjMktg — Marketing Dashboard
            ProjectTask.Create(oid, prjMktg.Id, "Diseño de gráficas de conversión",          "todo",        "medium", assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 6, 22)),
            ProjectTask.Create(oid, prjMktg.Id, "Conexión con Google Analytics API",         "in_progress", "high",   assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6, 14)),
            ProjectTask.Create(oid, prjMktg.Id, "Tabla de segmentación por canal",           "in_progress", "medium", assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 6, 16)),
            ProjectTask.Create(oid, prjMktg.Id, "Exportación de reportes a PDF",             "review",      "low",    assigneeId: colMateo.Id,  dueDate: new DateOnly(2026, 6,  8)),
            ProjectTask.Create(oid, prjMktg.Id, "Wireframes y definición de KPIs",           "done",        "medium", assigneeId: colLaura.Id,  dueDate: new DateOnly(2026, 5, 15)),
        };

        _db.ProjectTasks.AddRange(tasks);
        await _db.SaveChangesAsync(ct);

        // ── CashFlow Entries ─────────────────────────────────────────────────
        if (await _db.CashFlowEntries.AnyAsync(e => e.OrganizationId == oid, ct)) return;

        var cashEntries = new List<CashFlowEntry>();

        // Gastos mensuales repetitivos (ene–jun 2026)
        for (var m = 1; m <= 6; m++)
        {
            cashEntries.AddRange([
                CashFlowEntry.CreateManual(oid, "expense", "Nómina equipo",                         9_840m, "USD", 1m, 9_840m,  new DateOnly(2026, m, 5),  categoryId: 1),
                CashFlowEntry.CreateManual(oid, "expense", "Licencias software",                    1_840m, "USD", 1m, 1_840m,  new DateOnly(2026, m, 5),  categoryId: 4),
                CashFlowEntry.CreateManual(oid, "expense", "Arriendo oficina Bogotá",               2_200m, "USD", 1m, 2_200m,  new DateOnly(2026, m, 1),  categoryId: 5),
                CashFlowEntry.CreateManual(oid, "expense", "Servicios contables y jurídicos",       1_760m, "USD", 1m, 1_760m,  new DateOnly(2026, m, 10), categoryId: 7),
                CashFlowEntry.CreateManual(oid, "expense", "Seguridad social y parafiscales",         560m, "USD", 1m,   560m,  new DateOnly(2026, m, 5),  categoryId: 6),
            ]);
        }

        // Ingresos por facturas pagadas
        // inv1 — TechCorp: $30,000 USD pagada feb 2025 (pero mapeamos a 2026 para que el dashboard tenga datos útiles)
        cashEntries.Add(CashFlowEntry.CreateFromInvoice(oid, inv1.Id, $"Pago {inv1.InvoiceNumber} — TechCorp Solutions",  30_000m, "USD", 1m, 30_000m, new DateOnly(2026, 2, 28)));
        // inv2 — BancoGreen: $14,000 USD pagada mar 2025
        cashEntries.Add(CashFlowEntry.CreateFromInvoice(oid, inv2.Id, $"Pago {inv2.InvoiceNumber} — BancoGreen",          14_000m, "USD", 1m, 14_000m, new DateOnly(2026, 3, 31)));
        // inv6 — RetailMax: $28,000 USD pagada, remapeada a may 2026
        cashEntries.Add(CashFlowEntry.CreateFromInvoice(oid, inv6.Id, $"Pago {inv6.InvoiceNumber} — RetailMax",           28_000m, "USD", 1m, 28_000m, new DateOnly(2026, 5, 15)));

        _db.CashFlowEntries.AddRange(cashEntries);
        await _db.SaveChangesAsync(ct);

        // ── Project Milestones ────────────────────────────────────────────────
        _db.ProjectMilestones.AddRange(
            ProjectMilestone.Create(oid, prjPortal.Id, "Diseño UI/UX aprobado",        7000m, new DateOnly(2026, 2, 15), "DONE"),
            ProjectMilestone.Create(oid, prjPortal.Id, "Backend auth y API core",       8400m, new DateOnly(2026, 3, 30), "DONE"),
            ProjectMilestone.Create(oid, prjPortal.Id, "Módulo de transacciones",       6300m, new DateOnly(2026, 6, 15), "IN_PROGRESS"),
            ProjectMilestone.Create(oid, prjPortal.Id, "Integración pasarela de pago",  4200m, new DateOnly(2026, 7, 31), "PENDING"),
            ProjectMilestone.Create(oid, prjPortal.Id, "QA y despliegue producción",    2100m, new DateOnly(2026, 8, 30), "PENDING"),

            ProjectMilestone.Create(oid, prjData.Id, "Análisis y arquitectura",  25000m, new DateOnly(2025, 12, 15), "DONE"),
            ProjectMilestone.Create(oid, prjData.Id, "Plataforma ETL fase 1",    35000m, new DateOnly(2026,  2, 28), "DONE"),
            ProjectMilestone.Create(oid, prjData.Id, "Módulo de reportes",       20000m, new DateOnly(2026,  5, 15), "IN_PROGRESS"),
            ProjectMilestone.Create(oid, prjData.Id, "Go-live y documentación",  15000m, new DateOnly(2026,  6, 30), "PENDING"),

            ProjectMilestone.Create(oid, prjMobile.Id, "Diseño y prototipado",     18000m, new DateOnly(2025, 10, 31), "DONE"),
            ProjectMilestone.Create(oid, prjMobile.Id, "Módulo de autenticación",  25000m, new DateOnly(2025, 12, 31), "DONE"),
            ProjectMilestone.Create(oid, prjMobile.Id, "Core financiero",          40000m, new DateOnly(2026,  4, 30), "IN_PROGRESS"),
            ProjectMilestone.Create(oid, prjMobile.Id, "QA y lanzamiento",         37000m, new DateOnly(2026,  6, 30), "PENDING"),

            ProjectMilestone.Create(oid, prjEcomm.Id, "Auditoría y arquitectura",  8000m, new DateOnly(2025, 12, 31), "DONE"),
            ProjectMilestone.Create(oid, prjEcomm.Id, "Rediseño frontend",        20000m, new DateOnly(2026,  3, 31), "IN_PROGRESS"),
            ProjectMilestone.Create(oid, prjEcomm.Id, "Integración pagos y QA",   17000m, new DateOnly(2026,  4, 30), "PENDING"),

            ProjectMilestone.Create(oid, prjAudit.Id, "Reconocimiento y análisis", 10000m, new DateOnly(2026, 1, 31), "DONE"),
            ProjectMilestone.Create(oid, prjAudit.Id, "Pruebas de penetración",    12000m, new DateOnly(2026, 2, 28), "IN_PROGRESS"),
            ProjectMilestone.Create(oid, prjAudit.Id, "Informe y remediación",     10000m, new DateOnly(2026, 3, 31), "PENDING"),

            ProjectMilestone.Create(oid, prjMktg.Id, "Definición y diseño UI",     8000m, new DateOnly(2025,  9, 30), "DONE"),
            ProjectMilestone.Create(oid, prjMktg.Id, "Desarrollo del dashboard",  12000m, new DateOnly(2025, 11, 30), "DONE"),
            ProjectMilestone.Create(oid, prjMktg.Id, "Integración y entrega",      8000m, new DateOnly(2025, 12, 31), "DONE")
        );
        await _db.SaveChangesAsync(ct);

        // ── Project Deliverables ──────────────────────────────────────────────
        var seedMilestones = _db.ProjectMilestones.Local.Where(m => m.ProjectId == prjPortal.Id).ToList();
        var msUx    = seedMilestones.FirstOrDefault(m => m.Name == "Diseño UI/UX aprobado")?.Id;
        var msBackend = seedMilestones.FirstOrDefault(m => m.Name == "Backend auth y API core")?.Id;
        var msTx    = seedMilestones.FirstOrDefault(m => m.Name == "Módulo de transacciones")?.Id;
        var msQa    = seedMilestones.FirstOrDefault(m => m.Name == "QA y despliegue producción")?.Id;

        _db.ProjectDeliverables.AddRange(
            ProjectDeliverable.Create(oid, prjPortal.Id, "Documento de arquitectura técnica",    "Documento", new DateOnly(2026, 2, 10), msUx,      "APPROVED",  "Cliente TechCorp"),
            ProjectDeliverable.Create(oid, prjPortal.Id, "Diseños UI aprobados (Figma)",         "Diseño",    new DateOnly(2026, 2, 15), msUx,      "APPROVED",  "Cliente TechCorp"),
            ProjectDeliverable.Create(oid, prjPortal.Id, "API REST documentada (Swagger)",       "Documento", new DateOnly(2026, 3, 25), msBackend, "APPROVED",  "Cliente TechCorp"),
            ProjectDeliverable.Create(oid, prjPortal.Id, "Módulo transacciones (código fuente)", "Código",    new DateOnly(2026, 6, 15), msTx,      "IN_REVIEW",  null),
            ProjectDeliverable.Create(oid, prjPortal.Id, "Manual de usuario v1",                 "Documento", new DateOnly(2026, 6, 30), msTx,      "PENDING",    null),
            ProjectDeliverable.Create(oid, prjPortal.Id, "Informe QA final",                     "Documento", new DateOnly(2026, 8, 20), msQa,      "PENDING",    null)
        );
        await _db.SaveChangesAsync(ct);

        // ── Project Billing Entries ───────────────────────────────────────────
        _db.ProjectBillingEntries.AddRange(
            ProjectBillingEntry.Create(oid, prjPortal.Id, "Cobro mensual — Enero 2026",   "ene 2026", 22m, new DateOnly(2026, 1, 31), 80,   1760m, "PAID",     "FAC-0071"),
            ProjectBillingEntry.Create(oid, prjPortal.Id, "Cobro mensual — Febrero 2026", "feb 2026", 22m, new DateOnly(2026, 2, 28), 90,   1980m, "PAID",     "FAC-0078"),
            ProjectBillingEntry.Create(oid, prjPortal.Id, "Cobro mensual — Marzo 2026",   "mar 2026", 22m, new DateOnly(2026, 3, 31), 85,   1870m, "PAID",     "FAC-0085"),
            ProjectBillingEntry.Create(oid, prjPortal.Id, "Cobro mensual — Abril 2026",   "abr 2026", 22m, new DateOnly(2026, 4, 30), 78,   1716m, "PAID",     "FAC-0091"),
            ProjectBillingEntry.Create(oid, prjPortal.Id, "Cobro mensual — Mayo 2026",    "may 2026", 22m, new DateOnly(2026, 5, 31), 79,   1738m, "INVOICED", "FAC-0098"),
            ProjectBillingEntry.Create(oid, prjPortal.Id, "Cobro mensual — Junio 2026",   "jun 2026", 22m, new DateOnly(2026, 6, 30), null, null,   "PENDING",  null)
        );
        await _db.SaveChangesAsync(ct);

        // ── Project Files ─────────────────────────────────────────────────────
        _db.ProjectFiles.AddRange(
            ProjectFile.Create(oid, prjPortal.Id, "Contrato_TechCorp_Portal.pdf", "pdf", "245 KB", "MV", new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)),
            ProjectFile.Create(oid, prjPortal.Id, "Propuesta_Tecnica_v2.docx",    "doc", "128 KB", "LP", new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc)),
            ProjectFile.Create(oid, prjPortal.Id, "Wireframes_UI_Portal.fig",     "fig", "8.4 MB", "LP", new DateTime(2026, 2,  1, 0, 0, 0, DateTimeKind.Utc)),
            ProjectFile.Create(oid, prjPortal.Id, "API_Spec_v1.0.yaml",           "yml",  "67 KB", "JS", new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)),
            ProjectFile.Create(oid, prjPortal.Id, "Acta_Reunion_Kick_Off.pdf",    "pdf",  "92 KB", "MV", new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc))
        );
        await _db.SaveChangesAsync(ct);

        // ── Project Activity Log ──────────────────────────────────────────────
        _db.ProjectActivityLogs.AddRange(
            ProjectActivityLog.Create(oid, prjPortal.Id, "MV", "Marcó hito 'Backend auth y API core' como completado",    "Hitos",       new DateTime(2026, 4,  1, 14, 32, 0, DateTimeKind.Utc)),
            ProjectActivityLog.Create(oid, prjPortal.Id, "LP", "Subió archivo 'Wireframes_UI_Portal.fig'",                "Archivos",    new DateTime(2026, 2,  1, 10, 15, 0, DateTimeKind.Utc)),
            ProjectActivityLog.Create(oid, prjPortal.Id, "JS", "Actualizó progreso de tarea 'API endpoint transacciones'","Tareas",      new DateTime(2026, 6,  3, 16, 45, 0, DateTimeKind.Utc)),
            ProjectActivityLog.Create(oid, prjPortal.Id, "MV", "Registró cobro mensual Mayo 2026 como FACTURADO",         "Cobros",      new DateTime(2026, 5, 30,  9, 20, 0, DateTimeKind.Utc)),
            ProjectActivityLog.Create(oid, prjPortal.Id, "LP", "Añadió entregable 'Manual de usuario v1'",                "Entregables", new DateTime(2026, 5, 15, 11,  0, 0, DateTimeKind.Utc)),
            ProjectActivityLog.Create(oid, prjPortal.Id, "JS", "Creó tarea 'Setup CI/CD pipeline' asignada a MV",        "Tareas",      new DateTime(2026, 5, 10,  8, 30, 0, DateTimeKind.Utc)),
            ProjectActivityLog.Create(oid, prjPortal.Id, "MV", "Actualizó el sprint de Sprint 5 a Sprint 6",             "Proyecto",    new DateTime(2026, 4, 28, 17,  0, 0, DateTimeKind.Utc))
        );
        await _db.SaveChangesAsync(ct);

        // ── Integrations ─────────────────────────────────────────────────────
        var slackConn = OrganizationIntegration.Create(oid, "slack");
        slackConn.Connect(user.UserId, connectedAs: "#techfactory-co");
        _db.OrganizationIntegrations.Add(slackConn);
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedBillingAsync(CancellationToken ct)
    {
        var org = await _db.Organizations
            .FirstOrDefaultAsync(o => o.Name == "TechFactory CO", ct);
        if (org is null) return;

        var oid = org.OrganizationId;
        if (await _db.BillingCustomers.AnyAsync(c => c.OrganizationId == oid, ct)) return;

        var studioPlan = await _db.BillingPlans
            .FirstOrDefaultAsync(p => p.Name == Plan.Studio, ct);
        if (studioPlan is null) return;

        var customer = BillingCustomer.Create(oid, "cus_dev_techfactory_2026",
            "dev@zentorysoftware.com", "TechFactory CO");
        _db.BillingCustomers.Add(customer);
        await _db.SaveChangesAsync(ct);

        var sub = Subscription.Create(oid, customer.Id, studioPlan.Id, "monthly");
        sub.Activate("sub_dev_studio_2026",
            periodStart: new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc),
            periodEnd:   new DateTime(2026, 7, 4, 0, 0, 0, DateTimeKind.Utc));
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync(ct);

        var inv1 = BillingInvoice.Create(oid, customer.Id, 89m, "USD", sub.Id);
        inv1.SetInvoiceNumber("INV-2026-04");
        inv1.MarkPaid(new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc));

        var inv2 = BillingInvoice.Create(oid, customer.Id, 89m, "USD", sub.Id);
        inv2.SetInvoiceNumber("INV-2026-05");
        inv2.MarkPaid(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc));

        var inv3 = BillingInvoice.Create(oid, customer.Id, 89m, "USD", sub.Id);
        inv3.SetInvoiceNumber("INV-2026-06");
        inv3.MarkPaid(new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc));

        _db.BillingInvoices.AddRange(inv1, inv2, inv3);
        await _db.SaveChangesAsync(ct);
    }

    private static TimeEntry Make(
        Guid orgId, Guid projectId, Guid collaboratorId,
        DateOnly date, decimal hours, decimal rate, string description)
        => TimeEntry.Create(orgId, projectId, date, hours, rate, "COP",
            billable: true, rateBilled: rate, description: description,
            collaboratorId: collaboratorId);
}
