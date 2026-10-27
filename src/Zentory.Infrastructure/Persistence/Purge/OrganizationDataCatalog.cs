using Microsoft.EntityFrameworkCore.Metadata;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Ai;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Purge;

// Qué hace la purga de una organización con cada tipo del modelo (PLAN-0018 RF-78 4, C-8; ADR purga §2).
public enum OrganizationDataCategory
{
    // Se borra por el predicado explícito OrganizationId == orgId.
    PurgeByOrganization,
    // Hijo sin OrganizationId: se borra por la clave de su padre, antes que el padre.
    PurgeByParent,
    // Facturación de la suscripción del owner: OrganizationId = null, la fila se conserva (C-6).
    Detach,
    // Regla propia: User (OrganizationId y LastActiveOrganizationId a null), CollaboratorAccessToken (por email, solo
    // los de colaboradores sin otra organización vigente; SR-0018.34) y Organization (se borra al final).
    Owned,
    // Del usuario o globales: no se tocan.
    Global,
}

// Un hijo que se purga por la clave de su padre (ForeignKey es la propiedad del hijo que apunta al padre).
public sealed record ParentLink(Type Child, Type Parent, string ForeignKey);

// Clasificación cerrada de todos los tipos de ZentoryDbContext.Model (ADR purga §2). Cada tipo está en exactamente una
// categoría; FindViolations lo comprueba contra el modelo y lo vigila un test de política (CA-122), así un tipo nuevo de
// B, C o D que no se clasifique rompe la suite en vez de sobrevivir a la purga (R-E6).
public sealed class OrganizationDataCatalog
{
    public const string OrganizationIdProperty = "OrganizationId";

    public static OrganizationDataCatalog Default { get; } = new(
        purgeByOrganization:
        [
            // Tenant (TenantEntity)
            typeof(ActivityLog), typeof(CashFlowEntry), typeof(Client), typeof(Collaborator),
            typeof(CollaboratorPayoutInvoice), typeof(Invoice), typeof(Project), typeof(ProjectActivityLog),
            typeof(ProjectBillingEntry), typeof(ProjectDeliverable), typeof(ProjectFile), typeof(ProjectMilestone),
            typeof(ProjectShare), typeof(ProjectTask), typeof(Proposal), typeof(ProposalTemplate), typeof(ServiceCatalog),
            typeof(TimeEntry), typeof(OrganizationInvitation), typeof(OrganizationOwnershipTransfer),
            // Con OrganizationId sin heredar de TenantEntity (C-8)
            typeof(OrganizationMember), typeof(OrganizationSettings), typeof(OrganizationBankAccount),
            typeof(OrganizationIntegration), typeof(OrganizationInvoiceSequence), typeof(ClientContact),
            typeof(ProjectCollaborator), typeof(InvoiceItem), typeof(InvoicePayment), typeof(ProposalItem),
            typeof(ProposalSection), typeof(PilaVerification), typeof(SsCalculationLog), typeof(Notification),
            typeof(SystemEvent), typeof(AuditLog), typeof(AiUsageLog), typeof(AiMonthlyUsage), typeof(AiQuotaOverride),
            typeof(AiGenerationFeedback),
        ],
        purgeByParent:
        [
            new ParentLink(typeof(ProjectFinancials), typeof(Project), nameof(ProjectFinancials.ProjectId)),
            new ParentLink(typeof(PilaVerificationDocument), typeof(PilaVerification),
                nameof(PilaVerificationDocument.PilaVerificationId)),
        ],
        detach:
        [
            typeof(BillingCustomer), typeof(BillingInvoice), typeof(BillingPayment), typeof(BillingCredit),
            typeof(CouponRedemption),
        ],
        owned:
        [
            typeof(User), typeof(CollaboratorAccessToken), typeof(Organization),
        ],
        global:
        [
            // Del usuario
            typeof(RefreshToken), typeof(OAuthAccount), typeof(PasswordResetToken), typeof(EmailVerificationToken),
            typeof(Subscription), typeof(PaymentMethod),
            // Catálogos de master, planes y facturación de Zentory
            typeof(Country), typeof(Currency), typeof(Industry), typeof(TaxType), typeof(ArlRiskLevel),
            typeof(ExpenseCategory), typeof(UnitOfMeasure), typeof(SsRule), typeof(ExchangeRate),
            typeof(IntegrationCatalog), typeof(PaymentGateway), typeof(GatewayRoutingRule), typeof(BillingPlan),
            typeof(PlanLimit), typeof(PlanMarketing), typeof(PlanFeature), typeof(PlanCompareItem), typeof(WebhookEvent),
            typeof(CouponCode),
            // IA de referencia
            typeof(AiProvider), typeof(AiModel), typeof(AiFeature), typeof(AiFeatureConfig), typeof(AiPromptTemplate),
            // Archivo legal: desvinculado de la organización a propósito (RF-79)
            typeof(LegalDocumentArchive),
            // Marca de base local: de la base, no de una organización (RF-63 h, D-42)
            typeof(DatabaseMarker),
        ]);

    public IReadOnlyList<Type>       PurgeByOrganization { get; }
    public IReadOnlyList<ParentLink> PurgeByParent       { get; }
    public IReadOnlyList<Type>       Detach              { get; }
    public IReadOnlyList<Type>       Owned               { get; }
    public IReadOnlyList<Type>       Global              { get; }

    public OrganizationDataCatalog(
        IEnumerable<Type>       purgeByOrganization,
        IEnumerable<ParentLink> purgeByParent,
        IEnumerable<Type>       detach,
        IEnumerable<Type>       owned,
        IEnumerable<Type>       global)
    {
        PurgeByOrganization = purgeByOrganization.ToList();
        PurgeByParent       = purgeByParent.ToList();
        Detach              = detach.ToList();
        Owned               = owned.ToList();
        Global              = global.ToList();
    }

    // Todas las categorías en que figura el tipo, con repeticiones: más de una es un error de clasificación.
    public IReadOnlyList<OrganizationDataCategory> CategoriesOf(Type type)
    {
        var categories = new List<OrganizationDataCategory>();
        categories.AddRange(PurgeByOrganization.Where(t => t == type).Select(_ => OrganizationDataCategory.PurgeByOrganization));
        categories.AddRange(PurgeByParent.Where(l => l.Child == type).Select(_ => OrganizationDataCategory.PurgeByParent));
        categories.AddRange(Detach.Where(t => t == type).Select(_ => OrganizationDataCategory.Detach));
        categories.AddRange(Owned.Where(t => t == type).Select(_ => OrganizationDataCategory.Owned));
        categories.AddRange(Global.Where(t => t == type).Select(_ => OrganizationDataCategory.Global));
        return categories;
    }

    // La categoría única del tipo; lanza si no está clasificado o figura más de una vez (el purgador no adivina).
    public OrganizationDataCategory CategoryOf(Type type)
    {
        var categories = CategoriesOf(type);
        return categories.Count == 1
            ? categories[0]
            : throw new InvalidOperationException(
                $"{type.Name} debe estar exactamente en una categoría de {nameof(OrganizationDataCatalog)} " +
                $"(figura en {categories.Count}).");
    }

    // Problemas de la clasificación frente al modelo. Vacío = cada tipo está en una sola categoría y es coherente.
    public IReadOnlyList<string> FindViolations(IModel model)
    {
        var violations = new List<string>();
        var modelTypes = model.GetEntityTypes().ToDictionary(e => e.ClrType);

        foreach (var (type, entity) in modelTypes)
        {
            var categories = CategoriesOf(type);
            if (categories.Count == 0)
            {
                violations.Add($"{type.Name}: sin clasificar.");
                continue;
            }
            if (categories.Count > 1)
            {
                violations.Add($"{type.Name}: en {categories.Count} categorías ({string.Join(", ", categories)}).");
                continue;
            }

            var organizationId = entity.FindProperty(OrganizationIdProperty);
            switch (categories[0])
            {
                case OrganizationDataCategory.Global when organizationId is not null:
                    violations.Add($"{type.Name}: tiene {OrganizationIdProperty} y figura como global.");
                    break;
                case OrganizationDataCategory.PurgeByOrganization when organizationId is null:
                    violations.Add($"{type.Name}: se purga por {OrganizationIdProperty} pero no lo tiene.");
                    break;
                case OrganizationDataCategory.Detach when organizationId is null || !organizationId.IsNullable:
                    violations.Add($"{type.Name}: se desvincula pero su {OrganizationIdProperty} no es nullable.");
                    break;
                case OrganizationDataCategory.PurgeByParent:
                    violations.AddRange(ParentLinkViolations(PurgeByParent.Single(l => l.Child == type), entity));
                    break;
            }
        }

        foreach (var stale in AllClassified().Where(type => !modelTypes.ContainsKey(type)).Distinct())
            violations.Add($"{stale.Name}: clasificado pero no está en el modelo.");

        return violations;
    }

    private IEnumerable<string> ParentLinkViolations(ParentLink link, IEntityType child)
    {
        if (child.FindProperty(link.ForeignKey) is null)
            yield return $"{link.Child.Name}: no tiene la clave de su padre {link.ForeignKey}.";
        if (CategoriesOf(link.Parent) is not [OrganizationDataCategory.PurgeByOrganization])
            yield return $"{link.Child.Name}: su padre {link.Parent.Name} no se purga por {OrganizationIdProperty}.";
    }

    private IEnumerable<Type> AllClassified()
        => PurgeByOrganization.Concat(PurgeByParent.Select(l => l.Child)).Concat(Detach).Concat(Owned).Concat(Global);
}
