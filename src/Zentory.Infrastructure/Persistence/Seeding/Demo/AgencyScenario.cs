using Zentory.Domain.Constants;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// Agencia TechFactory CO (§3.0.A): empresa · studio (el plan es el de la persona principal, su owner), WorkType
/// Software con proyectos de software y de marketing. Tiene admin y member propios. Es la organización de desarrollo:
/// <c>ProjectExpenseStore</c>, <c>DevToolsController</c> y los tests usan su ID y el de sus proyectos (RF-56 g).
/// Al final siembra la invitación pendiente de <see cref="PendingInvitationScenario"/> (RF-68).
/// </summary>
public sealed class AgencyScenario : IDemoScenario
{
    public const string OrganizationName = "TechFactory CO";

    public static readonly Guid OrganizationId = new("a1000000-0000-0000-0000-000000000001");

    // Los tres primeros los lee ProjectExpenseStore: no cambian de ID ni de organización.
    public static readonly Guid PortalProjectId    = new("c3000000-0001-0000-0000-000000000001");
    public static readonly Guid MobileProjectId    = new("c3000000-0002-0000-0000-000000000002");
    public static readonly Guid DataProjectId      = new("c3000000-0003-0000-0000-000000000003");
    public static readonly Guid EcommerceProjectId = new("c3000000-0004-0000-0000-000000000004");
    public static readonly Guid AuditProjectId     = new("c3000000-0005-0000-0000-000000000005");
    public static readonly Guid CampaignProjectId  = new("c3000000-0006-0000-0000-000000000006");

    public string Name => OrganizationName;

    public async Task SeedAsync(DemoSeedContext context, CancellationToken ct = default)
    {
        var principal = DemoPeople.Principal;
        await context.EnsureUserAsync(principal, OrgRole.Owner, ct);
        await context.EnsureUserAsync(DemoPeople.AgencyAdmin, OrgRole.Admin, ct);
        await context.EnsureUserAsync(DemoPeople.AgencyMember, OrgRole.Member, ct);

        await context.EnsureOrganizationAsync(OrganizationId, OrganizationName, LegalType.Empresa, principal.Id, ct);
        // La persona principal entra primero aquí: el login la deja en la Agencia (owner con la membresía más antigua).
        await context.EnsureMembershipAsync(OrganizationId, principal.Id, OrgRole.Owner, -420, ct);
        await context.EnsureMembershipAsync(OrganizationId, DemoPeople.AgencyAdmin.Id, OrgRole.Admin, -300, ct);
        await context.EnsureMembershipAsync(OrganizationId, DemoPeople.AgencyMember.Id, OrgRole.Member, -120, ct);

        await context.EnsureSettingsAsync(OrganizationId, new Dictionary<string, string>
        {
            ["currency.primary"]        = "COP",
            ["currency.reports"]        = "USD",
            ["number.format"]           = "latam",
            ["symbol.position"]         = "before",
            ["timezone"]                = "America/Bogota",
            ["date.format"]             = "DD/MM/YYYY",
            ["language"]                = "es",
            ["week.first_day"]          = "monday",
            ["invoice.payment_method"]  = "Transferencia bancaria",
            ["proposal.validity_days"]  = "30",
            ["proposal.revision_count"] = "3",
            ["invoice.currency"]        = "USD",
            ["profile.work_type"]       = WorkType.Software,
        }, ct);

        // El plan studio es del owner y lo heredan la Agencia y su marca freelance (RF-56 e).
        await context.EnsureActiveSubscriptionAsync(principal, OrganizationId, OrganizationName, Plan.Studio, ct);

        var s = new DemoOrganizationSeed(context, OrganizationId);

        // ── Clientes ────────────────────────────────────────────────────────────────────────────────────────────
        var techCorp  = s.Client("techcorp",  "TechCorp Solutions", "Ana Martínez",  "ana.martinez",  "+57 310 111 1111", "Bogotá",       "900.111.222-1");
        var bancoGreen = s.Client("bancogreen", "BancoGreen",       "Pedro Sánchez", "pedro.sanchez", "+57 310 222 2222", "Medellín",     "900.222.333-2");
        var logistica = s.Client("logistica", "Logística Express",  "María López",   "maria.lopez",   "+57 310 333 3333", "Cali",         "900.333.444-3");
        var mediaSol  = s.Client("mediasol",  "MediaSol",           "Juan Torres",   "juan.torres",   "+57 310 444 4444", "Barranquilla", "900.444.555-4");
        var innova    = s.Client("innova",    "InnovaGroup",        "Sofía Ramírez", "sofia.ramirez", "+57 310 555 5555", "Bogotá",       "900.555.666-5");
        var retailMax = s.Client("retailmax", "RetailMax",          "Carlos Díaz",   "carlos.diaz",   "+57 310 666 6666", "Medellín",     "900.666.777-6");
        await context.AddMissingAsync([techCorp, bancoGreen, logistica, mediaSol, innova, retailMax], ct);

        // ── Colaboradores ───────────────────────────────────────────────────────────────────────────────────────
        // Todos con correo de prueba: DevToolsController y el portal (magic link) toman uno de esta organización.
        var mateo   = s.Collaborator("mateo",   "Mateo Vélez",   "hourly_contractor", "Desarrollador Frontend",   hourlyRate: 85_000m,  email: DemoPeople.Email("mateo.velez"));
        var laura   = s.Collaborator("laura",   DemoPeople.PortalCollaboratorName, "hourly_contractor", "Analista de datos", hourlyRate: 90_000m, email: DemoPeople.PortalCollaboratorEmail);
        var juan    = s.Collaborator("juan",    "Juan Sánchez",  "hourly_contractor", "Desarrollador Full Stack", hourlyRate: 95_000m,  email: DemoPeople.Email("juan.sanchez"));
        var carlos  = s.Collaborator("carlos",  "Carlos Reyes",  "fixed_contractor",  "DevOps",                   monthlyRate: 9_500_000m, email: DemoPeople.Email("carlos.reyes"));
        var valeria = s.Collaborator("valeria", "Valeria Gómez", "hourly_contractor", "Diseñadora UX y contenidos", hourlyRate: 75_000m, email: DemoPeople.Email("valeria.gomez"));
        var daniel  = s.Collaborator("daniel",  "Daniel Ortiz",  "hourly_contractor", "Arquitecto cloud (remoto)", "USD", hourlyRate: 45m, email: DemoPeople.Email("daniel.ortiz"));
        await context.AddMissingAsync([mateo, laura, juan, carlos, valeria, daniel], ct);

        // ── Proyectos: una etapa distinta por proyecto y los tres BillingType ─────────────────────────────────
        var portal    = s.Project(PortalProjectId,    techCorp.Id,   "Portal Corporativo",          BillingType.FixedPrice, 85_000m,      "USD", 400, -120,  90, WorkType.Software,  hoursUsed: 210);
        var mobile    = s.Project(MobileProjectId,    bancoGreen.Id, "App Móvil Fintech",           BillingType.Hourly,     120_000m,     "USD", 600, -200,  60, WorkType.Software,  ProjectStatus.Paused, 260);
        // Activo y atrasado: la fecha de fin ya pasó, quedan hitos abiertos y la factura del hito 2 está vencida.
        var data      = s.Project(DataProjectId,      logistica.Id,  "Plataforma de Datos",         BillingType.Milestone,  380_000_000m, "COP", 500, -150,  -5, WorkType.Software,  hoursUsed: 470);
        var ecommerce = s.Project(EcommerceProjectId, mediaSol.Id,   "Renovación E-commerce",       BillingType.FixedPrice, 45_000m,      "USD", 250, -240, -30, WorkType.Software,  ProjectStatus.Completed, 248);
        var audit     = s.Project(AuditProjectId,     innova.Id,     "Auditoría de Seguridad",      BillingType.Hourly,     96_000_000m,  "COP", 200, -100,  20, WorkType.Software,  ProjectStatus.Cancelled, 40);
        var campaign  = s.Project(CampaignProjectId,  retailMax.Id,  "Campaña Digital RetailMax",   BillingType.FixedPrice, 42_000_000m,  "COP", 300,  -45,  75, WorkType.Marketing, hoursUsed: 90);
        await context.AddMissingAsync([portal, mobile, data, ecommerce, audit, campaign], ct);

        await context.AddMissingAsync(
        [
            s.Assign(portal.Id, mateo, 110_000m), s.Assign(portal.Id, juan, 120_000m), s.Assign(portal.Id, laura), s.Assign(portal.Id, carlos),
            s.Assign(data.Id, laura, 115_000m), s.Assign(data.Id, juan), s.Assign(data.Id, carlos), s.Assign(data.Id, daniel, 60m),
            s.Assign(mobile.Id, mateo), s.Assign(mobile.Id, laura),
            s.Assign(ecommerce.Id, mateo), s.Assign(ecommerce.Id, valeria),
            s.Assign(campaign.Id, valeria, 95_000m), s.Assign(campaign.Id, laura),
            s.Assign(audit.Id, daniel), s.Assign(audit.Id, carlos),
        ], ct);

        // ── Propuestas en todos sus estados ─────────────────────────────────────────────────────────────────────
        await context.AddMissingAsync(
        [
            s.Proposal("portal-v2", techCorp.Id, "Propuesta Portal Corporativo v2", "USD", "accepted", -130, 30,
                ("Diseño UX/UI", 8_000m), ("Desarrollo frontend", 30_000m), ("Integración backend", 20_000m)),
            s.Proposal("saas-fintech", bancoGreen.Id, "Plataforma SaaS Fintech — fase 2", "USD", "viewed", -6, 30,
                ("Arquitectura cloud", 15_000m), ("API REST", 45_000m), ("App iOS/Android", 35_000m)),
            s.Proposal("logistica", logistica.Id, "Plataforma de datos logística", "COP", "accepted", -160, 30,
                ("Análisis de requerimientos", 40_000_000m), ("Desarrollo de la plataforma", 300_000_000m), ("Capacitación y soporte", 40_000_000m)),
            s.Proposal("innova", innova.Id, "Transformación digital InnovaGroup", "COP", "rejected", -50, 30,
                ("Diagnóstico tecnológico", 18_000_000m), ("Hoja de ruta digital", 26_000_000m)),
            s.Proposal("retail-dashboard", retailMax.Id, "Dashboard analítico RetailMax", "COP", "draft", 0, 30,
                ("Diseño del dashboard", 9_000_000m), ("Integración de datos de ventas", 22_000_000m)),
            s.Proposal("mediasol-brand", mediaSol.Id, "Rediseño de marca MediaSol", "USD", "sent", -2, 15,
                ("Investigación y estrategia de marca", 6_500m), ("Sistema de identidad visual", 9_800m)),
            s.Proposal("techcorp-support", techCorp.Id, "Soporte y mantenimiento anual", "USD", "expired", -45, 30,
                ("Bolsa de 300 horas de soporte", 27_000m)),
        ], ct);

        // ── Facturas: borrador, enviada, pagada, vencida y pago parcial, en COP y USD ─────────────────────────
        const string invoiceType = "factura";
        var fv1001 = s.Invoice("FV-1001", techCorp.Id,   portal.Id,    "USD", invoiceType, -100, -70, DemoInvoiceState.Paid,
            ("Diseño UX/UI — fase 1", 1m, 12_000m), ("Desarrollo frontend — sprints 1 a 3", 1m, 18_000m));
        var fv1002 = s.Invoice("FV-1002", bancoGreen.Id, mobile.Id,    "USD", invoiceType, -80, -50, DemoInvoiceState.Paid,
            ("Desarrollo app móvil", 80m, 150m), ("Revisiones UX", 20m, 100m));
        var fv1003 = s.Invoice("FV-1003", logistica.Id,  data.Id,      "COP", invoiceType, -45, -15, DemoInvoiceState.Overdue,
            ("Hito 2 — Plataforma ETL fase 1", 1m, 95_000_000m));
        var fv1004 = s.Invoice("FV-1004", mediaSol.Id,   ecommerce.Id, "USD", invoiceType, -40, -10, DemoInvoiceState.Partial,
            ("E-commerce — entrega final", 1m, 22_500m));
        var fv1005 = s.Invoice("FV-1005", techCorp.Id,   portal.Id,    "USD", invoiceType, -5, 25, DemoInvoiceState.Sent,
            ("Desarrollo backend — sprints 4 y 5", 1m, 18_000m));
        var fv1006 = s.Invoice("FV-1006", retailMax.Id,  campaign.Id,  "COP", invoiceType, 0, 30, DemoInvoiceState.Draft,
            ("Campaña digital — anticipo 30 %", 1m, 12_600_000m));
        var fv1007 = s.Invoice("FV-1007", mediaSol.Id,   ecommerce.Id, "USD", invoiceType, -75, -45, DemoInvoiceState.Paid,
            ("E-commerce — fase 1", 1m, 22_500m));
        var fv1008 = s.Invoice("FV-1008", innova.Id,     audit.Id,     "COP", invoiceType, -60, -30, DemoInvoiceState.Paid,
            ("Auditoría de seguridad — horas ejecutadas", 40m, 240_000m));
        await context.AddMissingInvoicesAsync(OrganizationId, [fv1001, fv1002, fv1003, fv1004, fv1005, fv1006, fv1007, fv1008], ct);

        await context.AddMissingForInvoicesAsync(
            [s.Payment(fv1001, -72), s.Payment(fv1002, -52), s.Payment(fv1004, -12), s.Payment(fv1007, -46), s.Payment(fv1008, -31)],
            p => p.InvoiceId, ct);

        // ── Horas: pendientes, aprobadas y facturadas ─────────────────────────────────────────────────────────
        await context.AddMissingAsync(
        [
            s.Time("portal-mateo-1",   portal.Id,    mateo,   -1, 8m, 85_000m, "Navbar responsive y menú de usuario"),
            s.Time("portal-laura-1",   portal.Id,    laura,   -2, 6m, 90_000m, "Modelo de datos de transacciones"),
            s.Time("portal-juan-1",    portal.Id,    juan,    -4, 7m, 95_000m, "Endpoint de conciliación"),
            s.Time("data-juan-1",      data.Id,      juan,    -1, 7m, 95_000m, "Pipelines ETL de flota"),
            s.Time("data-daniel-1",    data.Id,      daniel,  -3, 6m, 45m,     "Ajuste de costos del clúster", currency: "USD"),
            s.Time("campaign-valeria-1", campaign.Id, valeria, -1, 5m, 75_000m, "Piezas para redes — semana 2"),

            s.Time("portal-juan-2",    portal.Id,    juan,    -8,  8m, 95_000m, "Autenticación con doble factor", "approved"),
            s.Time("portal-mateo-2",   portal.Id,    mateo,   -11, 8m, 85_000m, "Pantallas de dashboard", "approved"),
            s.Time("data-laura-1",     data.Id,      laura,   -9,  6m, 90_000m, "Validación de KPIs con el cliente", "approved"),
            s.Time("data-carlos-1",    data.Id,      carlos,  -12, 7m, 55_000m, "Infraestructura Kubernetes", "approved"),
            s.Time("campaign-valeria-2", campaign.Id, valeria, -10, 4m, 75_000m, "Moodboard de campaña", "approved"),

            s.Time("mobile-mateo-1",   mobile.Id,    mateo,   -45, 8m, 85_000m, "Pantallas de login", "billed"),
            s.Time("mobile-laura-1",   mobile.Id,    laura,   -46, 7m, 90_000m, "Módulo de autenticación", "billed"),
            s.Time("ecommerce-mateo-1", ecommerce.Id, mateo,  -38, 8m, 85_000m, "Checkout optimizado", "billed"),
        ], ct);

        // ── Hitos ───────────────────────────────────────────────────────────────────────────────────────────────
        var msUx        = s.Milestone("portal-ux",        portal.Id, "Diseño UI/UX aprobado",         17_000m, -90, "DONE");
        var msBackend   = s.Milestone("portal-backend",   portal.Id, "Backend auth y API core",       25_500m, -40, "DONE");
        var msTx        = s.Milestone("portal-tx",        portal.Id, "Módulo de transacciones",       21_250m,  15, "IN_PROGRESS");
        var msPayments  = s.Milestone("portal-payments",  portal.Id, "Integración pasarela de pago",  12_750m,  55, "PENDING");
        var msQa        = s.Milestone("portal-qa",        portal.Id, "QA y despliegue a producción",   8_500m,  85, "PENDING");
        var msDataArch  = s.Milestone("data-arch",        data.Id,   "Análisis y arquitectura",       76_000_000m,  -120, "DONE");
        var msDataEtl   = s.Milestone("data-etl",         data.Id,   "Plataforma ETL fase 1",         95_000_000m,   -60, "DONE");
        var msDataRep   = s.Milestone("data-reports",     data.Id,   "Módulo de reportes",           114_000_000m,   -20, "IN_PROGRESS");
        var msDataLive  = s.Milestone("data-golive",      data.Id,   "Puesta en producción",          95_000_000m,    -5, "PENDING");
        var msCampBrief = s.Milestone("campaign-brief",   campaign.Id, "Briefing y estrategia",        8_400_000m,  -35, "DONE");
        var msCampProd  = s.Milestone("campaign-content", campaign.Id, "Producción de contenidos",    16_800_000m,   10, "IN_PROGRESS");
        var msCampLaunch = s.Milestone("campaign-launch", campaign.Id, "Lanzamiento",                 10_500_000m,   40, "PENDING");
        var msCampReport = s.Milestone("campaign-report", campaign.Id, "Optimización y reporte",       6_300_000m,   75, "PENDING");
        await context.AddMissingAsync(
        [
            msUx, msBackend, msTx, msPayments, msQa, msDataArch, msDataEtl, msDataRep, msDataLive,
            msCampBrief, msCampProd, msCampLaunch, msCampReport,
            s.Milestone("mobile-design", mobile.Id, "Diseño y prototipado",     18_000m, -170, "DONE"),
            s.Milestone("mobile-auth",   mobile.Id, "Módulo de autenticación",  25_000m, -110, "DONE"),
            s.Milestone("mobile-core",   mobile.Id, "Core financiero",          40_000m,  -20, "IN_PROGRESS"),
            s.Milestone("mobile-launch", mobile.Id, "QA y lanzamiento",         37_000m,   50, "PENDING"),
            s.Milestone("ecommerce-arch",     ecommerce.Id, "Auditoría y arquitectura", 8_000m,  -200, "DONE"),
            s.Milestone("ecommerce-frontend", ecommerce.Id, "Rediseño frontend",        20_000m, -110, "DONE"),
            s.Milestone("ecommerce-launch",   ecommerce.Id, "Pagos, QA y salida",       17_000m,  -35, "DONE"),
        ], ct);

        // ── Tareas (con Gantt en el proyecto insignia) ───────────────────────────────────────────────────────────
        var tSetup      = s.Task("portal-setup",      portal.Id, "Setup del proyecto Next.js + TypeScript", "done", "medium", mateo, -88, msUx, -100, 8);
        var tWireframes = s.Task("portal-wireframes", portal.Id, "Wireframes del flujo de pagos",          "done", "high",   laura, -86, msUx, -98, 12, tSetup);
        var tContracts  = s.Task("portal-contracts",  portal.Id, "Definición de contratos API",            "done", "medium", juan,  -84, msUx, -96, 8);
        var tLogin      = s.Task("portal-login",      portal.Id, "Login pantalla principal v2",            "review", "high", laura,  -3, msBackend, -20, 20, tWireframes);
        var tJwt        = s.Task("portal-jwt",        portal.Id, "Pruebas de seguridad JWT",               "review", "high", juan,   -2, msBackend, -15, 12, tLogin);
        var tDocs       = s.Task("portal-docs",       portal.Id, "Documentación de endpoints REST",        "review", "medium", mateo, -1, msBackend, -14, 8, tContracts);
        var tBiometric  = s.Task("portal-biometric",  portal.Id, "Autenticación con biometría",            "in_progress", "high", mateo, 4, msTx, -10, 24, tLogin);
        var tOpenFin    = s.Task("portal-openfinance", portal.Id, "Integración API Open Finance",          "in_progress", "high", juan,  6, msTx, -6, 20, tJwt);
        var tDashboard  = s.Task("portal-dashboard",  portal.Id, "Pantallas de dashboard del usuario",     "in_progress", "medium", laura, 8, msTx, -4, 16, tLogin);
        var tCiCd       = s.Task("portal-cicd",       portal.Id, "Pipeline de CI/CD",                      "in_progress", "low", carlos, 10, msQa, -2, 12, tDocs);
        await context.AddMissingAsync(
        [
            tSetup, tWireframes, tContracts, tLogin, tJwt, tDocs, tBiometric, tOpenFin, tDashboard, tCiCd,
            s.Task("portal-kyc",    portal.Id, "Onboarding KYC paso 3",            "todo", "high",   laura, 12, msTx, 5, 16, tBiometric),
            s.Task("portal-push",   portal.Id, "Notificaciones push",              "todo", "medium", mateo, 18, msTx, 9, 8, tDashboard),
            s.Task("portal-schema", portal.Id, "Esquema de base de datos v2",      "todo", "low",    juan,  22, msPayments, 12, 12, tOpenFin),

            // Atrasadas: el proyecto está en riesgo.
            s.Task("data-reports",   data.Id, "Módulo de reportes de flota",       "in_progress", "high",   laura, -12, msDataRep),
            s.Task("data-migration", data.Id, "Migración del esquema v3 a v4",     "in_progress", "high",   juan,   -3, msDataRep),
            s.Task("data-kpis",      data.Id, "Revisión de KPIs con el cliente",   "review",      "medium", laura,  -1, msDataRep),
            s.Task("data-load",      data.Id, "Pruebas de carga",                  "todo",        "high",   daniel,  7, msDataLive),
            s.Task("data-cert",      data.Id, "Ambiente de certificación",         "done",        "medium", carlos, -100, msDataArch),

            s.Task("mobile-transfers", mobile.Id, "Transferencias con QR",           "in_progress", "high",   mateo, -25),
            s.Task("mobile-perf",      mobile.Id, "Pruebas de rendimiento Android",  "todo",        "medium", laura,  55),
            s.Task("mobile-setup",     mobile.Id, "Setup React Native + TypeScript", "done",        "medium", mateo, -180),

            s.Task("ecommerce-checkout", ecommerce.Id, "Rediseño del checkout",          "done", "high", valeria, -60),
            s.Task("ecommerce-payments", ecommerce.Id, "Integración de pasarela de pago", "done", "high", mateo,   -40),

            s.Task("campaign-ads",     campaign.Id, "Programar pauta en Instagram y TikTok", "todo",        "high",   valeria, 30),
            s.Task("campaign-video",   campaign.Id, "Editar video promocional de 30 s",      "in_progress", "high",   valeria,  8),
            s.Task("campaign-copy",    campaign.Id, "Copys del lanzamiento",                 "review",      "medium", laura,    3),
            s.Task("campaign-moodboard", campaign.Id, "Moodboard y paleta de campaña",       "done",        "medium", valeria, -30),

            s.Task("audit-owasp",  audit.Id, "Análisis OWASP Top 10",          "done", "high",   daniel, -60),
            s.Task("audit-pentest", audit.Id, "Pruebas de penetración de la API", "todo", "high", daniel,  15),
        ], ct);

        // ── Entregables ─────────────────────────────────────────────────────────────────────────────────────────
        await context.AddMissingAsync(
        [
            s.Deliverable("portal-architecture", portal.Id, "Documento de arquitectura técnica",    "Documento", -95, msUx,      "APPROVED", techCorp.Name),
            s.Deliverable("portal-figma",        portal.Id, "Diseños UI aprobados (Figma)",         "Diseño",    -90, msUx,      "APPROVED", techCorp.Name),
            s.Deliverable("portal-swagger",      portal.Id, "API REST documentada (Swagger)",       "Documento", -42, msBackend, "APPROVED", techCorp.Name),
            s.Deliverable("portal-tx-code",      portal.Id, "Módulo de transacciones (código)",     "Código",     15, msTx,      "IN_REVIEW"),
            s.Deliverable("portal-manual",       portal.Id, "Manual de usuario v1",                 "Documento",  60, msPayments, "PENDING"),
            s.Deliverable("portal-qa-report",    portal.Id, "Informe de QA final",                  "Documento",  80, msQa,      "PENDING"),
            s.Deliverable("data-architecture",   data.Id,   "Informe de arquitectura de datos",     "Documento", -118, msDataArch, "APPROVED", logistica.Name),
            s.Deliverable("data-dashboard",      data.Id,   "Tablero de reportes v1",               "Prototipo",  -18, msDataRep, "REJECTED"),
            s.Deliverable("campaign-brief",      campaign.Id, "Brief creativo",                     "Documento",  -33, msCampBrief, "APPROVED", retailMax.Name),
            s.Deliverable("campaign-grid",       campaign.Id, "Parrilla de contenidos del mes",     "Diseño",       5, msCampProd, "IN_REVIEW"),
            s.Deliverable("campaign-video",      campaign.Id, "Video promocional de 30 s",          "Video",       12, msCampProd, "PENDING"),
            s.Deliverable("campaign-report",     campaign.Id, "Reporte de resultados",              "Documento",   75, msCampReport, "PENDING"),
        ], ct);

        // ── Cobros del proyecto ─────────────────────────────────────────────────────────────────────────────────
        await context.AddMissingAsync(
        [
            s.Billing("mobile-1", mobile.Id, "Horas del mes", 150m, -150, 80, 12_000m, "PAID",     "FV-0981"),
            s.Billing("mobile-2", mobile.Id, "Horas del mes", 150m, -120, 90, 13_500m, "PAID",     "FV-0990"),
            s.Billing("mobile-3", mobile.Id, "Horas del mes", 150m,  -80, 93, 14_000m, "PAID",     fv1002.InvoiceNumber),
            s.Billing("mobile-4", mobile.Id, "Horas del mes", 150m,  -30, null, null,  "PENDING"),
            s.Billing("data-1", data.Id, "Cobro hito 1 — Análisis y arquitectura", 76_000_000m, -110, 1, 76_000_000m, "PAID", "FV-0975"),
            s.Billing("data-2", data.Id, "Cobro hito 2 — Plataforma ETL fase 1",   95_000_000m,  -45, 1, 95_000_000m, "INVOICED", fv1003.InvoiceNumber),
            s.Billing("data-3", data.Id, "Cobro hito 3 — Módulo de reportes",     114_000_000m,  -20, null, null, "PENDING"),
        ], ct);

        // ── Archivos y actividad ────────────────────────────────────────────────────────────────────────────────
        await context.AddMissingAsync(
        [
            s.File("contrato-techcorp-portal", portal.Id, "Contrato_TechCorp_Portal.pdf", "pdf", "245 KB", "MV", -118),
            s.File("propuesta-tecnica-v2",     portal.Id, "Propuesta_Tecnica_v2.docx",    "doc", "128 KB", "LP", -125),
            s.File("wireframes-ui-portal",     portal.Id, "Wireframes_UI_Portal.fig",     "fig", "8.4 MB", "LP", -95),
            s.File("api-spec-v1",              portal.Id, "API_Spec_v1.0.yaml",           "yml", "67 KB",  "JS", -60),
            s.File("acta-kick-off",            portal.Id, "Acta_Reunion_Kick_Off.pdf",    "pdf", "92 KB",  "MV", -117),
            s.File("brief-retailmax",          campaign.Id, "Brief_RetailMax.pdf",        "pdf", "150 KB", "VG", -33),
            s.File("moodboard-retailmax",      campaign.Id, "Moodboard_Campana.fig",      "fig", "6.1 MB", "VG", -28),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Activity("portal-1", portal.Id, "MV", "Marcó el hito 'Backend auth y API core' como completado", "Hitos",       -40, 14),
            s.Activity("portal-2", portal.Id, "LP", "Subió el archivo 'Wireframes_UI_Portal.fig'",              "Archivos",    -95, 10),
            s.Activity("portal-3", portal.Id, "JS", "Actualizó el progreso de 'Integración API Open Finance'", "Tareas",       -1, 16),
            s.Activity("portal-4", portal.Id, "CP", "Envió la factura FV-1005 a TechCorp Solutions",           "Facturación",  -5, 15),
            s.Activity("portal-5", portal.Id, "LP", "Añadió el entregable 'Manual de usuario v1'",              "Entregables", -20, 11),
            s.Activity("data-1",   data.Id,   "LP", "El cliente rechazó el entregable 'Tablero de reportes v1'", "Entregables", -18, 9),
            s.Activity("data-2",   data.Id,   "AR", "Cambió la fecha de 'Puesta en producción'",                 "Hitos",        -6, 17),
        ], ct);

        // ── Flujo de caja: gastos de seis meses e ingresos de las facturas cobradas ──────────────────────────────
        await context.AddMissingAsync(
        [
            .. s.MonthlyExpense("payroll",    "Nómina del equipo",               38_000_000m, "COP", 1, 6),
            .. s.MonthlyExpense("licenses",   "Licencias de software",            1_840m,     "USD", 4, 6),
            .. s.MonthlyExpense("rent",       "Arriendo oficina Bogotá",          8_500_000m, "COP", 5, 6, -4),
            .. s.MonthlyExpense("accounting", "Servicios contables y jurídicos",  3_200_000m, "COP", 7, 6, -8),
            .. s.MonthlyExpense("social",     "Seguridad social y parafiscales",  6_800_000m, "COP", 6, 6),
        ], ct);
        await context.AddMissingForInvoicesAsync(
        [
            s.Income(fv1001, techCorp.Name, -72), s.Income(fv1002, bancoGreen.Name, -52), s.Income(fv1004, mediaSol.Name, -12),
            s.Income(fv1007, mediaSol.Name, -46), s.Income(fv1008, innova.Name, -31),
        ], c => c.InvoiceId, ct);

        await context.EnsureIntegrationAsync(OrganizationId, "slack", principal.Id, "#techfactory-co", ct);

        // Parte 4 (RF-68): invitación pendiente a la Agencia para una persona sin organización.
        await PendingInvitationScenario.SeedAsync(context, ct);
    }
}
