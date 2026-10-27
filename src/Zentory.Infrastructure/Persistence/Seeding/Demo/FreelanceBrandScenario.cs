using Zentory.Domain.Constants;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// Marca freelance de la persona principal (§3.0.A): freelance · studio, heredado de su owner, sin suscripción propia.
/// Trabajo creativo (WorkType Marketing): identidad visual, contenidos y una campaña para un cliente que paga en USD.
/// Sin otros miembros; las horas las registra la owner.
/// </summary>
public sealed class FreelanceBrandScenario : IDemoScenario
{
    public const string OrganizationName = "Camila Perez Freelance";

    public static readonly Guid OrganizationId = new("a1000000-0000-0000-0000-000000000002");

    public string Name => OrganizationName;

    public async Task SeedAsync(DemoSeedContext context, CancellationToken ct = default)
    {
        var owner = DemoPeople.Principal;
        await context.EnsureUserAsync(owner, OrgRole.Owner, ct);

        await context.EnsureOrganizationAsync(OrganizationId, OrganizationName, LegalType.Freelance, owner.Id, ct);
        await context.EnsureMembershipAsync(OrganizationId, owner.Id, OrgRole.Owner, -380, ct);

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
            ["invoice.payment_method"]  = "Nequi o transferencia bancaria",
            ["proposal.validity_days"]  = "15",
            ["proposal.revision_count"] = "2",
            ["invoice.currency"]        = "COP",
            ["profile.work_type"]       = WorkType.Marketing,
        }, ct);

        var s = new DemoOrganizationSeed(context, OrganizationId);

        var espiga  = s.Client("espiga",  "Panadería La Espiga",     "Rosa Quintero",  "rosa.quintero",  "+57 311 101 2020", "Envigado",   "1.036.111.222-3");
        var prana   = s.Client("prana",   "Estudio Yoga Prana",      "Lucía Herrera",  "lucia.herrera",  "+57 311 303 4040", "Medellín",   "901.777.888-1");
        var marAzul = s.Client("marazul", "Hotel Boutique Mar Azul", "Thomas Becker",  "thomas.becker",  "+57 311 505 6060", "Cartagena",  "901.888.999-2");
        await context.AddMissingAsync([espiga, prana, marAzul], ct);

        // Un aliado externo para la fotografía; el resto lo hace la owner.
        var tomas = s.Collaborator("tomas", "Tomás Arango", "hourly_contractor", "Fotógrafo", hourlyRate: 120_000m,
            email: DemoPeople.Email("tomas.arango"));
        await context.AddMissingAsync([tomas], ct);

        var identity = s.Project(s.Id("project:identity"), espiga.Id,  "Identidad visual La Espiga",       BillingType.FixedPrice, 6_500_000m, "COP",  60, -120, -40, WorkType.Marketing, ProjectStatus.Completed, 58);
        var content  = s.Project(s.Id("project:content"),  prana.Id,   "Contenido mensual Yoga Prana",     BillingType.Hourly,     9_600_000m, "COP", 120,  -60, 120, WorkType.Marketing, hoursUsed: 44);
        var season   = s.Project(s.Id("project:season"),   marAzul.Id, "Campaña temporada alta Mar Azul",  BillingType.Milestone,  4_800m,     "USD",  80,  -20,  50, WorkType.Marketing, hoursUsed: 18);
        await context.AddMissingAsync([identity, content, season], ct);
        await context.AddMissingAsync([s.Assign(season.Id, tomas, 160_000m)], ct);

        await context.AddMissingAsync(
        [
            s.Proposal("season", marAzul.Id, "Campaña temporada alta 2026", "USD", "accepted", -28, 15,
                ("Concepto creativo", 1_200m), ("Sesión de fotos", 1_600m), ("Pauta y reporte", 2_000m)),
            s.Proposal("prana-web", prana.Id, "Rediseño del sitio web", "COP", "sent", -3, 15,
                ("Arquitectura de contenidos", 1_800_000m), ("Diseño y maquetación", 4_200_000m)),
            s.Proposal("espiga-packaging", espiga.Id, "Empaques de temporada", "COP", "draft", 0, 15,
                ("Diseño de tres empaques", 2_400_000m)),
        ], ct);

        // Cuentas de cobro: el documento de una persona natural.
        const string invoiceType = "cobro";
        var cc001 = s.Invoice("CC-001", espiga.Id,  identity.Id, "COP", invoiceType, -90, -75, DemoInvoiceState.Paid,
            ("Identidad visual — anticipo 50 %", 1m, 3_250_000m));
        var cc002 = s.Invoice("CC-002", espiga.Id,  identity.Id, "COP", invoiceType, -42, -27, DemoInvoiceState.Paid,
            ("Identidad visual — entrega final", 1m, 3_250_000m));
        var cc003 = s.Invoice("CC-003", prana.Id,   content.Id,  "COP", invoiceType, -33, -18, DemoInvoiceState.Overdue,
            ("Contenido del mes — horas", 22m, 80_000m));
        var cc004 = s.Invoice("CC-004", marAzul.Id, season.Id,   "USD", invoiceType, -15, 0, DemoInvoiceState.Partial,
            ("Hito 1 — Concepto creativo", 1m, 1_200m), ("Hito 2 — Sesión de fotos", 1m, 1_600m));
        var cc005 = s.Invoice("CC-005", prana.Id,   content.Id,  "COP", invoiceType, -2, 13, DemoInvoiceState.Sent,
            ("Contenido del mes — horas", 22m, 80_000m));
        await context.AddMissingInvoicesAsync(OrganizationId, [cc001, cc002, cc003, cc004, cc005], ct);
        await context.AddMissingForInvoicesAsync(
            [s.Payment(cc001, -80, "nequi"), s.Payment(cc002, -30), s.Payment(cc004, -6)], p => p.InvoiceId, ct);

        await context.AddMissingAsync(
        [
            s.Time("content-1", content.Id, null,  -1, 3m,   80_000m, "Calendario editorial de la semana", createdBy: owner.Id),
            s.Time("content-2", content.Id, null,  -3, 4m,   80_000m, "Carruseles para Instagram", createdBy: owner.Id),
            s.Time("season-1",  season.Id,  null,  -2, 5m,   80_000m, "Guion del video de temporada", createdBy: owner.Id),
            s.Time("season-2",  season.Id,  tomas, -5, 6m,  120_000m, "Sesión de fotos en el hotel", "approved"),
            s.Time("content-3", content.Id, null, -36, 4m,   80_000m, "Piezas del mes anterior", "billed", createdBy: owner.Id),
        ], ct);

        var msConcept = s.Milestone("season-concept", season.Id, "Concepto creativo", 1_200m, -10, "DONE");
        var msPhotos  = s.Milestone("season-photos",  season.Id, "Sesión de fotos",   1_600m,  -3, "DONE");
        var msAds     = s.Milestone("season-ads",     season.Id, "Pauta y reporte",   2_000m,  45, "IN_PROGRESS");
        await context.AddMissingAsync(
        [
            msConcept, msPhotos, msAds,
            s.Milestone("identity-logo",   identity.Id, "Logotipo y paleta", 3_250_000m, -70, "DONE"),
            s.Milestone("identity-manual", identity.Id, "Manual de marca",   3_250_000m, -42, "DONE"),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Task("season-reel",   season.Id,  "Editar reel de temporada",        "in_progress", "high",   null,   4, msAds),
            s.Task("season-ads",    season.Id,  "Configurar campaña en Meta Ads",  "todo",        "medium", null,  10, msAds),
            s.Task("season-photos", season.Id,  "Selección y retoque de fotos",    "done",        "high",   tomas, -3, msPhotos),
            s.Task("content-grid",  content.Id, "Parrilla de publicaciones",       "review",      "medium", null,   1),
            s.Task("content-stories", content.Id, "Historias de la semana",        "todo",        "low",    null,   5),
            s.Task("identity-files", identity.Id, "Entregar archivos fuente",      "done",        "medium", null, -40),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Deliverable("identity-manual", identity.Id, "Manual de marca La Espiga",    "Documento", -42, null,      "APPROVED", espiga.Name),
            s.Deliverable("season-moodboard", season.Id,  "Moodboard de campaña",         "Diseño",    -12, msConcept, "APPROVED", marAzul.Name),
            s.Deliverable("season-photos",   season.Id,   "Banco de fotos editado",       "Diseño",     -2, msPhotos,  "IN_REVIEW"),
            s.Deliverable("season-reel",     season.Id,   "Reel de temporada",            "Video",      12, msAds,     "PENDING"),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Billing("content-1", content.Id, "Horas del mes", 80_000m, -33, 22, 1_760_000m, "INVOICED", cc003.InvoiceNumber),
            s.Billing("content-2", content.Id, "Horas del mes", 80_000m,  -2, 22, 1_760_000m, "INVOICED", cc005.InvoiceNumber),
            s.Billing("content-3", content.Id, "Horas del mes", 80_000m,  28, null, null, "PENDING"),
        ], ct);

        await context.AddMissingAsync(
        [
            s.File("manual-la-espiga",  identity.Id, "Manual_Marca_La_Espiga.pdf", "pdf", "4.2 MB", "CP", -42),
            s.File("moodboard-mar-azul", season.Id,  "Moodboard_Mar_Azul.fig",     "fig", "5.3 MB", "CP", -12),
        ], ct);
        await context.AddMissingAsync(
        [
            s.Activity("season-1", season.Id, "CP", "Marcó el hito 'Sesión de fotos' como completado", "Hitos", -3, 18),
            s.Activity("season-2", season.Id, "TA", "Subió 42 fotos editadas",                       "Archivos", -2, 12),
        ], ct);

        await context.AddMissingAsync(
        [
            .. s.MonthlyExpense("adobe",     "Adobe Creative Cloud",       260_000m, "COP", 4, 6),
            .. s.MonthlyExpense("coworking", "Coworking",                  450_000m, "COP", 5, 6, -5),
            .. s.MonthlyExpense("social",    "Seguridad social (planilla)", 620_000m, "COP", 6, 6, -10),
        ], ct);
        await context.AddMissingForInvoicesAsync(
            [s.Income(cc001, espiga.Name, -80), s.Income(cc002, espiga.Name, -30), s.Income(cc004, marAzul.Name, -6)],
            c => c.InvoiceId, ct);
    }
}
