using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// Freelance «otro» (§3.0.A): freelance · pro, WorkType Otro. Una traductora e intérprete independiente con su propia
/// suscripción pro: proyectos cortos de precio fijo (una traducción, una jornada de interpretación, un subtitulado),
/// un cliente que paga en USD (multimoneda de pro) y su planilla PILA de los últimos meses, calculada y radicada. Trabaja
/// sola: sin colaboradores ni otros miembros.
/// </summary>
public sealed class FreelanceOtherScenario : IDemoScenario
{
    public const string OrganizationName = "Valentina Ruiz Traducciones";

    // ID nuevo: a1…0005 era Pulso Digital (empresa, retirada) y no se reutiliza con otro legalType.
    public static readonly Guid OrganizationId = new("a1000000-0000-0000-0000-000000000006");

    // Conserva el ID de la antigua owner de Pulso Digital; su email pasa al dominio demo.
    public static readonly DemoPerson Owner = new(
        new Guid("b2000000-0000-0000-0000-000000000004"), DemoPeople.Email("valentina.ruiz"), "Valentina", "Ruiz");

    public string Name => OrganizationName;

    public async Task SeedAsync(DemoSeedContext context, CancellationToken ct = default)
    {
        var owner = Owner;
        await context.EnsureUserAsync(owner, OrgRole.Owner, ct);

        await context.EnsureOrganizationAsync(OrganizationId, OrganizationName, LegalType.Freelance, owner.Id, ct);
        await context.EnsureMembershipAsync(OrganizationId, owner.Id, OrgRole.Owner, -330, ct);

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
            ["invoice.payment_method"]  = "Transferencia bancaria o PayPal",
            ["proposal.validity_days"]  = "10",
            ["proposal.revision_count"] = "1",
            ["invoice.currency"]        = "COP",
            ["profile.work_type"]       = WorkType.Otro,
        }, ct);

        await context.EnsureActiveSubscriptionAsync(owner, OrganizationId, OrganizationName, Plan.Pro, ct);

        var s = new DemoOrganizationSeed(context, OrganizationId);

        var editorial = s.Client("editorial", "Editorial Montaña Azul",  "Gloria Salazar", "gloria.salazar", "+57 312 404 1122", "Manizales", "900.765.432-1");
        var puentes   = s.Client("puentes",   "Fundación Puentes",       "Iván Cortés",    "ivan.cortes",    "+57 312 505 2233", "Bogotá",    "901.112.233-4");
        var lumen     = s.Client("lumen",     "Lumen Media Group",       "Olivia Brooks",  "olivia.brooks",  "+1 416 555 0187",  "Toronto",   "84-2233445");
        await context.AddMissingAsync([editorial, puentes, lumen], ct);

        // ── Proyectos cortos de precio fijo: semanas, no meses ─────────────────────────────────────────────────
        var guide     = s.Project(s.Id("project:guide"),     editorial.Id, "Traducción de la guía de lectura (EN→ES)",    BillingType.FixedPrice, 2_400_000m, "COP", 30, -40, -26, WorkType.Otro, ProjectStatus.Completed, 30);
        var forum     = s.Project(s.Id("project:forum"),     puentes.Id,   "Interpretación foro regional de juventud",    BillingType.FixedPrice, 3_200_000m, "COP", 16, -12, -10, WorkType.Otro, ProjectStatus.Completed, 16);
        var subtitles = s.Project(s.Id("project:subtitles"), lumen.Id,     "Subtitulado de serie documental (6 capítulos)", BillingType.FixedPrice, 2_700m,   "USD", 45,  -8,  14, WorkType.Otro, hoursUsed: 18);
        var contracts = s.Project(s.Id("project:contracts"), editorial.Id, "Traducción oficial de contratos de coedición", BillingType.FixedPrice, 1_800_000m, "COP", 20, -20,  -5, WorkType.Otro, ProjectStatus.Cancelled, 4);
        await context.AddMissingAsync([guide, forum, subtitles, contracts], ct);

        await context.AddMissingAsync(
        [
            s.Proposal("subtitles", lumen.Id, "Documentary series subtitling (EN/ES)", "USD", "accepted", -14, 10,
                ("Subtitles, 6 episodes", 2_400m), ("Quality review", 300m)),
            s.Proposal("congress", puentes.Id, "Interpretación simultánea — congreso anual", "COP", "sent", -2, 10,
                ("Dos jornadas de interpretación", 5_600_000m), ("Preparación de glosario", 600_000m)),
            s.Proposal("catalog", editorial.Id, "Traducción del catálogo de novedades", "COP", "expired", -35, 10,
                ("Traducción de 40 fichas", 1_600_000m)),
        ], ct);

        // Cuentas de cobro (persona natural).
        const string invoiceType = "cobro";
        var cc101 = s.Invoice("CC-101", editorial.Id, guide.Id,     "COP", invoiceType, -25, -15, DemoInvoiceState.Paid,
            ("Traducción de la guía de lectura", 1m, 2_400_000m));
        var cc102 = s.Invoice("CC-102", puentes.Id,   forum.Id,     "COP", invoiceType,  -9,   1, DemoInvoiceState.Paid,
            ("Interpretación — dos jornadas", 1m, 3_200_000m));
        var cc103 = s.Invoice("CC-103", lumen.Id,     subtitles.Id, "USD", invoiceType,  -6,   4, DemoInvoiceState.Partial,
            ("Subtitles — episodes 1 to 3", 1m, 1_350m));
        var cc104 = s.Invoice("CC-104", lumen.Id,     subtitles.Id, "USD", invoiceType,   0,  10, DemoInvoiceState.Draft,
            ("Subtitles — episodes 4 to 6", 1m, 1_350m));
        var cc105 = s.Invoice("CC-105", editorial.Id, contracts.Id, "COP", invoiceType, -45, -35, DemoInvoiceState.Overdue,
            ("Glosario jurídico de coedición", 1m, 450_000m));
        await context.AddMissingInvoicesAsync(OrganizationId, [cc101, cc102, cc103, cc104, cc105], ct);
        await context.AddMissingForInvoicesAsync(
            [s.Payment(cc101, -16), s.Payment(cc102, -3), s.Payment(cc103, -1, "paypal")], p => p.InvoiceId, ct);

        await context.AddMissingAsync(
        [
            s.Time("subtitles-1", subtitles.Id, null,  -1, 5m,  60m, "Subtitulado del capítulo 4", currency: "USD", createdBy: owner.Id),
            s.Time("subtitles-2", subtitles.Id, null,  -3, 6m,  60m, "Revisión de sincronía, capítulos 1 a 3", currency: "USD", createdBy: owner.Id),
            s.Time("forum-1",     forum.Id,     null, -11, 8m, 200_000m, "Interpretación — jornada 1", "billed", createdBy: owner.Id),
            s.Time("forum-2",     forum.Id,     null, -10, 8m, 200_000m, "Interpretación — jornada 2", "billed", createdBy: owner.Id),
            s.Time("guide-1",     guide.Id,     null, -30, 6m,  80_000m, "Traducción capítulos 3 y 4", "approved", createdBy: owner.Id),
        ], ct);

        var msFirst  = s.Milestone("subtitles-first",  subtitles.Id, "Capítulos 1 a 3", 1_350m, -2, "DONE");
        var msSecond = s.Milestone("subtitles-second", subtitles.Id, "Capítulos 4 a 6", 1_350m, 14, "IN_PROGRESS");
        await context.AddMissingAsync([msFirst, msSecond], ct);

        await context.AddMissingAsync(
        [
            s.Task("subtitles-ep4",      subtitles.Id, "Subtitular capítulo 4",               "in_progress", "high",   null,  3, msSecond),
            s.Task("subtitles-ep5",      subtitles.Id, "Subtitular capítulo 5",               "todo",        "high",   null,  8, msSecond),
            s.Task("subtitles-qa",       subtitles.Id, "Control de calidad de los capítulos 1 a 3", "done",  "medium", null, -2, msFirst),
            s.Task("forum-glossary",     forum.Id,     "Glosario del foro con la organización", "done",      "medium", null, -13),
            s.Task("guide-proofreading", guide.Id,     "Corrección de estilo final",          "done",        "medium", null, -27),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Deliverable("guide-final",    guide.Id,     "Guía de lectura traducida",           "Documento", -26, null,     "APPROVED", editorial.Name),
            s.Deliverable("subtitles-1-3",  subtitles.Id, "Subtítulos capítulos 1 a 3 (.srt)",   "Archivo",    -2, msFirst,  "IN_REVIEW"),
            s.Deliverable("subtitles-4-6",  subtitles.Id, "Subtítulos capítulos 4 a 6 (.srt)",   "Archivo",    14, msSecond, "PENDING"),
        ], ct);

        await context.AddMissingAsync(
        [
            s.Billing("subtitles-1", subtitles.Id, "Capítulos 1 a 3", 1_350m, -6, 1, 1_350m, "INVOICED", cc103.InvoiceNumber),
            s.Billing("subtitles-2", subtitles.Id, "Capítulos 4 a 6", 1_350m, 14, null, null, "PENDING"),
        ], ct);

        await context.AddMissingAsync(
        [
            s.File("guia-traducida",     guide.Id,     "Guia_Lectura_ES.docx",     "doc", "860 KB", "VR", -26),
            s.File("glosario-foro",      forum.Id,     "Glosario_Foro_Juventud.pdf", "pdf", "120 KB", "VR", -13),
            s.File("subtitulos-ep1-3",   subtitles.Id, "Subtitulos_Ep1-3.zip",     "zip", "96 KB",  "VR", -2),
        ], ct);
        await context.AddMissingAsync(
        [
            s.Activity("subtitles-1", subtitles.Id, "VR", "Entregó los subtítulos de los capítulos 1 a 3", "Entregables", -2, 18),
            s.Activity("forum-1",     forum.Id,     "VR", "Marcó el proyecto como completado",              "Proyecto",    -10, 19),
        ], ct);

        await context.AddMissingAsync(
        [
            .. s.MonthlyExpense("cat-tool", "Licencia de herramienta de traducción asistida", 140_000m, "COP", 4, 6),
            .. s.MonthlyExpense("internet", "Internet y telefonía",                            120_000m, "COP", 5, 6, -6),
            .. s.MonthlyExpense("pila",     "Seguridad social (planilla PILA)",                 760_000m, "COP", 6, 6, -10),
        ], ct);
        await context.AddMissingForInvoicesAsync(
            [s.Income(cc101, editorial.Name, -16), s.Income(cc102, puentes.Name, -3), s.Income(cc103, lumen.Name, -1)],
            c => c.InvoiceId, ct);

        await context.AddMissingAsync(await PilaAsync(context, s, owner), ct);
    }

    // PILA propia de los tres últimos periodos (cálculo del independiente, sin colaborador): IBC del 40 % del ingreso
    // con piso de un salario mínimo y las tasas vigentes del catálogo. Los periodos cerrados quedan radicados.
    private static async Task<IReadOnlyList<SsCalculationLog>> PilaAsync(DemoSeedContext context, DemoOrganizationSeed s, DemoPerson owner)
    {
        var rules = await context.Db.SsRules.AsNoTracking()
            .Where(r => r.CountryCode == "CO" && r.ContributorType == "independiente" && r.Active)
            .OrderByDescending(r => r.EffectiveYear)
            .ToListAsync();
        var health  = rules.First(r => r.FundType == "salud");
        var pension = rules.First(r => r.FundType == "pension");
        var risk    = rules.First(r => r.FundType == "arl" && r.ArlLevel == 1);

        decimal[] incomes = [5_600_000m, 6_400_000m, 7_100_000m];
        var logs = new List<SsCalculationLog>();
        for (var monthsBack = 0; monthsBack < incomes.Length; monthsBack++)
        {
            var income = incomes[monthsBack];
            var ibc    = Math.Max(Math.Round(income * 0.4m, 0), health.SmlvCop);
            var contributions = (Health: Math.Round(ibc * health.TotalPct, 0), Pension: Math.Round(ibc * pension.TotalPct, 0),
                Risk: Math.Round(ibc * risk.TotalPct, 0));
            var result = JsonSerializer.Serialize(new
            {
                ibc, salud = contributions.Health, pension = contributions.Pension, arl = contributions.Risk,
            });

            var periodDay = context.Day(-30 * monthsBack);
            var log = context.WithId(
                SsCalculationLog.Create(OrganizationId, owner.Id, "CO", periodDay.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    income, "COP", result, contributions.Health + contributions.Pension + contributions.Risk, health.SmlvCop),
                s.Id($"pila:{monthsBack}"));
            context.Set(log, l => l.CreatedAt, context.UtcDay(-30 * monthsBack, 10));
            if (monthsBack > 0)
            {
                log.MarkFiled(fileName: $"Planilla_PILA_{log.Period}.pdf", fileSize: 184_000, contentType: "application/pdf");
                context.Set(log, l => l.FiledAt, context.UtcDay(-30 * monthsBack + 3, 11));
            }
            logs.Add(log);
        }
        return logs;
    }
}
