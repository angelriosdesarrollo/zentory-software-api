using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Security;
using Zentory.Domain.Common;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Entities.Common;

namespace Zentory.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// Orquestador de los escenarios demo (PLAN-0018 RF-56, D-5). Corre después de <see cref="ReferenceDataSeeder"/>: los
/// escenarios cuelgan de los planes de facturación. El orden importa: la Agencia crea a la persona principal.
/// </summary>
public sealed class DemoScenarioRunner
{
    // Los seis de §3.0.A. La Consultora va después de la Agencia: suma a su owner (la persona Free) como member allí.
    public static IReadOnlyList<IDemoScenario> DefaultScenarios { get; } =
    [
        new AgencyScenario(), new FreelanceBrandScenario(), new ConstructionScenario(),
        new ConsultancyScenario(), new FreelanceOtherScenario(), new FreelanceNewScenario(),
    ];

    private readonly IReadOnlyList<IDemoScenario> _scenarios;

    public DemoScenarioRunner(IReadOnlyList<IDemoScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        _scenarios = scenarios;
    }

    /// <param name="demoPassword">Ya resuelta por <see cref="DemoPasswordResolver"/>; solo se usa para el hash.</param>
    public async Task RunAsync(ZentoryDbContext db, TimeProvider timeProvider, string demoPassword, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(timeProvider);

        // Segunda barrera antes de escribir (RF-57): quien llame sin pasar por el resolver tampoco siembra una
        // contraseña fuera de política. El mensaje nombra la clave, nunca el valor.
        if (!PasswordPolicy.IsSatisfiedBy(demoPassword))
            throw new InvalidOperationException(
                $"{DemoDataSettings.PasswordKey} does not meet the password policy: it must be {PasswordPolicy.Requirement}.");

        // Un solo hash para todas las personas de la corrida: BCrypt es lento a propósito.
        var context = new DemoSeedContext(db, timeProvider.GetUtcNow(), BCrypt.Net.BCrypt.HashPassword(demoPassword));

        foreach (var scenario in _scenarios)
            await scenario.SeedAsync(context, ct);
    }
}

/// <summary>
/// Lo que comparten los escenarios de una corrida: la base, el día de referencia (UTC, del <see cref="TimeProvider"/>)
/// y las inserciones idempotentes. Ningún método modifica una fila que ya existe.
/// </summary>
public sealed class DemoSeedContext
{
    public DemoSeedContext(ZentoryDbContext db, DateTimeOffset now, string passwordHash)
    {
        Db           = db;
        Today        = DateOnly.FromDateTime(now.UtcDateTime);
        PasswordHash = passwordHash;
    }

    public ZentoryDbContext Db { get; }

    public DateOnly Today { get; }

    private string PasswordHash { get; }

    // ── Fechas relativas al día de referencia ─────────────────────────────────────────────────────────────────────

    public DateOnly Day(int offset) => Today.AddDays(offset);

    // Siempre Kind = Utc: Npgsql rechaza Unspecified en timestamptz y no se usa EnableLegacyTimestampBehavior (R-8).
    public DateTime UtcDay(int offset, int hour = 0, int minute = 0)
        => Day(offset).ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);

    // ── Valores que el dominio no expone en sus fábricas ──────────────────────────────────────────────────────────

    // Fija el ID antes de rastrear la entidad, para que las hijas puedan apuntar a ella al construirse.
    public T WithId<T>(T entity, Guid id) where T : BaseEntity
    {
        Db.Entry(entity).Property(e => e.Id).CurrentValue = id;
        return entity;
    }

    // Fechas que las fábricas toman del reloj del sistema (emisión, envío, ingreso): se llevan al día de referencia.
    public T Set<T, TValue>(T entity, Expression<Func<T, TValue>> property, TValue value) where T : class
    {
        Db.Entry(entity).Property(property).CurrentValue = value;
        return entity;
    }

    // ── Inserciones idempotentes ──────────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<T>> AddMissingAsync<T>(IEnumerable<T> entities, CancellationToken ct)
        where T : BaseEntity
    {
        var candidates = entities.ToList();
        var existing   = await ExistingIdsAsync<T>(candidates.Select(e => e.Id), ct);
        var missing    = candidates.Where(e => !existing.Contains(e.Id)).ToList();
        if (missing.Count == 0) return missing;

        Db.Set<T>().AddRange(missing);
        await Db.SaveChangesAsync(ct);
        return missing;
    }

    public async Task<HashSet<Guid>> ExistingIdsAsync<T>(IEnumerable<Guid> ids, CancellationToken ct) where T : BaseEntity
    {
        var wanted = ids.Distinct().ToList();
        var found  = await Db.Set<T>().IgnoreQueryFilters().Where(e => wanted.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
        return [.. found];
    }

    // El número de factura es único por organización: una fila previa con el mismo número (otro ID) gana.
    public async Task AddMissingInvoicesAsync(Guid organizationId, IEnumerable<Invoice> invoices, CancellationToken ct)
    {
        var candidates = invoices.ToList();
        var numbers    = candidates.Select(i => i.InvoiceNumber).ToList();
        var taken      = await Db.Invoices.IgnoreQueryFilters()
            .Where(i => i.OrganizationId == organizationId && numbers.Contains(i.InvoiceNumber))
            .Select(i => i.InvoiceNumber)
            .ToListAsync(ct);

        await AddMissingAsync(candidates.Where(i => !taken.Contains(i.InvoiceNumber)), ct);
    }

    // Filas que cuelgan de una factura: solo si su factura existe con el ID fijo (si no, la FK fallaría en PostgreSQL).
    public async Task AddMissingForInvoicesAsync<T>(IEnumerable<T> entities, Func<T, Guid?> invoiceId, CancellationToken ct)
        where T : BaseEntity
    {
        var candidates = entities.ToList();
        var invoices   = await ExistingIdsAsync<Invoice>(candidates.Select(invoiceId).OfType<Guid>(), ct);
        await AddMissingAsync(candidates.Where(e => invoiceId(e) is not { } id || invoices.Contains(id)), ct);
    }

    public async Task EnsureUserAsync(DemoPerson person, string globalRole, CancellationToken ct)
    {
        var email  = EmailAddress.Normalize(person.Email);
        var exists = await Db.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.UserId == person.Id || u.Email == email, ct);
        if (exists) return;

        // Las personas demo quedan verificadas (PLAN-0018 RF-20, CA-68), con la fecha llevada al día de referencia.
        var user = User.Create(email, person.FirstName, person.LastName, globalRole, PasswordHash, id: person.Id);
        user.MarkEmailVerified();
        Set(user, u => u.EmailVerifiedAt, UtcDay(-365, 9));
        Db.Users.Add(user);
        await Db.SaveChangesAsync(ct);
    }

    // Devuelve false si la organización ya existía: sus filas no se tocan (ni el owner ni el legalType).
    public async Task<bool> EnsureOrganizationAsync(Guid id, string name, string legalType, Guid ownerId, CancellationToken ct)
    {
        if (await Db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.OrganizationId == id, ct)) return false;

        var organization = Organization.Create(name, legalType, id: id);
        organization.SetOwner(ownerId);
        Db.Organizations.Add(organization);
        await Db.SaveChangesAsync(ct);
        return true;
    }

    // Clave natural (organización, usuario), incluidas las membresías revocadas: una revocación no se deshace.
    public async Task EnsureMembershipAsync(Guid organizationId, Guid userId, string role, int joinedOffsetDays, CancellationToken ct)
    {
        var exists = await Db.OrganizationMembers.IgnoreQueryFilters()
            .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct);
        if (exists) return;

        var member = OrganizationMember.Create(organizationId, userId, role);
        WithId(member, DemoIds.For(organizationId, $"member:{userId:N}"));
        Set(member, m => m.JoinedAt, UtcDay(joinedOffsetDays, 9));
        Db.OrganizationMembers.Add(member);
        await Db.SaveChangesAsync(ct);
    }

    public async Task EnsureSettingsAsync(Guid organizationId, IReadOnlyDictionary<string, string> settings, CancellationToken ct)
    {
        var keys  = settings.Keys.ToList();
        var taken = await Db.OrganizationSettings
            .Where(s => s.OrganizationId == organizationId && keys.Contains(s.Key))
            .Select(s => s.Key)
            .ToListAsync(ct);

        var missing = settings.Where(s => !taken.Contains(s.Key)).ToList();
        if (missing.Count == 0) return;

        Db.OrganizationSettings.AddRange(missing.Select(s => OrganizationSettings.Set(organizationId, s.Key, s.Value)));
        await Db.SaveChangesAsync(ct);
    }

    public async Task EnsureIntegrationAsync(Guid organizationId, string integrationId, Guid connectedBy, string connectedAs, CancellationToken ct)
    {
        var exists = await Db.OrganizationIntegrations.IgnoreQueryFilters()
            .AnyAsync(i => i.OrganizationId == organizationId && i.IntegrationId == integrationId, ct);
        if (exists) return;

        var integration = OrganizationIntegration.Create(organizationId, integrationId);
        WithId(integration, DemoIds.For(organizationId, $"integration:{integrationId}"));
        integration.Connect(connectedBy, connectedAs: connectedAs);
        Set(integration, i => i.ConnectedAt, UtcDay(-90, 10));
        Db.OrganizationIntegrations.Add(integration);
        await Db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Suscripción activa del owner (RF-56 e): el plan lo resuelve el owner, no la organización. Pasarela
    /// <c>manual</c>, periodo en curso alrededor del día de referencia y los tres últimos cobros pagados. Se omite si
    /// el owner ya tiene una suscripción (índice único por usuario) o la organización ya tiene cliente de facturación.
    /// </summary>
    public async Task EnsureActiveSubscriptionAsync(
        DemoPerson owner, Guid organizationId, string organizationName, string planName, CancellationToken ct)
    {
        if (await Db.Subscriptions.AnyAsync(s => s.UserId == owner.Id, ct)) return;
        if (await Db.BillingCustomers.AnyAsync(c => c.OrganizationId == organizationId, ct)) return;

        var plan = await Db.BillingPlans.FirstAsync(p => p.Name == planName, ct);

        var customer = BillingCustomer.Create(
            organizationId, $"manual_demo_{owner.Id:N}", owner.Email, organizationName, paymentGateway: ManualGateway);
        WithId(customer, DemoIds.For(organizationId, "billing:customer"));
        Db.BillingCustomers.Add(customer);
        await Db.SaveChangesAsync(ct);

        var subscription = Subscription.Create(owner.Id, customer.Id, plan.Id);
        WithId(subscription, DemoIds.For(organizationId, "billing:subscription"));
        subscription.Activate($"manual_demo_{owner.Id:N}_{planName}", periodStart: UtcDay(-10), periodEnd: UtcDay(20));
        Db.Subscriptions.Add(subscription);
        await Db.SaveChangesAsync(ct);

        var invoices = Enumerable.Range(0, 3).Select(month =>
        {
            var offset  = -10 - (30 * month);
            var invoice = BillingInvoice.Create(organizationId, customer.Id, plan.PriceMonthlyUsd, "USD", subscription.Id);
            WithId(invoice, DemoIds.For(organizationId, $"billing:invoice:{month}"));
            invoice.SetInvoiceNumber($"ZEN-{organizationId.ToString("N")[^4..].ToUpperInvariant()}-{3 - month:D2}");
            invoice.MarkPaid(UtcDay(offset));
            return invoice;
        });
        await AddMissingAsync(invoices, ct);
    }

    /// <summary>
    /// Owner que bajó de plan (RF-56 e, RF-75; decisión 6): tuvo una suscripción <paramref name="previousPlanName"/>
    /// por <c>manual</c>, pagó sus cobros y hoy está cancelada con <see cref="DowngradedBySeedReason"/>, sin otra
    /// activa, así que su plan efectivo es free. Mismas guardas que <see cref="EnsureActiveSubscriptionAsync"/>.
    /// </summary>
    public async Task EnsureDowngradedSubscriptionAsync(
        DemoPerson owner, Guid organizationId, string organizationName, string previousPlanName, int cancelledOffset,
        CancellationToken ct)
    {
        if (await Db.Subscriptions.AnyAsync(s => s.UserId == owner.Id, ct)) return;
        if (await Db.BillingCustomers.AnyAsync(c => c.OrganizationId == organizationId, ct)) return;

        var plan = await Db.BillingPlans.FirstAsync(p => p.Name == previousPlanName, ct);

        var customer = BillingCustomer.Create(
            organizationId, $"manual_demo_{owner.Id:N}", owner.Email, organizationName, paymentGateway: ManualGateway);
        WithId(customer, DemoIds.For(organizationId, "billing:customer"));
        Db.BillingCustomers.Add(customer);
        await Db.SaveChangesAsync(ct);

        // Un año en el plan de pago hasta el último periodo, que terminó el día de la bajada.
        var subscription = Subscription.Create(owner.Id, customer.Id, plan.Id);
        WithId(subscription, DemoIds.For(organizationId, "billing:subscription"));
        subscription.Activate($"manual_demo_{owner.Id:N}_{previousPlanName}",
            periodStart: UtcDay(cancelledOffset - 30), periodEnd: UtcDay(cancelledOffset));
        subscription.Cancel(DowngradedBySeedReason);
        Set(subscription, s => s.CreatedAt, UtcDay(cancelledOffset - 365, 9));
        Set(subscription, s => s.CancelledAt, UtcDay(cancelledOffset, 9));
        Set(subscription, s => s.UpdatedAt, UtcDay(cancelledOffset, 9));
        Db.Subscriptions.Add(subscription);
        await Db.SaveChangesAsync(ct);

        var invoices = Enumerable.Range(0, 3).Select(month =>
        {
            var offset  = cancelledOffset - 30 - (30 * month);
            var invoice = BillingInvoice.Create(organizationId, customer.Id, plan.PriceMonthlyUsd, "USD", subscription.Id);
            WithId(invoice, DemoIds.For(organizationId, $"billing:invoice:{month}"));
            invoice.SetInvoiceNumber($"ZEN-{organizationId.ToString("N")[^4..].ToUpperInvariant()}-{3 - month:D2}");
            invoice.MarkPaid(UtcDay(offset));
            return invoice;
        });
        await AddMissingAsync(invoices, ct);
    }

    // El mismo motivo que deja `seed assign-plan --plan free` (RF-37 d2).
    public const string DowngradedBySeedReason = "downgraded_by_seed";

    private const string ManualGateway = "manual";
}
