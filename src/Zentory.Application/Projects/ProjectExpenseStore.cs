using Zentory.Application.Projects.Queries;

namespace Zentory.Application.Projects;

/// <summary>
/// Provisional in-memory store for project expenses.
/// Seeded with dev fixture data keyed to fixed dev project GUIDs.
/// Replace with a real EF-backed repository once the ProjectExpense domain entity and migration exist.
/// </summary>
public sealed class ProjectExpenseStore
{
    // Debe coincidir con DevDataSeeder.DevOrgId: la semilla pertenece solo a la organización de dev.
    private static readonly Guid DevOrg = new("a1000000-0000-0000-0000-000000000001");

    // Fixed dev project GUIDs — must match DevDataSeeder.PrjPortalId etc.
    private static readonly Guid PrjPortal = new("c3000000-0001-0000-0000-000000000001");
    private static readonly Guid PrjMobile = new("c3000000-0002-0000-0000-000000000002");
    private static readonly Guid PrjData   = new("c3000000-0003-0000-0000-000000000003");

    // La organización vive solo en el registro interno para que ProjectExpenseDto (y su JSON)
    // no cambie; todo acceso público exige organizationId y filtra por él (R-2).
    private sealed record Entry(Guid OrganizationId, ProjectExpenseDto Expense);

    private readonly List<Entry> _entries;
    private readonly object _lock = new();

    public ProjectExpenseStore()
    {
        _entries = [.. Seed().Select(e => new Entry(DevOrg, e))];
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public IReadOnlyList<ProjectExpenseDto> GetByProject(Guid organizationId, Guid projectId)
    {
        lock (_lock)
            return _entries.Where(e => e.OrganizationId == organizationId && e.Expense.ProjectId == projectId)
                           .Select(e => e.Expense)
                           .OrderByDescending(e => e.Date)
                           .ToList();
    }

    public IReadOnlyList<ProjectExpenseDto> GetAll(Guid organizationId)
    {
        lock (_lock)
            return _entries.Where(e => e.OrganizationId == organizationId)
                           .Select(e => e.Expense)
                           .OrderByDescending(e => e.Date)
                           .ToList();
    }

    public ProjectExpenseDto Add(Guid organizationId, ProjectExpenseDto expense)
    {
        lock (_lock)
        {
            _entries.Add(new Entry(organizationId, expense));
            return expense;
        }
    }

    public ProjectExpenseDto? GetById(Guid organizationId, Guid id)
    {
        lock (_lock)
            return _entries.FirstOrDefault(e => e.OrganizationId == organizationId && e.Expense.Id == id)?.Expense;
    }

    // Devuelve null si el gasto no existe en esa organización; la organización del Entry
    // original se conserva (un Update nunca mueve un gasto de organización).
    public ProjectExpenseDto? Update(Guid organizationId, ProjectExpenseDto expense)
    {
        lock (_lock)
        {
            var index = _entries.FindIndex(e => e.OrganizationId == organizationId && e.Expense.Id == expense.Id);
            if (index < 0) return null;

            _entries[index] = _entries[index] with { Expense = expense };
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
