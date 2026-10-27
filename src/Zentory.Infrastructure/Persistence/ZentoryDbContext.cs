using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Ai;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence;

public class ZentoryDbContext : DbContext, IZentoryDbContext
{
    private readonly ITenantContext? _tenant;

    public ZentoryDbContext(DbContextOptions<ZentoryDbContext> options, ITenantContext tenant)
        : base(options) { _tenant = tenant; }

    public ZentoryDbContext(DbContextOptions<ZentoryDbContext> options)
        : base(options) { }

    // PLAN-0018 RF-11 (ADR-0008 §1; SR-0018.6). Sin HttpContext (Hangfire, seed, CLI, fábrica de diseño) no hay
    // principal autenticado y el filtro de tenant queda apagado. Con un principal autenticado de cualquier esquema
    // (usuario o colaborador) se filtra por su organización activa, y si no trae un active_org_id válido el filtro
    // compara contra Guid.Empty: falla cerrado, sin filas.
    private bool TenantFilterActive => _tenant?.IsAuthenticated == true;

    private Guid TenantOrganizationId
        => TenantFilterActive && _tenant!.HasActiveOrganization ? _tenant.OrganizationId : Guid.Empty;

    // ── Auth / Identity ──────────────────────────────────────────────────────────
    public DbSet<Organization>               Organizations               { get; set; } = default!;
    public DbSet<User>                       Users                       { get; set; } = default!;
    public DbSet<RefreshToken>               RefreshTokens               { get; set; } = default!;
    public DbSet<OrganizationMember>         OrganizationMembers         { get; set; } = default!;
    public DbSet<OAuthAccount>               OAuthAccounts               { get; set; } = default!;
    public DbSet<PasswordResetToken>         PasswordResetTokens         { get; set; } = default!;
    public DbSet<EmailVerificationToken>     EmailVerificationTokens     { get; set; } = default!;
    public DbSet<CollaboratorAccessToken>    CollaboratorAccessTokens    { get; set; } = default!;
    public DbSet<OrganizationInvitation>     OrganizationInvitations     { get; set; } = default!;
    public DbSet<OrganizationOwnershipTransfer> OrganizationOwnershipTransfers { get; set; } = default!;

    // ── Tenant configuration ─────────────────────────────────────────────────────
    public DbSet<OrganizationSettings>       OrganizationSettings        { get; set; } = default!;
    public DbSet<OrganizationBankAccount>    OrganizationBankAccounts    { get; set; } = default!;
    public DbSet<OrganizationInvoiceSequence> OrganizationInvoiceSequences { get; set; } = default!;

    // ── Integrations ─────────────────────────────────────────────────────────────
    public DbSet<IntegrationCatalog>         IntegrationCatalog          { get; set; } = default!;
    public DbSet<OrganizationIntegration>    OrganizationIntegrations    { get; set; } = default!;

    // ── CRM ──────────────────────────────────────────────────────────────────────
    public DbSet<Client>                     Clients                     { get; set; } = default!;
    public DbSet<ClientContact>              ClientContacts              { get; set; } = default!;

    // ── Projects ─────────────────────────────────────────────────────────────────
    public DbSet<Project>                    Projects                    { get; set; } = default!;
    public DbSet<ProjectCollaborator>        ProjectCollaborators        { get; set; } = default!;
    public DbSet<ProjectFinancials>          ProjectFinancials           { get; set; } = default!;
    public DbSet<ProjectTask>                ProjectTasks                { get; set; } = default!;
    public DbSet<ProjectMilestone>           ProjectMilestones           { get; set; } = default!;
    public DbSet<ProjectShare>               ProjectShares               { get; set; } = default!;
    public DbSet<ProjectDeliverable>         ProjectDeliverables         { get; set; } = default!;
    public DbSet<ProjectBillingEntry>        ProjectBillingEntries       { get; set; } = default!;
    public DbSet<ProjectFile>                ProjectFiles                { get; set; } = default!;
    public DbSet<ProjectActivityLog>         ProjectActivityLogs         { get; set; } = default!;

    // ── Catalog ──────────────────────────────────────────────────────────────────
    public DbSet<ServiceCatalog>             ServiceCatalog              { get; set; } = default!;

    // ── Proposals ────────────────────────────────────────────────────────────────
    public DbSet<ProposalTemplate>           ProposalTemplates           { get; set; } = default!;
    public DbSet<Proposal>                   Proposals                   { get; set; } = default!;
    public DbSet<ProposalSection>            ProposalSections            { get; set; } = default!;
    public DbSet<ProposalItem>               ProposalItems               { get; set; } = default!;

    // ── Invoices ─────────────────────────────────────────────────────────────────
    public DbSet<Invoice>                    Invoices                    { get; set; } = default!;
    public DbSet<InvoiceItem>                InvoiceItems                { get; set; } = default!;
    public DbSet<InvoicePayment>             InvoicePayments             { get; set; } = default!;

    // ── Team / PILA ──────────────────────────────────────────────────────────────
    public DbSet<Collaborator>               Collaborators               { get; set; } = default!;
    public DbSet<PilaVerification>           PilaVerifications           { get; set; } = default!;
    public DbSet<PilaVerificationDocument>   PilaVerificationDocuments   { get; set; } = default!;
    public DbSet<CollaboratorPayoutInvoice>  CollaboratorPayoutInvoices  { get; set; } = default!;
    public DbSet<SsCalculationLog>           SsCalculationLogs           { get; set; } = default!;

    // ── Time tracking & Finances ─────────────────────────────────────────────────
    public DbSet<TimeEntry>                  TimeEntries                 { get; set; } = default!;
    public DbSet<CashFlowEntry>              CashFlowEntries             { get; set; } = default!;

    // ── Observability ────────────────────────────────────────────────────────────
    public DbSet<ActivityLog>                ActivityLogs                { get; set; } = default!;
    public DbSet<AuditLog>                   AuditLogs                   { get; set; } = default!;
    public DbSet<SystemEvent>                SystemEvents                { get; set; } = default!;
    public DbSet<Notification>               Notifications               { get; set; } = default!;

    // ── Legal archive (PLAN-0018 RF-79): sin tenant ni filtro global; solo lo usa la purga ──────────────────────
    public DbSet<LegalDocumentArchive>       LegalDocumentArchives       { get; set; } = default!;

    // ── Marca de base local (PLAN-0018 RF-63 h, M3a): tabla sin tenant ni filtro global (ADR-0008); la escriben db init
    // y el arranque en Development, nunca una migración ─────────────────────────────────────────────────────────────
    public DbSet<DatabaseMarker>             DatabaseMarkers             { get; set; } = default!;

    // ── Master / reference data ──────────────────────────────────────────────────
    public DbSet<Country>                    Countries                   { get; set; } = default!;
    public DbSet<Currency>                   Currencies                  { get; set; } = default!;
    public DbSet<Industry>                   Industries                  { get; set; } = default!;
    public DbSet<TaxType>                     TaxTypes                    { get; set; } = default!;
    public DbSet<ArlRiskLevel>               ArlRiskLevels               { get; set; } = default!;
    public DbSet<ExpenseCategory>            ExpenseCategories           { get; set; } = default!;
    public DbSet<UnitOfMeasure>              UnitsOfMeasure              { get; set; } = default!;
    public DbSet<SsRule>                     SsRules                     { get; set; } = default!;
    public DbSet<ExchangeRate>               ExchangeRates               { get; set; } = default!;

    // ── Billing (Zentory's own subscription billing) ─────────────────────────────
    public DbSet<PaymentGateway>             PaymentGateways             { get; set; } = default!;
    public DbSet<GatewayRoutingRule>         GatewayRoutingRules         { get; set; } = default!;
    public DbSet<BillingPlan>                BillingPlans                { get; set; } = default!;
    public DbSet<PlanLimit>                  PlanLimits                  { get; set; } = default!;
    public DbSet<PlanMarketing>              PlanMarketing               { get; set; } = default!;
    public DbSet<PlanFeature>                PlanFeatures                { get; set; } = default!;
    public DbSet<PlanCompareItem>            PlanCompareItems            { get; set; } = default!;
    public DbSet<BillingCustomer>            BillingCustomers            { get; set; } = default!;
    public DbSet<PaymentMethod>              PaymentMethods              { get; set; } = default!;
    public DbSet<Subscription>               Subscriptions               { get; set; } = default!;
    public DbSet<BillingInvoice>             BillingInvoices             { get; set; } = default!;
    public DbSet<BillingPayment>             BillingPayments             { get; set; } = default!;
    public DbSet<BillingCredit>              BillingCredits              { get; set; } = default!;
    public DbSet<WebhookEvent>               WebhookEvents               { get; set; } = default!;
    public DbSet<CouponCode>                 CouponCodes                 { get; set; } = default!;
    public DbSet<CouponRedemption>           CouponRedemptions           { get; set; } = default!;

    // ── AI ───────────────────────────────────────────────────────────────────────
    public DbSet<AiProvider>                 AiProviders                 { get; set; } = default!;
    public DbSet<AiModel>                    AiModels                    { get; set; } = default!;
    public DbSet<AiFeature>                  AiFeatures                  { get; set; } = default!;
    public DbSet<AiFeatureConfig>            AiFeatureConfigs            { get; set; } = default!;
    public DbSet<AiPromptTemplate>           AiPromptTemplates           { get; set; } = default!;
    public DbSet<AiUsageLog>                 AiUsageLogs                 { get; set; } = default!;
    public DbSet<AiMonthlyUsage>             AiMonthlyUsages             { get; set; } = default!;
    public DbSet<AiQuotaOverride>            AiQuotaOverrides            { get; set; } = default!;
    public DbSet<AiGenerationFeedback>       AiGenerationFeedbacks       { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ZentoryDbContext).Assembly);

        // Los filtros leen TenantFilterActive y TenantOrganizationId del contexto que ejecuta la consulta, no una
        // variable local: EF cachea el modelo por tipo, así que una local quedaba fijada por el primer contexto del
        // proceso (H-08). Sobre miembros de la instancia, EF los parametriza por consulta (ADR-0008 §1).
        // ── Organization: borrado lógico, sin tenant (PLAN-0018 RF-65; ADR purga §1). Falla cerrado: toda lectura deja de
        // ver la organización eliminada sin tocar cada handler. Las que necesitan verla están en la lista blanca de
        // IgnoreQueryFilters (ADR-0008 §3).
        modelBuilder.Entity<Organization>().HasQueryFilter(o => o.DeletedAt == null);

        // ── Client / Project: legacy IsDeleted pattern (preserved from InitialCreate migration)
        modelBuilder.Entity<Client>().HasQueryFilter(c =>
            !c.IsDeleted &&
            (!TenantFilterActive || c.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<Project>().HasQueryFilter(p =>
            !p.IsDeleted &&
            (!TenantFilterActive || p.OrganizationId == TenantOrganizationId));

        // ── DeletedAt pattern: all new tenant entities
        modelBuilder.Entity<Collaborator>().HasQueryFilter(c =>
            c.DeletedAt == null &&
            (!TenantFilterActive || c.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<CollaboratorPayoutInvoice>().HasQueryFilter(p =>
            p.DeletedAt == null &&
            (!TenantFilterActive || p.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<Invoice>().HasQueryFilter(i =>
            i.DeletedAt == null &&
            (!TenantFilterActive || i.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<Proposal>().HasQueryFilter(p =>
            p.DeletedAt == null &&
            (!TenantFilterActive || p.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<TimeEntry>().HasQueryFilter(t =>
            t.DeletedAt == null &&
            (!TenantFilterActive || t.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<ProjectTask>().HasQueryFilter(t =>
            t.DeletedAt == null &&
            (!TenantFilterActive || t.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<ProjectShare>().HasQueryFilter(s =>
            s.DeletedAt == null &&
            (!TenantFilterActive || s.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<CashFlowEntry>().HasQueryFilter(c =>
            c.DeletedAt == null &&
            (!TenantFilterActive || c.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<OrganizationMember>().HasQueryFilter(m =>
            m.DeletedAt == null &&
            (!TenantFilterActive || m.OrganizationId == TenantOrganizationId));

        // Invitaciones (PLAN-0018 RF-49 b, M4): la aceptación las busca por hash sin tenant, en la lista blanca de
        // IgnoreQueryFilters (ADR-0008 §3; T29b).
        modelBuilder.Entity<OrganizationInvitation>().HasQueryFilter(i =>
            i.DeletedAt == null &&
            (!TenantFilterActive || i.OrganizationId == TenantOrganizationId));

        // Propuestas de transferencia (PLAN-0018 RF-64, M4c): sin borrado lógico, el estado las resuelve. La lectura del
        // destino por to_user_id (RF-83 i, T30c) va en la lista blanca de IgnoreQueryFilters.
        modelBuilder.Entity<OrganizationOwnershipTransfer>().HasQueryFilter(t =>
            !TenantFilterActive || t.OrganizationId == TenantOrganizationId);

        modelBuilder.Entity<OrganizationBankAccount>().HasQueryFilter(b =>
            b.DeletedAt == null &&
            (!TenantFilterActive || b.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<OrganizationIntegration>().HasQueryFilter(oi =>
            (!TenantFilterActive || oi.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<ServiceCatalog>().HasQueryFilter(s =>
            s.DeletedAt == null &&
            (!TenantFilterActive || s.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<ProposalTemplate>().HasQueryFilter(t =>
            t.DeletedAt == null &&
            (!TenantFilterActive || t.OrganizationId == TenantOrganizationId));

        modelBuilder.Entity<ClientContact>().HasQueryFilter(c =>
            c.DeletedAt == null &&
            (!TenantFilterActive || c.OrganizationId == TenantOrganizationId));

        base.OnModelCreating(modelBuilder);
    }
}
