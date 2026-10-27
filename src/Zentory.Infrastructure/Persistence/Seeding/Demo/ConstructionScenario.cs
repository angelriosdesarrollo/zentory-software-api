using Zentory.Domain.Constants;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// Constructora Interventoría y Gestión Pública SAS (§3.0.A): empresa · pro, WorkType ObraPublica. Su owner es otra
/// persona, con suscripción pro propia; la persona principal es admin aquí y ve el plan del owner, no el suyo. Comparte
/// con la Agencia a la colaboradora del portal (mismo email en las dos organizaciones).
/// </summary>
public sealed class ConstructionScenario : IDemoScenario
{
    public const string OrganizationName = "Interventoría y Gestión Pública SAS";

    public static readonly Guid OrganizationId = new("a1000000-0000-0000-0000-000000000004");

    public static readonly Guid AqueductProjectId = new("c3000000-0007-0000-0000-000000000007");
    public static readonly Guid RoadsProjectId    = new("c3000000-0008-0000-0000-000000000008");
    public static readonly Guid HospitalProjectId = new("c3000000-0009-0000-0000-000000000009");

    public string Name => OrganizationName;

    public async Task SeedAsync(DemoSeedContext context, CancellationToken ct = default)
    {
        var owner     = DemoPeople.ConstructionOwner;
        var principal = DemoPeople.Principal;
        await context.EnsureUserAsync(owner, OrgRole.Owner, ct);
        await context.EnsureUserAsync(principal, OrgRole.Owner, ct);

        await context.EnsureOrganizationAsync(OrganizationId, OrganizationName, LegalType.Empresa, owner.Id, ct);
        await context.EnsureMembershipAsync(OrganizationId, owner.Id, OrgRole.Owner, -500, ct);
        await context.EnsureMembershipAsync(OrganizationId, principal.Id, OrgRole.Admin, -260, ct);

        await context.EnsureSettingsAsync(OrganizationId, new Dictionary<string, string>
        {
            ["currency.primary"]        = "COP",
            ["currency.reports"]        = "COP",
            ["number.format"]           = "latam",
            ["symbol.position"]         = "before",
            ["timezone"]                = "America/Bogota",
            ["date.format"]             = "DD/MM/YYYY",
            ["language"]                = "es",
            ["week.first_day"]          = "monday",
            ["invoice.payment_method"]  = "Transferencia bancaria",
            ["proposal.validity_days"]  = "30",
            ["proposal.revision_count"] = "3",
            ["invoice.currency"]        = "COP",
            ["profile.work_type"]       = WorkType.ObraPublica,
        }, ct);

        await context.EnsureActiveSubscriptionAsync(owner, OrganizationId, OrganizationName, Plan.Pro, ct);

        var s = new DemoOrganizationSeed(context, OrganizationId);

        // ── Clientes (entes públicos y un programa con financiación multilateral) ─────────────────────────────
        var aguazul  = s.Client("aguazul",  "Alcaldía de Aguazul",                               "Ing. Rafael Torres",  "rafael.torres",   "+57 320 111 2233", "Aguazul",       "891.856.842-1");
        var casanare = s.Client("casanare", "Gobernación de Casanare",                           "Dra. Laura Beltrán",  "laura.beltran",   "+57 320 222 3344", "Yopal",         "800.103.913-2");
        var yopal    = s.Client("yopal",    "Alcaldía de Yopal",                                 "Ing. Camilo Rincón",  "camilo.rincon",   "+57 320 333 4455", "Yopal",         "891.855.999-3");
        var eaav     = s.Client("eaav",     "Acueducto y Alcantarillado de Villavicencio (EAAV)", "Ing. Patricia Gómez", "patricia.gomez",  "+57 320 444 5566", "Villavicencio", "892.099.101-4");
        var water    = s.Client("water",    "Fondo Agua Rural (programa BID)",                   "Michael Grant",       "michael.grant",   "+57 320 555 6677", "Bogotá",        "900.987.654-5");
        await context.AddMissingAsync([aguazul, casanare, yopal, eaav, water], ct);

        // ── Colaboradores ───────────────────────────────────────────────────────────────────────────────────────
        var rodrigo = s.Collaborator("rodrigo", "Rodrigo Fajardo",  "fixed_contractor",  "Interventor / Ingeniero residente", monthlyRate: 9_500_000m, email: DemoPeople.Email("rodrigo.fajardo"));
        var diana   = s.Collaborator("diana",   "Diana Cárdenas",   "hourly_contractor", "Especialista social",               hourlyRate: 85_000m,    email: DemoPeople.Email("diana.cardenas"));
        var felipe  = s.Collaborator("felipe",  "Felipe Contreras", "hourly_contractor", "Especialista ambiental",            hourlyRate: 90_000m,    email: DemoPeople.Email("felipe.contreras"));
        var marcela = s.Collaborator("marcela", "Marcela Uribe",    "fixed_contractor",  "Coordinadora financiera",           monthlyRate: 6_200_000m, email: DemoPeople.Email("marcela.uribe"));
        // La misma persona que en la Agencia, con el mismo email: el portal le muestra las dos organizaciones.
        var laura   = s.Collaborator("laura",   DemoPeople.PortalCollaboratorName, "hourly_contractor", "Analista de datos de obra", hourlyRate: 90_000m, email: DemoPeople.PortalCollaboratorEmail);
        await context.AddMissingAsync([rodrigo, diana, felipe, marcela, laura], ct);

        // ── Proyectos ───────────────────────────────────────────────────────────────────────────────────────────
        var aqueduct = s.Project(AqueductProjectId, aguazul.Id,  "Interventoría Acueducto Regional Aguazul",    BillingType.Milestone,  420_000_000m, "COP", 2200, -180,  190, WorkType.ObraPublica, hoursUsed: 900);
        var roads    = s.Project(RoadsProjectId,    casanare.Id, "Formulación BPIN Vías Terciarias Casanare",   BillingType.FixedPrice, 180_000_000m, "COP",  900, -280,  -20, WorkType.ObraPublica, ProjectStatus.Completed, 880);
        // Activo y atrasado: la supervisión debía cerrar hace dos semanas.
        var hospital = s.Project(HospitalProjectId, yopal.Id,    "Supervisión Técnica Hospital Regional Yopal", BillingType.Hourly,     260_000_000m, "COP", 1500, -240,  -15, WorkType.ObraPublica, hoursUsed: 1460);
        var rural    = s.Project(s.Id("project:rural-water"), water.Id, "Supervisión Programa Agua Rural", BillingType.FixedPrice, 85_000m, "USD", 600, -90, 120, WorkType.ObraPublica, ProjectStatus.Paused, 150);
        var park     = s.Project(s.Id("project:linear-park"), yopal.Id, "Estudios Parque Lineal Yopal",    BillingType.FixedPrice, 60_000_000m, "COP", 300, -130, -10, WorkType.ObraPublica, ProjectStatus.Cancelled, 60);
        await context.AddMissingAsync([aqueduct, roads, hospital, rural, park], ct);

        await context.AddMissingAsync(
        [
            s.Assign(aqueduct.Id, rodrigo), s.Assign(aqueduct.Id, diana), s.Assign(aqueduct.Id, felipe), s.Assign(aqueduct.Id, marcela),
            s.Assign(aqueduct.Id, laura),
            s.Assign(hospital.Id, rodrigo), s.Assign(hospital.Id, diana, 120_000m), s.Assign(hospital.Id, laura, 130_000m),
            s.Assign(roads.Id, marcela), s.Assign(rural.Id, felipe), s.Assign(park.Id, felipe),
        ], ct);

        // ── Propuestas ──────────────────────────────────────────────────────────────────────────────────────────
        await context.AddMissingAsync(
        [
            s.Proposal("eaav-phase-2", eaav.Id, "Interventoría alcantarillado EAAV — fase 2", "COP", "viewed", -4, 30,
                ("Diagnóstico y estudios previos", 35_000_000m), ("Formulación y estructuración BPIN", 48_000_000m),
                ("Interventoría técnica y financiera (12 meses)", 210_000_000m)),
            s.Proposal("hospital-scope", yopal.Id, "Ampliación de alcance — supervisión hospital", "COP", "accepted", -60, 30,
                ("Supervisión adicional de equipos biomédicos", 42_000_000m)),
            s.Proposal("school", aguazul.Id, "Interventoría Institución Educativa Monterrey", "COP", "draft", 0, 30,
                ("Interventoría técnica (8 meses)", 96_000_000m)),
            s.Proposal("park-phase-2", yopal.Id, "Estudios Parque Lineal — fase 2", "COP", "rejected", -20, 30,
                ("Estudios de detalle", 38_000_000m)),
            s.Proposal("rural-water", water.Id, "Supervisión Programa Agua Rural", "USD", "accepted", -100, 30,
                ("Supervisión técnica", 60_000m), ("Informes al financiador", 25_000m)),
        ], ct);

        // ── Facturas ────────────────────────────────────────────────────────────────────────────────────────────
        const string invoiceType = "factura";
        var fe2101 = s.Invoice("FE-2101", aguazul.Id,  aqueduct.Id, "COP", invoiceType, -150, -120, DemoInvoiceState.Paid,
            ("Cobro hito 1 — Diagnóstico", 1m, 40_000_000m));
        var fe2102 = s.Invoice("FE-2102", aguazul.Id,  aqueduct.Id, "COP", invoiceType, -95, -65, DemoInvoiceState.Paid,
            ("Cobro hito 2 — Formulación", 1m, 55_000_000m));
        var fe2103 = s.Invoice("FE-2103", aguazul.Id,  aqueduct.Id, "COP", invoiceType, -8, 22, DemoInvoiceState.Sent,
            ("Cobro hito 3 — Presupuesto", 1m, 60_000_000m));
        var fe2104 = s.Invoice("FE-2104", casanare.Id, roads.Id,    "COP", invoiceType, -40, -20, DemoInvoiceState.Overdue,
            ("Formulación BPIN — entrega final", 1m, 90_000_000m));
        var fe2105 = s.Invoice("FE-2105", yopal.Id,    hospital.Id, "COP", invoiceType, -35, -5, DemoInvoiceState.Partial,
            ("Supervisión técnica — horas del periodo", 120m, 95_000m));
        var fe2106 = s.Invoice("FE-2106", aguazul.Id,  aqueduct.Id, "COP", invoiceType, 0, 30, DemoInvoiceState.Draft,
            ("Cobro hito 4 — Documentación", 1m, 45_000_000m));
        var fe2107 = s.Invoice("FE-2107", water.Id,    rural.Id,    "USD", invoiceType, -85, -55, DemoInvoiceState.Paid,
            ("Anticipo 30 % — supervisión", 1m, 25_500m));
        var fe2108 = s.Invoice("FE-2108", yopal.Id,    park.Id,     "COP", invoiceType, -110, -80, DemoInvoiceState.Paid,
            ("Estudios preliminares", 1m, 12_000_000m));
        var fe2099 = s.Invoice("FE-2099", casanare.Id, roads.Id,    "COP", invoiceType, -200, -170, DemoInvoiceState.Paid,
            ("Formulación BPIN — anticipo 50 %", 1m, 90_000_000m));
        await context.AddMissingInvoicesAsync(OrganizationId, [fe2099, fe2101, fe2102, fe2103, fe2104, fe2105, fe2106, fe2107, fe2108], ct);
        await context.AddMissingForInvoicesAsync(
        [
            s.Payment(fe2099, -172), s.Payment(fe2101, -122), s.Payment(fe2102, -66), s.Payment(fe2105, -7),
            s.Payment(fe2107, -57), s.Payment(fe2108, -82),
        ], p => p.InvoiceId, ct);

        // ── Horas ───────────────────────────────────────────────────────────────────────────────────────────────
        await context.AddMissingAsync(
        [
            s.Time("aqueduct-diana-1",  aqueduct.Id, diana,   -1, 6m, 85_000m, "Socialización con la comunidad — vereda La Vigía"),
            s.Time("aqueduct-felipe-1", aqueduct.Id, felipe,  -2, 5m, 90_000m, "Visita técnica a la línea de conducción"),
            s.Time("hospital-rodrigo-1", hospital.Id, rodrigo, -1, 8m, 95_000m, "Supervisión de obra civil — bloque quirúrgico"),
            s.Time("hospital-laura-1",  hospital.Id, laura,   -3, 4m, 90_000m, "Tablero de avance físico y financiero"),
            s.Time("aqueduct-rodrigo-1", aqueduct.Id, rodrigo, -9, 8m, 95_000m, "Informe mensual de interventoría", "approved"),
            s.Time("hospital-diana-1",  hospital.Id, diana,  -11, 4m, 85_000m, "Mesa de trabajo con la comunidad hospitalaria", "approved"),
            s.Time("hospital-rodrigo-2", hospital.Id, rodrigo, -38, 8m, 95_000m, "Supervisión del periodo anterior", "billed"),
        ], ct);

        // ── Hitos: las siete dimensiones del proyecto insignia ──────────────────────────────────────────────────
        var msDiagnosis = s.Milestone("aqueduct-diagnosis", aqueduct.Id, "Diagnóstico",     40_000_000m, -150, "DONE");
        var msFormulate = s.Milestone("aqueduct-formulate", aqueduct.Id, "Formulación",     55_000_000m,  -95, "DONE");
        var msBudget    = s.Milestone("aqueduct-budget",    aqueduct.Id, "Presupuesto",     60_000_000m,  -60, "DONE");
        var msDocs      = s.Milestone("aqueduct-docs",      aqueduct.Id, "Documentación",   45_000_000m,   10, "IN_PROGRESS");
        var msLogistics = s.Milestone("aqueduct-logistics", aqueduct.Id, "Logística",       70_000_000m,   40, "IN_PROGRESS");
        var msBuild     = s.Milestone("aqueduct-build",     aqueduct.Id, "Implementación", 100_000_000m,  120, "PENDING");
        var msCollect   = s.Milestone("aqueduct-collect",   aqueduct.Id, "Cobros",          50_000_000m,  190, "PENDING");
        await context.AddMissingAsync([msDiagnosis, msFormulate, msBudget, msDocs, msLogistics, msBuild, msCollect], ct);

        await context.AddMissingAsync(
        [
            s.Task("aqueduct-policies",  aqueduct.Id, "Revisar pólizas de cumplimiento actualizadas",    "todo",        "high",   marcela,  8, msDocs),
            s.Task("aqueduct-env-visit", aqueduct.Id, "Programar visita de seguimiento ambiental",       "todo",        "medium", felipe,  12, msLogistics),
            s.Task("aqueduct-logistics", aqueduct.Id, "Consolidar plan logístico de adquisiciones",      "in_progress", "high",   rodrigo,  6, msLogistics),
            s.Task("aqueduct-report",    aqueduct.Id, "Informe mensual de interventoría",                "review",      "high",   rodrigo,  2, msDocs),
            s.Task("aqueduct-budget",    aqueduct.Id, "Aprobar presupuesto detallado del contrato",      "done",        "high",   marcela, -60, msBudget),
            s.Task("hospital-equipment", hospital.Id, "Verificar entrega de equipos biomédicos",         "in_progress", "high",   diana,  -10),
            s.Task("hospital-closing",   hospital.Id, "Acta de recibo final de obra",                    "todo",        "high",   rodrigo, -2),
            s.Task("hospital-dashboard", hospital.Id, "Actualizar tablero de avance",                    "review",      "medium", laura,    1),
            s.Task("roads-delivery",     roads.Id,    "Radicar ficha BPIN en la plataforma del DNP",     "done",        "high",   marcela, -25),
            s.Task("rural-report",       rural.Id,    "Informe trimestral al financiador",               "todo",        "medium", felipe,  30),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Deliverable("aqueduct-studies",   aqueduct.Id, "Estudios previos y diagnóstico técnico",          "Documento", -152, msDiagnosis, "APPROVED", aguazul.Name),
            s.Deliverable("aqueduct-bpin",      aqueduct.Id, "Ficha BPIN y formulación del proyecto",           "Documento",  -97, msFormulate, "APPROVED", aguazul.Name),
            s.Deliverable("aqueduct-budget",    aqueduct.Id, "Presupuesto detallado y flujo de caja",           "Documento",  -62, msBudget,    "APPROVED", aguazul.Name),
            s.Deliverable("aqueduct-policies",  aqueduct.Id, "Pólizas de cumplimiento y responsabilidad civil", "Documento",    8, msDocs,      "IN_REVIEW"),
            s.Deliverable("aqueduct-logistics", aqueduct.Id, "Plan logístico y cronograma de adquisiciones",    "Documento",   38, msLogistics, "PENDING"),
            s.Deliverable("aqueduct-monthly",   aqueduct.Id, "Informe mensual de interventoría de obra",        "Documento",  118, msBuild,     "PENDING"),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Billing("aqueduct-1", aqueduct.Id, "Cobro hito 1 — Diagnóstico",   40_000_000m, -150, 1, 40_000_000m, "PAID",     fe2101.InvoiceNumber),
            s.Billing("aqueduct-2", aqueduct.Id, "Cobro hito 2 — Formulación",   55_000_000m,  -95, 1, 55_000_000m, "PAID",     fe2102.InvoiceNumber),
            s.Billing("aqueduct-3", aqueduct.Id, "Cobro hito 3 — Presupuesto",   60_000_000m,   -8, 1, 60_000_000m, "INVOICED", fe2103.InvoiceNumber),
            s.Billing("aqueduct-4", aqueduct.Id, "Cobro hito 4 — Documentación", 45_000_000m,   15, null, null,     "PENDING"),
        ], ct);

        await context.AddMissingAsync(
        [
            s.File("contrato-interventoria-aguazul", aqueduct.Id, "Contrato_Interventoria_Aguazul.pdf", "pdf", "310 KB", "RF", -178),
            s.File("polizas-cumplimiento",           aqueduct.Id, "Polizas_Cumplimiento.pdf",           "pdf", "180 KB", "MU", -170),
            s.File("acta-de-inicio",                 aqueduct.Id, "Acta_de_Inicio.pdf",                 "pdf", "95 KB",  "RF", -175),
            s.File("ficha-bpin-v1",                  aqueduct.Id, "Ficha_BPIN_v1.pdf",                  "pdf", "420 KB", "MU", -97),
        ], ct);
        await context.AddMissingAsync(
        [
            s.Activity("aqueduct-1", aqueduct.Id, "RF", "Marcó el hito 'Presupuesto' como completado", "Hitos",    -60, 9),
            s.Activity("aqueduct-2", aqueduct.Id, "MU", "Registró el pago de la factura FE-2102",      "Cobros",   -66, 10),
            s.Activity("aqueduct-3", aqueduct.Id, "RF", "Subió 'Ficha_BPIN_v1.pdf'",                   "Archivos", -97, 14),
            s.Activity("hospital-1", hospital.Id, "DC", "Reportó retraso en la entrega de equipos",    "Tareas",   -10, 16),
        ], ct);

        await context.AddMissingAsync(
        [
            .. s.MonthlyExpense("resident",    "Honorarios interventor residente",  9_500_000m, "COP", 2, 6),
            .. s.MonthlyExpense("specialists", "Honorarios especialistas técnicos", 3_600_000m, "COP", 7, 6),
            .. s.MonthlyExpense("policies",    "Pólizas y garantías",                 560_000m, "COP", 9, 6, -7),
            .. s.MonthlyExpense("travel",      "Viáticos de supervisión en campo",        900m, "USD", 9, 3, -12),
        ], ct);
        await context.AddMissingForInvoicesAsync(
        [
            s.Income(fe2099, casanare.Name, -172), s.Income(fe2101, aguazul.Name, -122), s.Income(fe2102, aguazul.Name, -66),
            s.Income(fe2105, yopal.Name, -7), s.Income(fe2107, water.Name, -57), s.Income(fe2108, yopal.Name, -82),
        ], c => c.InvoiceId, ct);
    }
}
