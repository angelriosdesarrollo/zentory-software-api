using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Ai;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Entities.Master;
using static Zentory.Domain.Constants.PlanLimits;

namespace Zentory.Infrastructure.Persistence.Seeding;

/// <summary>
/// Catálogos globales (no por tenant): planes con su marketing, features, límites y comparativa, reglas de seguridad
/// social, categorías de gasto, catálogo de integraciones y la configuración mínima de IA (PLAN-0018 RF-33, D-5).
/// Inserta por clave natural solo lo que falta y nunca modifica una fila existente, así que se puede correr en cada
/// arranque y sobre una base con catálogos editados a mano.
/// </summary>
public sealed class ReferenceDataSeeder
{
    private const string SsCountry = "CO";
    private const short  SsYear    = 2026;

    // SMMLV Colombia 2026: $1.423.500 COP
    private const decimal Smlv2026 = 1_423_500m;

    private readonly ZentoryDbContext _db;

    public ReferenceDataSeeder(ZentoryDbContext db) => _db = db;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedBillingPlansAsync(ct);
        await SeedSsRulesAsync(ct);
        await SeedExpenseCategoriesAsync(ct);
        await SeedIntegrationCatalogAsync(ct);
        await SeedAiConfigAsync(ct);
    }

    // ── Planes ──────────────────────────────────────────────────────────────────

    private async Task SeedBillingPlansAsync(CancellationToken ct)
    {
        await AddMissingAsync(_db.BillingPlans, p => p.Name,
        [
            BillingPlan.Create(Plan.Free,   "Free",   0m,    0m,    sortOrder: 0),
            BillingPlan.Create(Plan.Pro,    "Pro",    36m,   29m,   sortOrder: 1),
            BillingPlan.Create(Plan.Studio, "Studio", 109m,  89m,   sortOrder: 2),
        ], ct);

        // Los hijos cuelgan del Id real de cada plan, sea el recién insertado o uno que ya existía.
        var planIds = await _db.BillingPlans.AsNoTracking().ToDictionaryAsync(p => p.Name, p => p.Id, ct);
        var free   = planIds[Plan.Free];
        var pro    = planIds[Plan.Pro];
        var studio = planIds[Plan.Studio];

        // ── Plan Marketing ───────────────────────────────────────────────────
        await AddMissingAsync(_db.PlanMarketing, m => (m.PlanId, m.LegalType),
        [
            // Freelance
            PlanMarketing.Create(free,   LegalType.Freelance, "Para empezar a ordenar tu operación sin costo.",         "Empezar gratis",   isPopular: false, featuresHeading: null),
            PlanMarketing.Create(pro,    LegalType.Freelance, "Para el independiente que ya vive de esto.",              "Comenzar Pro",     isPopular: true,  featuresHeading: "Todo lo de Free, más"),
            PlanMarketing.Create(studio, LegalType.Freelance, "Para el freelance que quiere dominar su cash flow.",      "Comenzar Studio",  isPopular: false, featuresHeading: "Todo lo de Pro, más"),
            // Empresa
            PlanMarketing.Create(free,   LegalType.Empresa,   "Para empezar a ordenar la operación sin costo.",         "Empezar gratis",   isPopular: false, featuresHeading: null),
            PlanMarketing.Create(pro,    LegalType.Empresa,   "Para la empresa que factura formalmente.",                "Comenzar Pro",     isPopular: true,  featuresHeading: "Todo lo de Free, más"),
            PlanMarketing.Create(studio, LegalType.Empresa,   "Para agencias y software factories con equipo.",          "Comenzar Studio",  isPopular: false, featuresHeading: "Todo lo de Pro, más"),
        ], ct);

        // ── Plan Features ────────────────────────────────────────────────────
        // Clave: plan + tipo legal + posición (el índice de consulta), para que editar el texto no duplique la fila.
        await AddMissingAsync(_db.PlanFeatures, f => (f.PlanId, f.LegalType, f.SortOrder),
        [
            // Freelance Free
            PlanFeature.Create(free,   LegalType.Freelance, "Hasta 2 clientes",                         sortOrder: 0),
            PlanFeature.Create(free,   LegalType.Freelance, "3 cobros / mes",                           sortOrder: 1),
            PlanFeature.Create(free,   LegalType.Freelance, "Proyectos básicos",                        sortOrder: 2),
            PlanFeature.Create(free,   LegalType.Freelance, "Calculadora PILA (seguridad social)", isHighlight: true, badgeText: "siempre gratis", sortOrder: 3),
            // Freelance Pro
            PlanFeature.Create(pro,    LegalType.Freelance, "Clientes y cobros ilimitados",             sortOrder: 0),
            PlanFeature.Create(pro,    LegalType.Freelance, "Sin marca Zentory en documentos",          sortOrder: 1),
            PlanFeature.Create(pro,    LegalType.Freelance, "Tracking de apertura de propuestas",       sortOrder: 2),
            PlanFeature.Create(pro,    LegalType.Freelance, "Multimoneda (USD, COP, EUR…)",             sortOrder: 3),
            PlanFeature.Create(pro,    LegalType.Freelance, "Recordatorios automáticos de cobro",       sortOrder: 4),
            // Freelance Studio
            PlanFeature.Create(studio, LegalType.Freelance, "Vista Gantt / Timeline",                  sortOrder: 0),
            PlanFeature.Create(studio, LegalType.Freelance, "Flujo de caja y runway",                  sortOrder: 1),
            PlanFeature.Create(studio, LegalType.Freelance, "Flujo proyectado a 3 meses",              sortOrder: 2),
            PlanFeature.Create(studio, LegalType.Freelance, "Portal cliente completo",                  sortOrder: 3),
            // Empresa Free
            PlanFeature.Create(free,   LegalType.Empresa,   "Hasta 2 clientes",                        sortOrder: 0),
            PlanFeature.Create(free,   LegalType.Empresa,   "3 facturas / mes",                        sortOrder: 1),
            PlanFeature.Create(free,   LegalType.Empresa,   "Proyectos básicos",                       sortOrder: 2),
            PlanFeature.Create(free,   LegalType.Empresa,   "Calculadora PILA (seguridad social)", isHighlight: true, badgeText: "siempre gratis", sortOrder: 3),
            // Empresa Pro
            PlanFeature.Create(pro,    LegalType.Empresa,   "Clientes y facturas ilimitados",          sortOrder: 0),
            PlanFeature.Create(pro,    LegalType.Empresa,   "Sin marca Zentory en documentos",         sortOrder: 1),
            PlanFeature.Create(pro,    LegalType.Empresa,   "Facturación Electrónica DIAN", isHighlight: true, sortOrder: 2),
            PlanFeature.Create(pro,    LegalType.Empresa,   "Tracking de apertura de propuestas",      sortOrder: 3),
            PlanFeature.Create(pro,    LegalType.Empresa,   "Multimoneda (USD, COP, EUR…)",            sortOrder: 4),
            PlanFeature.Create(pro,    LegalType.Empresa,   "Recordatorios automáticos de cobro",      sortOrder: 5),
            // Empresa Studio
            PlanFeature.Create(studio, LegalType.Empresa,   "Rentabilidad real por proyecto",          sortOrder: 0),
            PlanFeature.Create(studio, LegalType.Empresa,   "Flujo de caja y runway",                  sortOrder: 1),
            PlanFeature.Create(studio, LegalType.Empresa,   "Colaboradores y cuentas por pagar",       sortOrder: 2),
            PlanFeature.Create(studio, LegalType.Empresa,   "Multi-usuario y roles de acceso",         sortOrder: 3),
            PlanFeature.Create(studio, LegalType.Empresa,   "Propuestas con margen y equipo",          sortOrder: 4),
        ], ct);

        // ── Plan Limits ──────────────────────────────────────────────────────
        // max_projects va sin límite (null) en todos los planes: FEATURE_PERMISSIONS §3 es la fuente (RF-41).
        await AddMissingAsync(_db.PlanLimits, l => (l.PlanId, l.LegalType, l.FeatureKey),
        [
            // Freelance Free
            PlanLimit.Create(free,   LegalType.Freelance, FeatureKeys.MaxClients,        2),
            PlanLimit.Create(free,   LegalType.Freelance, FeatureKeys.MaxInvoicesMonth,  3),
            PlanLimit.Create(free,   LegalType.Freelance, FeatureKeys.MaxProjects,       null),
            PlanLimit.Create(free,   LegalType.Freelance, FeatureKeys.MaxCollaborators,  0),
            PlanLimit.Create(free,   LegalType.Freelance, FeatureKeys.MaxOrgMembers,     1),
            PlanLimit.Create(free,   LegalType.Freelance, FeatureKeys.MaxOwnedOrgs,      1),
            // Freelance Pro / Studio — sin límite (null)
            PlanLimit.Create(pro,    LegalType.Freelance, FeatureKeys.MaxClients,        null),
            PlanLimit.Create(pro,    LegalType.Freelance, FeatureKeys.MaxInvoicesMonth,  null),
            PlanLimit.Create(pro,    LegalType.Freelance, FeatureKeys.MaxProjects,       null),
            PlanLimit.Create(pro,    LegalType.Freelance, FeatureKeys.MaxCollaborators,  0),
            PlanLimit.Create(pro,    LegalType.Freelance, FeatureKeys.MaxOrgMembers,     1),
            PlanLimit.Create(pro,    LegalType.Freelance, FeatureKeys.MaxOwnedOrgs,      null),
            PlanLimit.Create(studio, LegalType.Freelance, FeatureKeys.MaxClients,        null),
            PlanLimit.Create(studio, LegalType.Freelance, FeatureKeys.MaxInvoicesMonth,  null),
            PlanLimit.Create(studio, LegalType.Freelance, FeatureKeys.MaxProjects,       null),
            PlanLimit.Create(studio, LegalType.Freelance, FeatureKeys.MaxCollaborators,  null),
            PlanLimit.Create(studio, LegalType.Freelance, FeatureKeys.MaxOrgMembers,     1),
            PlanLimit.Create(studio, LegalType.Freelance, FeatureKeys.MaxOwnedOrgs,      null),
            // Empresa Free
            PlanLimit.Create(free,   LegalType.Empresa,   FeatureKeys.MaxClients,        2),
            PlanLimit.Create(free,   LegalType.Empresa,   FeatureKeys.MaxInvoicesMonth,  3),
            PlanLimit.Create(free,   LegalType.Empresa,   FeatureKeys.MaxProjects,       null),
            PlanLimit.Create(free,   LegalType.Empresa,   FeatureKeys.MaxCollaborators,  0),
            PlanLimit.Create(free,   LegalType.Empresa,   FeatureKeys.MaxOrgMembers,     1),
            PlanLimit.Create(free,   LegalType.Empresa,   FeatureKeys.MaxOwnedOrgs,      1),
            // Empresa Pro / Studio
            PlanLimit.Create(pro,    LegalType.Empresa,   FeatureKeys.MaxClients,        null),
            PlanLimit.Create(pro,    LegalType.Empresa,   FeatureKeys.MaxInvoicesMonth,  null),
            PlanLimit.Create(pro,    LegalType.Empresa,   FeatureKeys.MaxProjects,       null),
            PlanLimit.Create(pro,    LegalType.Empresa,   FeatureKeys.MaxCollaborators,  10),
            PlanLimit.Create(pro,    LegalType.Empresa,   FeatureKeys.MaxOrgMembers,     5),
            PlanLimit.Create(pro,    LegalType.Empresa,   FeatureKeys.MaxOwnedOrgs,      null),
            PlanLimit.Create(studio, LegalType.Empresa,   FeatureKeys.MaxClients,        null),
            PlanLimit.Create(studio, LegalType.Empresa,   FeatureKeys.MaxInvoicesMonth,  null),
            PlanLimit.Create(studio, LegalType.Empresa,   FeatureKeys.MaxProjects,       null),
            PlanLimit.Create(studio, LegalType.Empresa,   FeatureKeys.MaxCollaborators,  null),
            PlanLimit.Create(studio, LegalType.Empresa,   FeatureKeys.MaxOrgMembers,     null),
            PlanLimit.Create(studio, LegalType.Empresa,   FeatureKeys.MaxOwnedOrgs,      null),
        ], ct);

        // ── Plan Compare Items ───────────────────────────────────────────────
        // Clave: tipo legal + posición (el índice de consulta), para que editar la etiqueta no duplique la fila.
        await AddMissingAsync(_db.PlanCompareItems, c => (c.LegalType, c.SortOrder),
        [
            // Shared (both account types)
            PlanCompareItem.Create("Clientes",                   freeValue: "Hasta 2", proValue: "Ilimitados", studioValue: "Ilimitados", sortOrder: 0),
            PlanCompareItem.Create("Proyectos",                  freeValue: "Básicos", proValue: "true",        studioValue: "true",        sortOrder: 2),
            PlanCompareItem.Create("Calculadora PILA",           freeValue: "true",    proValue: "true",        studioValue: "true",        sortOrder: 3),
            PlanCompareItem.Create("Sin marca Zentory",          freeValue: "false",   proValue: "true",        studioValue: "true",        sortOrder: 4),
            PlanCompareItem.Create("Tracking de apertura",       freeValue: "false",   proValue: "true",        studioValue: "true",        sortOrder: 5),
            PlanCompareItem.Create("Multimoneda",                freeValue: "false",   proValue: "true",        studioValue: "true",        sortOrder: 6),
            PlanCompareItem.Create("Flujo de caja / runway",     freeValue: "false",   proValue: "false",       studioValue: "true",        sortOrder: 8),
            PlanCompareItem.Create("Vista Gantt / Timeline",     freeValue: "false",   proValue: "false",       studioValue: "true",        sortOrder: 9, legalType: LegalType.Freelance),
            PlanCompareItem.Create("Portal cliente completo",    freeValue: "false",   proValue: "false",       studioValue: "true",        sortOrder: 10, legalType: LegalType.Freelance),
            // Freelance specific
            PlanCompareItem.Create("Cobros / mes",               freeValue: "3",       proValue: "Ilimitados",  studioValue: "Ilimitados",  sortOrder: 1, legalType: LegalType.Freelance),
            // Empresa specific
            PlanCompareItem.Create("Facturas / mes",             freeValue: "3",       proValue: "Ilimitados",  studioValue: "Ilimitados",  sortOrder: 1, legalType: LegalType.Empresa),
            PlanCompareItem.Create("Facturación Electrónica DIAN", freeValue: "false", proValue: "true",        studioValue: "true",        sortOrder: 7, legalType: LegalType.Empresa, isEmpresaOnly: true),
            PlanCompareItem.Create("Rentabilidad real por proyecto", freeValue: "false",proValue: "false",       studioValue: "true",        sortOrder: 11, legalType: LegalType.Empresa, isEmpresaOnly: true),
            PlanCompareItem.Create("Colaboradores y cuentas por pagar", freeValue: "false", proValue: "false",  studioValue: "true",        sortOrder: 12, legalType: LegalType.Empresa, isEmpresaOnly: true),
            PlanCompareItem.Create("Multi-usuario y roles",      freeValue: "false",   proValue: "false",       studioValue: "true",        sortOrder: 13, legalType: LegalType.Empresa, isEmpresaOnly: true),
        ], ct);
    }

    // ── Seguridad social ────────────────────────────────────────────────────────

    private Task SeedSsRulesAsync(CancellationToken ct) => AddMissingAsync(_db.SsRules,
        r => (r.CountryCode, r.EffectiveYear, r.FundType, r.ContributorType, r.ArlLevel),
        [
            // Salud — independiente (cotiza el total: 12.5%)
            new SsRule { CountryCode = SsCountry, EffectiveYear = SsYear, FundType = "salud",    ContributorType = "independiente", EmployeePct = 0.125m, EmployerPct = 0m,     TotalPct = 0.125m, MinBaseSmlv = 1m, SmlvCop = Smlv2026, Notes = "IBC mínimo = 1 SMMLV" },
            // Pensión — independiente
            new SsRule { CountryCode = SsCountry, EffectiveYear = SsYear, FundType = "pension",  ContributorType = "independiente", EmployeePct = 0.16m,  EmployerPct = 0m,     TotalPct = 0.16m,  MinBaseSmlv = 1m, SmlvCop = Smlv2026, Notes = "Sin fondo de solidaridad si IBC < 4 SMMLV" },
            // ARL — riesgo 1 (0.522%)
            new SsRule { CountryCode = SsCountry, EffectiveYear = SsYear, FundType = "arl",      ContributorType = "independiente", EmployeePct = 0.00522m,EmployerPct = 0m,     TotalPct = 0.00522m, MinBaseSmlv = 1m, SmlvCop = Smlv2026, ArlLevel = 1, Notes = "Riesgo 1: actividades de oficina" },
            // ARL — riesgo 2
            new SsRule { CountryCode = SsCountry, EffectiveYear = SsYear, FundType = "arl",      ContributorType = "independiente", EmployeePct = 0.01044m,EmployerPct = 0m,     TotalPct = 0.01044m, MinBaseSmlv = 1m, SmlvCop = Smlv2026, ArlLevel = 2, Notes = "Riesgo 2" },
            // ARL — riesgo 3
            new SsRule { CountryCode = SsCountry, EffectiveYear = SsYear, FundType = "arl",      ContributorType = "independiente", EmployeePct = 0.02436m,EmployerPct = 0m,     TotalPct = 0.02436m, MinBaseSmlv = 1m, SmlvCop = Smlv2026, ArlLevel = 3, Notes = "Riesgo 3" },
            // Salud — empleado
            new SsRule { CountryCode = SsCountry, EffectiveYear = SsYear, FundType = "salud",    ContributorType = "empleado",      EmployeePct = 0.04m,  EmployerPct = 0.085m, TotalPct = 0.125m, MinBaseSmlv = 1m, SmlvCop = Smlv2026 },
            // Pensión — empleado
            new SsRule { CountryCode = SsCountry, EffectiveYear = SsYear, FundType = "pension",  ContributorType = "empleado",      EmployeePct = 0.04m,  EmployerPct = 0.12m,  TotalPct = 0.16m,  MinBaseSmlv = 1m, SmlvCop = Smlv2026 },
            // Caja de compensación — empleador
            new SsRule { CountryCode = SsCountry, EffectiveYear = SsYear, FundType = "caja_compensacion", ContributorType = "empleador", EmployeePct = 0m, EmployerPct = 0.04m,  TotalPct = 0.04m,  MinBaseSmlv = 1m, SmlvCop = Smlv2026 },
        ], ct);

    // ── Categorías de gasto ─────────────────────────────────────────────────────

    private async Task SeedExpenseCategoriesAsync(CancellationToken ct)
    {
        ExpenseCategory[] categories =
        [
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
            new ExpenseCategory { Id = 12, Name = "Otros ingresos",             Slug = "otros_ingresos",           Type = "income"  },
        ];

        // La clave natural es el slug (índice único), pero el Id también es fijo: una fila con el mismo Id y otro slug
        // es una edición manual, no una ausencia, y reinsertarla chocaría con la clave primaria.
        var existing = await _db.ExpenseCategories.AsNoTracking().Select(c => new { c.Id, c.Slug }).ToListAsync(ct);
        var takenIds   = existing.Select(c => c.Id).ToHashSet();
        var takenSlugs = existing.Select(c => c.Slug).ToHashSet(StringComparer.Ordinal);

        var missing = categories.Where(c => !takenIds.Contains(c.Id) && !takenSlugs.Contains(c.Slug)).ToList();
        if (missing.Count == 0) return;

        _db.ExpenseCategories.AddRange(missing);
        await _db.SaveChangesAsync(ct);
    }

    // ── Integraciones ───────────────────────────────────────────────────────────

    // Con PostgreSQL ya las trae el HasData de la migración inicial; aquí solo se completan (InMemory) sin duplicar.
    private Task SeedIntegrationCatalogAsync(CancellationToken ct) => AddMissingAsync(_db.IntegrationCatalog, i => i.Id,
        [
            IntegrationCatalog.Create("google-meet", "Google Meet", "Videollamadas integradas en propuestas y proyectos",         sortOrder: 1),
            IntegrationCatalog.Create("slack",       "Slack",       "Notificaciones de proyectos e invoices en tu workspace",     sortOrder: 2),
            IntegrationCatalog.Create("github",      "GitHub",      "Vincula repositorios con proyectos y time entries",          sortOrder: 3),
            IntegrationCatalog.Create("figma",       "Figma",       "Adjunta entregables de diseño directamente en propuestas",   sortOrder: 4),
        ], ct);

    // ── IA ──────────────────────────────────────────────────────────────────────

    // Config mínima de IA para el único feature del MVP (enriquecer secciones de propuesta).
    // El resto del catálogo de ai.features (resúmenes, reportes, etc.) se siembra cuando esos
    // features se implementen — no antes.
    private const string AnthropicProvider   = "anthropic";
    private const string HaikuModelId        = "claude-haiku-4-5";
    private const string EnrichFeatureKey    = "proposal_section_enrich";
    private const int    EnrichPromptVersion = 1;

    private async Task SeedAiConfigAsync(CancellationToken ct)
    {
        await AddMissingAsync(_db.AiProviders, p => p.Name,
            [AiProvider.Create(AnthropicProvider, "Anthropic", "https://api.anthropic.com")], ct);
        var providerId = await _db.AiProviders.Where(p => p.Name == AnthropicProvider).Select(p => p.Id).FirstAsync(ct);

        // Precios vigentes por MTok ($1.00 in / $5.00 out) expresados por 1k tokens.
        await AddMissingAsync(_db.AiModels, m => m.ModelId,
        [
            AiModel.Create(
                providerId:      providerId,
                modelId:         HaikuModelId,
                displayName:     "Claude Haiku 4.5",
                contextWindow:   200_000,
                maxOutputTokens: 8192,
                inputCostPer1k:  0.00100000m,
                outputCostPer1k: 0.00500000m,
                supportsVision:  true),
        ], ct);
        var modelId = await _db.AiModels.Where(m => m.ModelId == HaikuModelId).Select(m => m.Id).FirstAsync(ct);

        await AddMissingAsync(_db.AiFeatures, f => f.Key,
        [
            AiFeature.Create(
                key:         EnrichFeatureKey,
                displayName: "Enriquecer sección de propuesta",
                category:    "text_generation",
                description: "Mejora la redacción de una sección de propuesta comercial manteniendo el formato HTML."),
        ], ct);
        var featureId = await _db.AiFeatures.Where(f => f.Key == EnrichFeatureKey).Select(f => f.Id).FirstAsync(ct);

        await AddMissingAsync(_db.AiFeatureConfigs, c => c.FeatureId,
        [
            AiFeatureConfig.Create(
                featureId:       featureId,
                modelId:         modelId,
                minPlan:         Plan.Pro,
                maxInputTokens:  4000,
                maxOutputTokens: 2000,
                temperature:     0.70m,
                monthlyReqLimit: 50),
        ], ct);

        const string systemPrompt = """
            Eres un asistente que mejora la redacción de secciones de propuestas comerciales para
            software factories en Latinoamérica. Recibirás el contenido de una sección en HTML.

            Reglas estrictas:
            - Responde únicamente con HTML válido, sin explicaciones ni comentarios adicionales.
            - Preserva exactamente las mismas etiquetas HTML y su estructura (no agregues, quites
              ni reordenes tags) — solo mejora la redacción del texto dentro de ellas.
            - Usa español latinoamericano, tono profesional y persuasivo, sin tecnicismos innecesarios.
            - No inventes cifras, plazos ni compromisos que no estén ya en el texto original.
            """;

        await AddMissingAsync(_db.AiPromptTemplates, t => (t.FeatureId, t.Version),
        [
            AiPromptTemplate.Create(
                featureId:    featureId,
                version:      EnrichPromptVersion,
                name:         "proposal_section_enrich_v1",
                systemPrompt: systemPrompt,
                createdBy:    "seed"),
        ], ct);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────

    // Inserta los candidatos cuya clave natural no está en la tabla. Las filas existentes no se leen con seguimiento ni
    // se tocan: un catálogo editado a mano conserva sus valores (D-5). Los catálogos son pequeños, así que se leen
    // completos en vez de traducir la clave a SQL.
    private async Task AddMissingAsync<TEntity, TKey>(
        DbSet<TEntity> set, Func<TEntity, TKey> naturalKey, IReadOnlyList<TEntity> candidates, CancellationToken ct)
        where TEntity : class
    {
        var taken = (await set.AsNoTracking().ToListAsync(ct)).Select(naturalKey).ToHashSet();
        var missing = candidates.Where(candidate => taken.Add(naturalKey(candidate))).ToList();
        if (missing.Count == 0) return;

        set.AddRange(missing);
        await _db.SaveChangesAsync(ct);
    }
}
