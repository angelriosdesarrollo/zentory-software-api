using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// StartupX SAS, la Consultora (§3.0.A, decisión 6): empresa · free <b>tras bajar de pro</b>. Su owner, la persona Free,
/// tuvo una suscripción pro que hoy está cancelada (<see cref="DemoSeedContext.DowngradedBySeedReason"/>) y no tiene
/// otra activa. Nada se borró al bajar (RF-75 b), así que la organización quedó por encima de los límites de Free:
/// 3 clientes (límite 2), 4 facturas en el mes en curso (límite 3), 2 miembros (límite 1), una colaboradora (límite 0)
/// y una factura en USD, dato de la multimoneda de pro. La persona principal es member aquí.
/// </summary>
public sealed class ConsultancyScenario : IDemoScenario
{
    public const string OrganizationName = "StartupX SAS";

    // ID conservado (empresa antes y después); el work_type pasa de software a consultoría.
    public static readonly Guid OrganizationId = new("a1000000-0000-0000-0000-000000000003");

    /// <summary>
    /// Persona Free (§3.0.A): owner de la Consultora, su única organización propia (el máximo de Free), y member en la
    /// Agencia. Conserva el ID del antiguo owner de StartupX; su email pasa al dominio demo.
    /// </summary>
    public static readonly DemoPerson Owner = new(
        new Guid("b2000000-0000-0000-0000-000000000003"), DemoPeople.Email("daniel.ospina"), "Daniel", "Ospina");

    public string Name => OrganizationName;

    public async Task SeedAsync(DemoSeedContext context, CancellationToken ct = default)
    {
        var owner     = Owner;
        var principal = DemoPeople.Principal;
        await context.EnsureUserAsync(owner, OrgRole.Owner, ct);
        await context.EnsureUserAsync(principal, OrgRole.Owner, ct);

        await context.EnsureOrganizationAsync(OrganizationId, OrganizationName, LegalType.Empresa, owner.Id, ct);
        await context.EnsureMembershipAsync(OrganizationId, owner.Id, OrgRole.Owner, -400, ct);
        await context.EnsureMembershipAsync(OrganizationId, principal.Id, OrgRole.Member, -200, ct);

        // Ser invitado no tiene límite de plan: la persona Free también trabaja como member en la Agencia.
        if (await context.Db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.OrganizationId == AgencyScenario.OrganizationId, ct))
            await context.EnsureMembershipAsync(AgencyScenario.OrganizationId, owner.Id, OrgRole.Member, -90, ct);

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
            ["proposal.revision_count"] = "2",
            ["invoice.currency"]        = "COP",
            ["profile.work_type"]       = WorkType.Consultoria,
        }, ct);

        // Bajó hoy: las facturas del mes se emitieron todavía en pro y siguen ahí (RF-75 d).
        await context.EnsureDowngradedSubscriptionAsync(owner, OrganizationId, OrganizationName, Plan.Pro, 0, ct);

        var s = new DemoOrganizationSeed(context, OrganizationId);

        // ── Clientes: tres, uno por encima del límite de Free ────────────────────────────────────────────────
        var cafe     = s.Client("cafe-origen", "Café Origen del Huila SAS", "Marta Cifuentes", "marta.cifuentes", "+57 315 210 3040", "Neiva",  "901.234.567-1");
        var textiles = s.Client("textiles",    "Textiles Andinos Ltda.",    "Hernán Duarte",   "hernan.duarte",   "+57 315 320 4050", "Pereira", "800.456.789-2");
        var north    = s.Client("northbridge", "NorthBridge Partners LLC",  "Emily Carter",    "emily.carter",    "+1 305 555 0142",  "Miami",   "98-7654321");
        await context.AddMissingAsync([cafe, textiles, north], ct);

        // Contratada en la época pro; Free no admite colaboradores, pero lo existente se conserva.
        var paola = s.Collaborator("paola", "Paola Restrepo", "hourly_contractor", "Analista financiera", hourlyRate: 70_000m,
            email: DemoPeople.Email("paola.restrepo"));
        await context.AddMissingAsync([paola], ct);

        // ── Proyectos ───────────────────────────────────────────────────────────────────────────────────────────
        var processes = s.Project(s.Id("project:processes"), cafe.Id,     "Rediseño de procesos comerciales",       BillingType.Hourly,     48_000_000m, "COP", 320,  -75,  45, WorkType.Consultoria, hoursUsed: 140);
        var market    = s.Project(s.Id("project:market"),    north.Id,    "Estudio de entrada al mercado andino",   BillingType.Milestone,  18_000m,     "USD", 160,  -50,  40, WorkType.Consultoria, hoursUsed: 70);
        var costs     = s.Project(s.Id("project:costs"),     textiles.Id, "Reestructuración de costos operativos",  BillingType.FixedPrice, 32_000_000m, "COP", 200, -160, -20, WorkType.Consultoria, ProjectStatus.Completed, 196);
        await context.AddMissingAsync([processes, market, costs], ct);
        await context.AddMissingAsync([s.Assign(processes.Id, paola, 110_000m), s.Assign(costs.Id, paola)], ct);

        await context.AddMissingAsync(
        [
            s.Proposal("strategy-2027", textiles.Id, "Plan estratégico 2027", "COP", "sent", -5, 30,
                ("Diagnóstico y talleres con la gerencia", 9_500_000m), ("Plan estratégico y tablero de seguimiento", 14_000_000m)),
            s.Proposal("market-entry", north.Id, "Market entry study — Andean region", "USD", "accepted", -55, 30,
                ("Sector analysis", 6_000m), ("Distributor interviews", 7_000m), ("Final report and roadmap", 5_000m)),
            s.Proposal("sales-dashboard", cafe.Id, "Tablero de indicadores comerciales", "COP", "rejected", -40, 30,
                ("Definición de indicadores", 4_000_000m), ("Implementación del tablero", 7_500_000m)),
        ], ct);

        // ── Facturas: las cuatro en el mes en curso del día de referencia (CA-70), una de ellas en USD ──────────
        // Repartidas entre el día 1 del mes y hoy; con la referencia en el día 1, las cuatro caen ese día.
        var elapsed = context.Today.Day - 1;
        int InMonth(int sixths) => -elapsed + (elapsed * sixths / 6);

        const string invoiceType = "factura";
        var fc3001 = s.Invoice("FC-3001", textiles.Id, costs.Id,     "COP", invoiceType, InMonth(0), InMonth(0) + 30, DemoInvoiceState.Paid,
            ("Reestructuración de costos — entrega final", 1m, 16_000_000m));
        var fc3002 = s.Invoice("FC-3002", north.Id,    market.Id,    "USD", invoiceType, InMonth(2), InMonth(2) + 30, DemoInvoiceState.Sent,
            ("Milestone 1 — Sector analysis", 1m, 6_000m));
        var fc3003 = s.Invoice("FC-3003", cafe.Id,     processes.Id, "COP", invoiceType, InMonth(4), InMonth(4) + 30, DemoInvoiceState.Sent,
            ("Consultoría de procesos — horas del periodo", 60m, 150_000m));
        var fc3004 = s.Invoice("FC-3004", cafe.Id,     processes.Id, "COP", invoiceType, InMonth(6), InMonth(6) + 30, DemoInvoiceState.Draft,
            ("Consultoría de procesos — horas en curso", 32m, 150_000m));
        Invoice[] invoices = [fc3001, fc3002, fc3003, fc3004];
        // El cupo mensual se cuenta por CreatedAt: se lleva al día de emisión para que el límite vea las cuatro.
        foreach (var invoice in invoices)
            context.Set(invoice, i => i.CreatedAt, context.UtcDay(invoice.IssuedAt.DayNumber - context.Today.DayNumber, 8));
        await context.AddMissingInvoicesAsync(OrganizationId, invoices, ct);
        await context.AddMissingForInvoicesAsync([s.Payment(fc3001, InMonth(1))], p => p.InvoiceId, ct);

        // ── Horas ───────────────────────────────────────────────────────────────────────────────────────────────
        await context.AddMissingAsync(
        [
            s.Time("processes-owner-1", processes.Id, null,  -1, 4m, 150_000m, "Taller de mapeo del proceso de ventas", createdBy: owner.Id),
            s.Time("processes-paola-1", processes.Id, paola, -2, 6m,  70_000m, "Análisis del embudo comercial"),
            s.Time("market-owner-1",    market.Id,    null,  -3, 5m,      90m, "Entrevistas con distribuidores en Lima", currency: "USD", createdBy: owner.Id),
            s.Time("processes-paola-2", processes.Id, paola, -9, 6m,  70_000m, "Indicadores de conversión por canal", "approved"),
            s.Time("costs-paola-1",     costs.Id,     paola, -25, 8m, 70_000m, "Modelo de costos por línea de producto", "billed"),
        ], ct);

        // ── Hitos, tareas y entregables ─────────────────────────────────────────────────────────────────────────
        var msSector    = s.Milestone("market-sector",     market.Id, "Análisis sectorial",              6_000m, -20, "DONE");
        var msInterview = s.Milestone("market-interviews", market.Id, "Entrevistas con distribuidores",  7_000m,  10, "IN_PROGRESS");
        var msReport    = s.Milestone("market-report",     market.Id, "Informe final y hoja de ruta",    5_000m,  40, "PENDING");
        await context.AddMissingAsync([msSector, msInterview, msReport], ct);

        await context.AddMissingAsync(
        [
            s.Task("market-interviews", market.Id,    "Agendar entrevistas con 8 distribuidores", "in_progress", "high",   null,   5, msInterview),
            s.Task("market-synthesis",  market.Id,    "Síntesis de hallazgos por país",           "todo",        "medium", null,  25, msReport),
            s.Task("market-sector",     market.Id,    "Informe de análisis sectorial",            "done",        "high",   null, -20, msSector),
            s.Task("processes-funnel",  processes.Id, "Rediseñar el embudo comercial",            "in_progress", "high",   paola,  7),
            s.Task("processes-review",  processes.Id, "Validar el proceso con la gerencia",       "review",      "medium", null,   2),
            s.Task("costs-closing",     costs.Id,     "Acta de cierre con el cliente",            "done",        "medium", null, -20),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Deliverable("market-sector",  market.Id,    "Informe de análisis sectorial",          "Documento", -20, msSector,    "APPROVED", north.Name),
            s.Deliverable("market-report",  market.Id,    "Informe final y hoja de ruta",           "Documento",  40, msReport,    "PENDING"),
            s.Deliverable("processes-map",  processes.Id, "Mapa del proceso comercial",             "Documento",   3, null,        "IN_REVIEW"),
            s.Deliverable("costs-model",    costs.Id,     "Modelo de costos y plan de ahorro",      "Documento", -22, null,        "APPROVED", textiles.Name),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Billing("processes-1", processes.Id, "Horas del periodo", 150_000m, InMonth(4), 60, 9_000_000m, "INVOICED", fc3003.InvoiceNumber),
            s.Billing("processes-2", processes.Id, "Horas en curso",    150_000m, 20, null, null, "PENDING"),
            s.Billing("market-1",    market.Id,    "Hito 1 — Análisis sectorial", 6_000m, InMonth(2), 1, 6_000m, "INVOICED", fc3002.InvoiceNumber),
        ], ct);

        await context.AddMissingAsync(
        [
            s.File("propuesta-market-entry", market.Id,    "Market_Entry_Proposal.pdf",  "pdf", "540 KB", "DO", -55),
            s.File("mapa-proceso-ventas",    processes.Id, "Mapa_Proceso_Ventas.pdf",    "pdf", "1.1 MB", "PR", -3),
            s.File("modelo-costos",          costs.Id,     "Modelo_Costos_Textiles.xlsx", "xls", "320 KB", "PR", -22),
        ], ct);
        await context.AddMissingAsync(
        [
            s.Activity("market-1",    market.Id,    "DO", "Marcó el hito 'Análisis sectorial' como completado", "Hitos",       -20, 17),
            s.Activity("processes-1", processes.Id, "PR", "Subió 'Mapa_Proceso_Ventas.pdf'",                    "Archivos",     -3, 11),
            s.Activity("costs-1",     costs.Id,     "DO", "Cerró el proyecto con el cliente",                   "Proyecto",    -20, 15),
        ], ct);

        // ── Flujo de caja: gastos fijos, ingresos de antes de las facturas del mes y el pago de FC-3001 ─────────
        await context.AddMissingAsync(
        [
            .. s.MonthlyExpense("analyst",  "Honorarios analista financiera", 2_400_000m, "COP", 2, 6),
            .. s.MonthlyExpense("software", "Herramientas de productividad",    180_000m, "COP", 4, 6, -6),
            .. s.MonthlyExpense("cowork",   "Coworking en Bogotá",              900_000m, "COP", 5, 6, -4),
            context.WithId(CashFlowEntry.CreateManual(OrganizationId, "income", $"Anticipo reestructuración de costos — {textiles.Name}",
                16_000_000m, "COP", 1m, 16_000_000m, context.Day(-150), categoryId: 11), s.Id("cash:income:costs-advance")),
            context.WithId(CashFlowEntry.CreateManual(OrganizationId, "income", $"Anticipo estudio de mercado — {north.Name}",
                3_000m, "USD", 1m, 3_000m, context.Day(-48), categoryId: 11), s.Id("cash:income:market-advance")),
        ], ct);
        await context.AddMissingForInvoicesAsync([s.Income(fc3001, textiles.Name, InMonth(1))], c => c.InvoiceId, ct);
    }
}
