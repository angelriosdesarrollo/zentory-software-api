using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Common;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Billing.Commands;

// seed assign-plan (PLAN-0018 RF-37; ADR-0002 §3 anotado; D-23; SR-3.2, SR-0018.19). Sin pasarela de pago en el MVP es la
// única vía de subida y de bajada del plan, que pertenece al owner: el plan efectivo de sus organizaciones es el de su
// suscripción vigente (ResolveForOwnerAsync, RF-73). Ningún endpoint HTTP lo envía (CA-44): solo la CLI y el builder de
// los tests.
//
// Target es "<host>/<base>" de la conexión, sin usuario ni contraseña: lo arma quien llama (la CLI lo lee con
// NpgsqlConnectionStringBuilder) y queda en la auditoría.
public sealed record AssignPlanBySeedCommand(
    string? Email,
    string? Plan,
    string? Operator,
    string  Target,
    bool    DryRun = false) : IRequest<AssignPlanBySeedResult>;

public enum AssignPlanOutcome
{
    // Validación previa fallida: no se escribió nada (Error trae el motivo).
    Rejected,
    // Ya tenía ese plan (o, para free, ninguna suscripción de pago vigente): no se escribió nada.
    Unchanged,
    // Plan de pago sin suscripción de pago previa: se crea la suscripción.
    Created,
    // Otro plan de pago vigente: se cancela (reassigned_by_seed) y se crea la nueva.
    Replaced,
    // Bajada a free: se cancela la vigente (downgraded_by_seed) sin crear otra.
    Downgraded,
}

public sealed record AssignPlanBySeedResult(
    AssignPlanOutcome Outcome,
    string?           PreviousPlan = null,
    string?           NewPlan      = null,
    bool              DryRun       = false,
    string?           Error        = null,
    Guid?             UserId       = null,
    Guid?             SubscriptionId = null)
{
    public bool Succeeded => Outcome != AssignPlanOutcome.Rejected;

    // Hay escritura (o la habría, en --dry-run).
    public bool Changes => Outcome is AssignPlanOutcome.Created or AssignPlanOutcome.Replaced or AssignPlanOutcome.Downgraded;

    public static AssignPlanBySeedResult Reject(string error) => new(AssignPlanOutcome.Rejected, Error: error);
}

public sealed class AssignPlanBySeedCommandHandler : IRequestHandler<AssignPlanBySeedCommand, AssignPlanBySeedResult>
{
    public const string AuditTable  = "subscriptions";
    public const string AuditAction = "ASSIGNPLAN";

    // Vigencia de la suscripción asignada por seed (ADR-0002 §3): un año, sin renovación automática.
    public const int PeriodYears = 1;

    public static readonly IReadOnlyList<string> AssignablePlans = [Plan.Free, Plan.Pro, Plan.Studio];

    public const string MissingOperator = "--operator is required";
    public const string MissingEmail    = "--email is required";
    public const string UserNotFound    = "user not found";
    public const string NotAnOwner      = "the user is not the owner of any organization";

    public static string InvalidPlan(string? plan)
        => $"invalid plan '{plan}'; expected one of: {string.Join(", ", AssignablePlans)}";

    public static string BillingPlanMissing(string plan)
        => $"billing plan '{plan}' does not exist; run seed reference first";

    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly TimeProvider      _time;

    public AssignPlanBySeedCommandHandler(IZentoryDbContext db, IUnitOfWork uow, TimeProvider time)
    {
        _db   = db;
        _uow  = uow;
        _time = time;
    }

    public async Task<AssignPlanBySeedResult> Handle(AssignPlanBySeedCommand request, CancellationToken ct)
    {
        // 1. Validación previa (RF-37 a): todo antes de escribir; un rechazo no deja filas cambiadas.
        var operatorName = request.Operator?.Trim();
        if (string.IsNullOrEmpty(operatorName)) return AssignPlanBySeedResult.Reject(MissingOperator);

        var plan = request.Plan?.Trim();
        if (plan is null || !AssignablePlans.Contains(plan, StringComparer.Ordinal))
            return AssignPlanBySeedResult.Reject(InvalidPlan(request.Plan));

        if (string.IsNullOrWhiteSpace(request.Email)) return AssignPlanBySeedResult.Reject(MissingEmail);
        var email = EmailAddress.Normalize(request.Email);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return AssignPlanBySeedResult.Reject(UserNotFound);

        // Organizaciones propias vigentes (Organization.OwnerId; el filtro global oculta las eliminadas).
        var ownedOrganizationIds = await _db.Organizations
            .Where(o => o.OwnerId == user.UserId)
            .OrderBy(o => o.Name).ThenBy(o => o.OrganizationId)
            .Select(o => o.OrganizationId)
            .ToListAsync(ct);
        if (ownedOrganizationIds.Count == 0) return AssignPlanBySeedResult.Reject(NotAnOwner);

        var billingPlan = await _db.BillingPlans.FirstOrDefaultAsync(p => p.Name == plan, ct);
        if (billingPlan is null) return AssignPlanBySeedResult.Reject(BillingPlanMissing(plan));

        // 2. Suscripciones vigentes del owner, la más reciente primero (la que resuelve el plan).
        var current = await _db.Subscriptions
            .Where(s => s.UserId == user.UserId
                     && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing))
            .Join(_db.BillingPlans, s => s.PlanId, p => p.Id, (s, p) => new { Subscription = s, PlanName = p.Name })
            .OrderByDescending(r => r.Subscription.CreatedAt)
            .ToListAsync(ct);
        var previousPlan = current.FirstOrDefault()?.PlanName ?? Plan.Free;

        // 3. Decisión (RF-37 c, d, d2).
        List<Subscription> toCancel;
        AssignPlanOutcome  outcome;
        string             reason;
        if (plan == Plan.Free)
        {
            // Bajada: se cancelan las vigentes de pago. Una free vigente (heredada de altas anteriores a DEC-0101) ya es free.
            toCancel = current.Where(r => r.PlanName != Plan.Free).Select(r => r.Subscription).ToList();
            outcome  = toCancel.Count == 0 ? AssignPlanOutcome.Unchanged : AssignPlanOutcome.Downgraded;
            reason   = SubscriptionCancellationReason.DowngradedBySeed;
        }
        else if (current.Count == 1 && current[0].PlanName == plan)
        {
            toCancel = [];
            outcome  = AssignPlanOutcome.Unchanged;
            reason   = string.Empty;
        }
        else
        {
            toCancel = current.Select(r => r.Subscription).ToList();
            outcome  = current.Any(r => r.PlanName != Plan.Free) ? AssignPlanOutcome.Replaced : AssignPlanOutcome.Created;
            reason   = SubscriptionCancellationReason.ReassignedBySeed;
        }

        if (outcome == AssignPlanOutcome.Unchanged || request.DryRun)
            return new AssignPlanBySeedResult(outcome, previousPlan, plan, request.DryRun, UserId: user.UserId);

        // 4. Escritura (D-23): dos SaveChanges en orden dentro de una transacción. El primero cancela, así el UPDATE de
        //    status llega a la base antes del INSERT de la nueva y el índice único filtrado de M3 no choca (EF no
        //    ordena por índices filtrados). El segundo crea la nueva y la auditoría.
        Subscription? created = null;
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            foreach (var subscription in toCancel) subscription.Cancel(reason);
            if (toCancel.Count > 0) await _uow.SaveChangesAsync(token);

            if (plan != Plan.Free)
                created = await CreateSubscriptionAsync(user, ownedOrganizationIds, billingPlan, token);

            _db.AuditLogs.Add(AuditLog.Record(
                tableName: AuditTable,
                recordId:  created?.Id ?? toCancel[0].Id,
                action:    AuditAction,
                userId:    user.UserId,
                oldValues: JsonSerializer.Serialize(new
                {
                    plan          = previousPlan,
                    subscriptions = toCancel.Select(s => s.Id).ToArray(),
                }),
                newValues: JsonSerializer.Serialize(new
                {
                    plan,
                    subscription = created?.Id,
                    reason       = toCancel.Count > 0 ? reason : null,
                    @operator    = operatorName,
                    osUser       = Environment.UserName,
                    machine      = Environment.MachineName,
                    target       = request.Target,
                    user         = user.UserId,
                    email        = user.Email,
                    at           = _time.GetUtcNow().UtcDateTime,
                })));

            await _uow.SaveChangesAsync(token);
        }, ct);

        return new AssignPlanBySeedResult(outcome, previousPlan, plan, DryRun: false, UserId: user.UserId,
            SubscriptionId: created?.Id);
    }

    // Cliente de cobro manual (RF-37 b): reutiliza el de una organización propia con pasarela manual o crea uno en la
    // primera organización propia. La suscripción queda activa por un año (ADR-0002 §3).
    private async Task<Subscription> CreateSubscriptionAsync(
        User user, List<Guid> ownedOrganizationIds, BillingPlan billingPlan, CancellationToken ct)
    {
        var customers = await _db.BillingCustomers
            .Where(c => c.PaymentGateway == PaymentGatewayCodes.Manual
                     && c.OrganizationId != null && ownedOrganizationIds.Contains(c.OrganizationId.Value))
            .ToListAsync(ct);
        var customer = customers.OrderBy(c => ownedOrganizationIds.IndexOf(c.OrganizationId!.Value)).FirstOrDefault();

        if (customer is null)
        {
            var organizationId = ownedOrganizationIds[0];
            var organizationName = await _db.Organizations
                .Where(o => o.OrganizationId == organizationId).Select(o => o.Name).FirstAsync(ct);
            customer = BillingCustomer.Create(
                organizationId, gatewayCustomerId: $"seed_{user.UserId:N}", user.Email, organizationName,
                paymentGateway: PaymentGatewayCodes.Manual);
            _db.BillingCustomers.Add(customer);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var subscription = Subscription.Create(user.UserId, customer.Id, billingPlan.Id);
        subscription.Activate($"seed_{subscription.Id:N}", now, now.AddYears(PeriodYears));
        _db.Subscriptions.Add(subscription);
        return subscription;
    }
}
