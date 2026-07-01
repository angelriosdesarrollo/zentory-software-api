using Zentory.Application.Projects.Queries;

namespace Zentory.Application.Projects;

/// <summary>
/// Provisional in-memory store for project expenses.
/// Seeded with dev fixture data keyed to fixed dev project GUIDs.
/// Replace with a real EF-backed repository once the ProjectExpense domain entity and migration exist.
/// </summary>
public sealed class ProjectExpenseStore
{
    // Fixed dev project GUIDs — must match DevDataSeeder.PrjPortalId etc.
    private static readonly Guid PrjPortal = new("c3000000-0001-0000-0000-000000000001");
    private static readonly Guid PrjMobile = new("c3000000-0002-0000-0000-000000000002");
    private static readonly Guid PrjData   = new("c3000000-0003-0000-0000-000000000003");

    private readonly List<ProjectExpenseDto> _expenses;
    private readonly object _lock = new();

    public ProjectExpenseStore()
    {
        _expenses = [.. Seed()];
    }

    public IReadOnlyList<ProjectExpenseDto> GetByProject(Guid projectId)
    {
        lock (_lock)
            return _expenses.Where(e => e.ProjectId == projectId)
                            .OrderByDescending(e => e.Date)
                            .ToList();
    }

    public ProjectExpenseDto Add(ProjectExpenseDto expense)
    {
        lock (_lock)
        {
            _expenses.Add(expense);
            return expense;
        }
    }

    private static IEnumerable<ProjectExpenseDto> Seed() =>
    [
        // ── Portal Corporativo (USD) ─────────────────────────────────────────
        new(Guid.NewGuid(), PrjPortal, "2026-02-10", "subcontratista", "Backend senior — módulo de pagos",  3200m,   "USD", "Juan Cárdenas",       false, "aprobado",  "DR"),
        new(Guid.NewGuid(), PrjPortal, "2026-02-20", "licencias",      "Figma Teams (3 meses)",             135m,    "USD", "Figma Inc.",           false, "aprobado",  "MV"),
        new(Guid.NewGuid(), PrjPortal, "2026-03-05", "licencias",      "AWS EC2 + RDS — entorno staging",   890m,    "USD", "Amazon Web Services",  true,  "aprobado",  "LP"),
        new(Guid.NewGuid(), PrjPortal, "2026-04-01", "otro",           "Suscripción Linear (equipo)",        48m,    "USD", "Linear",               false, "pendiente", "DR"),

        // ── App Móvil Fintech (USD) ──────────────────────────────────────────
        new(Guid.NewGuid(), PrjMobile, "2025-10-15", "subcontratista", "Consultor seguridad bancaria",      4800m,   "USD", "SecureCode SAS",       false, "aprobado",  "DR"),
        new(Guid.NewGuid(), PrjMobile, "2025-11-03", "licencias",      "SonarQube Enterprise (6 meses)",    780m,    "USD", "SonarSource",          false, "aprobado",  "JS"),
        new(Guid.NewGuid(), PrjMobile, "2026-01-08", "subcontratista", "QA specialist — ciclo de pruebas",  1800m,   "USD", "Camila Herrera",       false, "aprobado",  "DR"),
        new(Guid.NewGuid(), PrjMobile, "2026-02-22", "licencias",      "GitHub Advanced Security",          320m,    "USD", "GitHub",               false, "pendiente", "CR"),

        // ── Plataforma de Datos (USD) ────────────────────────────────────────
        new(Guid.NewGuid(), PrjData,   "2026-02-15", "licencias",      "Shopify partners + APIs",           290m,    "USD", "Shopify",              true,  "aprobado",  "LP"),
        new(Guid.NewGuid(), PrjData,   "2026-03-10", "subcontratista", "Diseñador UX freelance",            950m,    "USD", "Andrés Molina",        false, "aprobado",  "LP"),
    ];
}
