using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Ai;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Entities.Master;

namespace Zentory.Application.Common.Interfaces;

public interface IZentoryDbContext
{
    // ── Auth / Identity ──────────────────────────────────────────────────────────
    DbSet<global::Zentory.Domain.Entities.Organization> Organizations               { get; }
    DbSet<User>                        Users                       { get; }
    DbSet<RefreshToken>                RefreshTokens               { get; }
    DbSet<OrganizationMember>          OrganizationMembers         { get; }
    DbSet<OAuthAccount>                OAuthAccounts               { get; }
    DbSet<PasswordResetToken>          PasswordResetTokens         { get; }
    DbSet<EmailVerificationToken>      EmailVerificationTokens     { get; }

    // ── Tenant configuration ─────────────────────────────────────────────────────
    DbSet<OrganizationSettings>        OrganizationSettings        { get; }
    DbSet<OrganizationBankAccount>     OrganizationBankAccounts    { get; }
    DbSet<OrganizationInvoiceSequence> OrganizationInvoiceSequences { get; }

    // ── Integrations ─────────────────────────────────────────────────────────────
    DbSet<IntegrationCatalog>          IntegrationCatalog          { get; }
    DbSet<OrganizationIntegration>     OrganizationIntegrations    { get; }

    // ── CRM ──────────────────────────────────────────────────────────────────────
    DbSet<Client>                      Clients                     { get; }
    DbSet<ClientContact>               ClientContacts              { get; }

    // ── Projects ─────────────────────────────────────────────────────────────────
    DbSet<Project>                     Projects                    { get; }
    DbSet<ProjectCollaborator>         ProjectCollaborators        { get; }
    DbSet<ProjectFinancials>           ProjectFinancials           { get; }
    DbSet<ProjectTask>                 ProjectTasks                { get; }
    DbSet<ProjectMilestone>            ProjectMilestones           { get; }
    DbSet<ProjectShare>                ProjectShares               { get; }
    DbSet<ProjectDeliverable>          ProjectDeliverables         { get; }
    DbSet<ProjectBillingEntry>         ProjectBillingEntries       { get; }
    DbSet<ProjectFile>                 ProjectFiles                { get; }
    DbSet<ProjectActivityLog>          ProjectActivityLogs         { get; }

    // ── Catalog ──────────────────────────────────────────────────────────────────
    DbSet<ServiceCatalog>              ServiceCatalog              { get; }

    // ── Proposals ────────────────────────────────────────────────────────────────
    DbSet<ProposalTemplate>            ProposalTemplates           { get; }
    DbSet<Proposal>                    Proposals                   { get; }
    DbSet<ProposalSection>             ProposalSections            { get; }
    DbSet<ProposalItem>                ProposalItems               { get; }

    // ── Invoices ─────────────────────────────────────────────────────────────────
    DbSet<Invoice>                     Invoices                    { get; }
    DbSet<InvoiceItem>                 InvoiceItems                { get; }
    DbSet<InvoicePayment>              InvoicePayments             { get; }

    // ── Team / PILA ──────────────────────────────────────────────────────────────
    DbSet<Collaborator>                Collaborators               { get; }
    DbSet<PilaVerification>            PilaVerifications           { get; }
    DbSet<SsCalculationLog>            SsCalculationLogs           { get; }

    // ── Time tracking & Finances ─────────────────────────────────────────────────
    DbSet<TimeEntry>                   TimeEntries                 { get; }
    DbSet<CashFlowEntry>               CashFlowEntries             { get; }

    // ── Observability ────────────────────────────────────────────────────────────
    DbSet<ActivityLog>                 ActivityLogs                { get; }
    DbSet<AuditLog>                    AuditLogs                   { get; }
    DbSet<SystemEvent>                 SystemEvents                { get; }
    DbSet<Notification>                Notifications               { get; }

    // ── Master / reference data ──────────────────────────────────────────────────
    DbSet<Country>                     Countries                   { get; }
    DbSet<Currency>                    Currencies                  { get; }
    DbSet<Industry>                    Industries                  { get; }
    DbSet<TaxType>                     TaxTypes                    { get; }
    DbSet<ArlRiskLevel>                ArlRiskLevels               { get; }
    DbSet<ExpenseCategory>             ExpenseCategories           { get; }
    DbSet<UnitOfMeasure>               UnitsOfMeasure              { get; }
    DbSet<SsRule>                      SsRules                     { get; }
    DbSet<ExchangeRate>                ExchangeRates               { get; }

    // ── Billing ──────────────────────────────────────────────────────────────────
    DbSet<PaymentGateway>              PaymentGateways             { get; }
    DbSet<GatewayRoutingRule>          GatewayRoutingRules         { get; }
    DbSet<BillingPlan>                 BillingPlans                { get; }
    DbSet<PlanLimit>                   PlanLimits                  { get; }
    DbSet<PlanMarketing>               PlanMarketing               { get; }
    DbSet<PlanFeature>                 PlanFeatures                { get; }
    DbSet<PlanCompareItem>             PlanCompareItems            { get; }
    DbSet<BillingCustomer>             BillingCustomers            { get; }
    DbSet<PaymentMethod>               PaymentMethods              { get; }
    DbSet<Subscription>                Subscriptions               { get; }
    DbSet<BillingInvoice>              BillingInvoices             { get; }
    DbSet<BillingPayment>              BillingPayments             { get; }
    DbSet<BillingCredit>               BillingCredits              { get; }
    DbSet<WebhookEvent>                WebhookEvents               { get; }
    DbSet<CouponCode>                  CouponCodes                 { get; }
    DbSet<CouponRedemption>            CouponRedemptions           { get; }

    // ── AI ───────────────────────────────────────────────────────────────────────
    DbSet<AiProvider>                  AiProviders                 { get; }
    DbSet<AiModel>                     AiModels                    { get; }
    DbSet<AiFeature>                   AiFeatures                  { get; }
    DbSet<AiFeatureConfig>             AiFeatureConfigs            { get; }
    DbSet<AiPromptTemplate>            AiPromptTemplates           { get; }
    DbSet<AiUsageLog>                  AiUsageLogs                 { get; }
    DbSet<AiMonthlyUsage>              AiMonthlyUsages             { get; }
    DbSet<AiQuotaOverride>             AiQuotaOverrides            { get; }
    DbSet<AiGenerationFeedback>        AiGenerationFeedbacks       { get; }
}
