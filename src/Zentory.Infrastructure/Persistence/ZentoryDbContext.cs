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

    // ── Auth / Identity ──────────────────────────────────────────────────────────
    public DbSet<Organization>               Organizations               { get; set; } = default!;
    public DbSet<User>                       Users                       { get; set; } = default!;
    public DbSet<RefreshToken>               RefreshTokens               { get; set; } = default!;
    public DbSet<OrganizationMember>         OrganizationMembers         { get; set; } = default!;
    public DbSet<OAuthAccount>               OAuthAccounts               { get; set; } = default!;
    public DbSet<PasswordResetToken>         PasswordResetTokens         { get; set; } = default!;
    public DbSet<EmailVerificationToken>     EmailVerificationTokens     { get; set; } = default!;

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
    public DbSet<SsCalculationLog>           SsCalculationLogs           { get; set; } = default!;

    // ── Time tracking & Finances ─────────────────────────────────────────────────
    public DbSet<TimeEntry>                  TimeEntries                 { get; set; } = default!;
    public DbSet<CashFlowEntry>              CashFlowEntries             { get; set; } = default!;

    // ── Observability ────────────────────────────────────────────────────────────
    public DbSet<ActivityLog>                ActivityLogs                { get; set; } = default!;
    public DbSet<AuditLog>                   AuditLogs                   { get; set; } = default!;
    public DbSet<SystemEvent>                SystemEvents                { get; set; } = default!;
    public DbSet<Notification>               Notifications               { get; set; } = default!;

    // ── Master / reference data ──────────────────────────────────────────────────
    public DbSet<Country>                    Countries                   { get; set; } = default!;
    public DbSet<Currency>                   Currencies                  { get; set; } = default!;
    public DbSet<Industry>                   Industries                  { get; set; } = default!;
    public DbSet<TaxType>                    TaxTypes                    { get; set; } = default!;
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

        bool tenantActive = _tenant != null && _tenant.IsAuthenticated;

        // ── Client / Project: legacy IsDeleted pattern (preserved from InitialCreate migration)
        modelBuilder.Entity<Client>().HasQueryFilter(c =>
            !c.IsDeleted &&
            (!tenantActive || c.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<Project>().HasQueryFilter(p =>
            !p.IsDeleted &&
            (!tenantActive || p.OrganizationId == _tenant!.OrganizationId));

        // ── DeletedAt pattern: all new tenant entities
        modelBuilder.Entity<Collaborator>().HasQueryFilter(c =>
            c.DeletedAt == null &&
            (!tenantActive || c.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<Invoice>().HasQueryFilter(i =>
            i.DeletedAt == null &&
            (!tenantActive || i.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<Proposal>().HasQueryFilter(p =>
            p.DeletedAt == null &&
            (!tenantActive || p.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<TimeEntry>().HasQueryFilter(t =>
            t.DeletedAt == null &&
            (!tenantActive || t.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<ProjectTask>().HasQueryFilter(t =>
            t.DeletedAt == null &&
            (!tenantActive || t.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<ProjectShare>().HasQueryFilter(s =>
            s.DeletedAt == null &&
            (!tenantActive || s.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<CashFlowEntry>().HasQueryFilter(c =>
            c.DeletedAt == null &&
            (!tenantActive || c.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<OrganizationMember>().HasQueryFilter(m =>
            m.DeletedAt == null &&
            (!tenantActive || m.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<OrganizationBankAccount>().HasQueryFilter(b =>
            b.DeletedAt == null &&
            (!tenantActive || b.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<OrganizationIntegration>().HasQueryFilter(oi =>
            (!tenantActive || oi.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<ServiceCatalog>().HasQueryFilter(s =>
            s.DeletedAt == null &&
            (!tenantActive || s.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<ProposalTemplate>().HasQueryFilter(t =>
            t.DeletedAt == null &&
            (!tenantActive || t.OrganizationId == _tenant!.OrganizationId));

        modelBuilder.Entity<ClientContact>().HasQueryFilter(c =>
            c.DeletedAt == null &&
            (!tenantActive || c.OrganizationId == _tenant!.OrganizationId));

        base.OnModelCreating(modelBuilder);
    }
}
