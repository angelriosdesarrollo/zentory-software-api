using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Billing.Commands;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Plans;

// PLAN-0018 T24 (RF-37; ADR-0002 §3 anotado; D-23; SR-3.2, SR-0018.19): AssignPlanBySeedCommand sobre el host de pruebas.
// CA-41 (subida, unchanged, reasignación, plan resuelto, token del siguiente login o refresh y auditoría) y CA-42
// (rechazos sin filas cambiadas, --dry-run, bajada a free). Los owners parten de free, sin suscripción: el builder ya
// asigna el plan con este mismo comando.
public sealed class AssignPlanBySeedTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string Operator      = "ops";
    private const string Target        = "db-assign.invalid/zentory_assign";
    private const string OwnerPassword = "Owner-Assign-Plan-2026!";
    private const string PlanClaim     = "plan";

    private readonly ZentoryApiFactory _factory;
    private readonly List<IServiceScope> _scopes = [];

    public AssignPlanBySeedTests(ZentoryApiFactory factory) => _factory = factory;

    public void Dispose() => _scopes.ForEach(scope => scope.Dispose());

    // ── CA-41: subida a studio ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA41_AssignPlan_OwnerFreeAStudio_QuedaUnaSolaSuscripcionVigenteLigadaAlPlanStudio()
    {
        var owner = await SeedFreeOwnerAsync();

        var result = await AssignAsync(owner.Email, Plan.Studio);

        Assert.Equal(AssignPlanOutcome.Created, result.Outcome);
        Assert.Equal(Plan.Free, result.PreviousPlan);
        var current = await CurrentSubscriptionsAsync(owner.UserId);
        var subscription = Assert.Single(current);
        Assert.Equal(await BillingPlanIdAsync(Plan.Studio), subscription.PlanId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(result.SubscriptionId, subscription.Id);
    }

    [Fact]
    public async Task PLAN0018_CA41_AssignPlan_Studio_ResolveForOwnerAsyncDevuelveStudio()
    {
        var owner = await SeedFreeOwnerAsync();

        await AssignAsync(owner.Email, Plan.Studio);

        Assert.Equal(Plan.Studio, await ResolveAsync(owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA41_AssignPlan_Studio_SiguienteLoginYSiguienteRefreshEmitenTokenConPlanStudio()
    {
        var owner = await SeedFreeOwnerAsync();
        using var http = _factory.CreateClient();
        using (var before = await LoginAsync(http, owner.Email, OwnerPassword))
            Assert.Equal(Plan.Free, (await ReadSessionAsync(before)).Claim(PlanClaim));

        await AssignAsync(owner.Email, Plan.Studio);

        using var login = await LoginAsync(http, owner.Email, OwnerPassword);
        var session = await ReadSessionAsync(login);
        Assert.Equal(Plan.Studio, session.Claim(PlanClaim));

        // Refresh conserva la sesión (RF-90) y vuelve a resolver el plan del owner.
        await AssignAsync(owner.Email, Plan.Pro);
        using var refresh = await RefreshAsync(http, session.RefreshToken);
        Assert.Equal(Plan.Pro, (await ReadSessionAsync(refresh)).Claim(PlanClaim));
    }

    [Fact]
    public async Task PLAN0018_CA41_AssignPlan_Studio_ClienteDeCobroManualEnLaOrganizacionPropiaYSeReutiliza()
    {
        var (owner, organizationId) = await SeedFreeOwnerWithOrganizationAsync();

        await AssignAsync(owner.Email, Plan.Studio);
        await AssignAsync(owner.Email, Plan.Pro);

        await using var db = NewContext();
        var customers = await db.BillingCustomers.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId).ToListAsync();
        var customer = Assert.Single(customers);
        Assert.Equal(PaymentGatewayCodes.Manual, customer.PaymentGateway);
        Assert.All(await db.Subscriptions.AsNoTracking().Where(s => s.UserId == owner.UserId).ToListAsync(),
            subscription => Assert.Equal(customer.Id, subscription.CustomerId));
    }

    // ── CA-41: repetición ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA41_AssignPlan_RepetirElMismoPlan_UnchangedSinEscribir()
    {
        var owner = await SeedFreeOwnerAsync();
        await AssignAsync(owner.Email, Plan.Studio);
        var before = await SnapshotAsync(owner.UserId);

        var repeated = await AssignAsync(owner.Email, Plan.Studio);

        Assert.Equal(AssignPlanOutcome.Unchanged, repeated.Outcome);
        Assert.False(repeated.Changes);
        Assert.Equal(before, await SnapshotAsync(owner.UserId));
    }

    // ── CA-41: reasignación ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA41_AssignPlan_ReasignarAPro_CancelaLaAnteriorConReassignedBySeedYDejaUnaSolaVigente()
    {
        var owner = await SeedFreeOwnerAsync();
        var studio = await AssignAsync(owner.Email, Plan.Studio);

        var pro = await AssignAsync(owner.Email, Plan.Pro);

        Assert.Equal(AssignPlanOutcome.Replaced, pro.Outcome);
        Assert.Equal(Plan.Studio, pro.PreviousPlan);
        await using var db = NewContext();
        var previous = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == studio.SubscriptionId);
        Assert.Equal(SubscriptionStatus.Cancelled, previous.Status);
        Assert.Equal(SubscriptionCancellationReason.ReassignedBySeed, previous.CancellationReason);
        var current = Assert.Single(await CurrentSubscriptionsAsync(owner.UserId));
        Assert.Equal(await BillingPlanIdAsync(Plan.Pro), current.PlanId);
        Assert.Equal(Plan.Pro, await ResolveAsync(owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA41_AssignPlan_Reasignacion_DosSaveChangesEnOrdenCancelarYLuegoCrearDentroDeUnaTransaccion()
    {
        // D-23: EF no ordena el UPDATE antes del INSERT por un índice filtrado, así que el comando guarda dos veces.
        var owner = await SeedFreeOwnerAsync();
        await AssignAsync(owner.Email, Plan.Studio);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var uow = new RecordingUnitOfWork(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), db);
        var handler = new AssignPlanBySeedCommandHandler(db, uow, TimeProvider.System);

        var result = await handler.Handle(new AssignPlanBySeedCommand(owner.Email, Plan.Pro, Operator, Target), default);

        Assert.Equal(AssignPlanOutcome.Replaced, result.Outcome);
        Assert.Equal(2, uow.Saves.Count);
        Assert.All(uow.Saves, save => Assert.True(save.InTransaction, "Un SaveChanges quedó fuera de la transacción."));
        Assert.Equal(new[] { (nameof(Subscription), EntityState.Modified) }, uow.Saves[0].Changes);
        Assert.Contains((nameof(Subscription), EntityState.Added), uow.Saves[1].Changes);
        Assert.Contains((nameof(AuditLog), EntityState.Added), uow.Saves[1].Changes);
        Assert.DoesNotContain(uow.Saves[1].Changes, change => change.State == EntityState.Modified);
    }

    [Fact]
    public async Task PLAN0018_CA41_AssignPlan_SubidaYBajadaSimultaneas_LaQuePierdeLaCarreraFallaSinEfectoYNoPisaALaOtra()
    {
        // Riesgo de QA: sin token de concurrencia, la reasignación a pro cancelaría otra vez la fila que la bajada ya
        // canceló y crearía pro, perdiendo la bajada en silencio. Con Status como token, el UPDATE de la que pierde no
        // encuentra la fila en el estado leído y la transacción entera no tiene efecto.
        var owner = await SeedFreeOwnerAsync();
        await AssignAsync(owner.Email, Plan.Studio);
        AssignPlanBySeedResult? competitor = null;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var uow = new RecordingUnitOfWork(scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), db,
            beforeFirstSave: async () => competitor = await AssignAsync(owner.Email, Plan.Free));
        var handler = new AssignPlanBySeedCommandHandler(db, uow, TimeProvider.System);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            handler.Handle(new AssignPlanBySeedCommand(owner.Email, Plan.Pro, Operator, Target), default));

        Assert.Equal(AssignPlanOutcome.Downgraded, competitor!.Outcome);
        Assert.Empty(await CurrentSubscriptionsAsync(owner.UserId));
        Assert.Equal(Plan.Free, await ResolveAsync(owner.UserId));
        await using var check = NewContext();
        Assert.Equal(SubscriptionCancellationReason.DowngradedBySeed,
            (await check.Subscriptions.AsNoTracking().SingleAsync(s => s.UserId == owner.UserId)).CancellationReason);
    }

    [Fact]
    public void PLAN0018_CA41_ModeloDeSubscription_StatusEsTokenDeConcurrencia()
    {
        using var scope = _factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>().Model.FindEntityType(typeof(Subscription))!;

        Assert.True(entity.FindProperty(nameof(Subscription.Status))!.IsConcurrencyToken);
    }

    // ── CA-41: auditoría ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA41_AssignPlan_Auditoria_OperadorUsuarioDelSistemaMaquinaDestinoUsuarioPlanAnteriorPlanNuevoYFecha()
    {
        var owner = await SeedFreeOwnerAsync();
        await AssignAsync(owner.Email, Plan.Studio);
        var startedAt = DateTime.UtcNow;

        await AssignAsync(owner.Email, Plan.Pro);

        var audit = await LastAuditAsync(owner.UserId);
        Assert.Equal(AssignPlanBySeedCommandHandler.AuditTable, audit.TableName);
        Assert.Null(audit.OrganizationId);
        using var oldValues = JsonDocument.Parse(audit.OldValues!);
        using var newValues = JsonDocument.Parse(audit.NewValues!);
        var values = newValues.RootElement;
        Assert.Equal(Plan.Studio, oldValues.RootElement.GetProperty("plan").GetString());
        Assert.Equal(Plan.Pro, values.GetProperty("plan").GetString());
        Assert.Equal(Operator, values.GetProperty("operator").GetString());
        Assert.Equal(Environment.UserName, values.GetProperty("osUser").GetString());
        Assert.Equal(Environment.MachineName, values.GetProperty("machine").GetString());
        Assert.Equal(Target, values.GetProperty("target").GetString());
        Assert.Equal(owner.UserId, values.GetProperty("user").GetGuid());
        Assert.Equal(owner.Email, values.GetProperty("email").GetString());
        Assert.Equal(SubscriptionCancellationReason.ReassignedBySeed, values.GetProperty("reason").GetString());
        Assert.InRange(values.GetProperty("at").GetDateTime(), startedAt.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    // ── CA-42: rechazos sin filas cambiadas ─────────────────────────────────────────────────────────────────────

    public static TheoryData<string?> InvalidPlans => new()
    {
        null, "", "enterprise", Plan.Studio.ToUpperInvariant(), $"{Plan.Pro}-plus",
    };

    [Theory]
    [MemberData(nameof(InvalidPlans))]
    public async Task PLAN0018_CA42_AssignPlan_PlanQueNoEsFreeProNiStudio_RechazaSinCambiarFilas(string? plan)
    {
        var owner = await SeedFreeOwnerAsync();
        await AssignAsync(owner.Email, Plan.Pro);

        await AssertRejectedWithoutChangesAsync(owner.UserId,
            () => AssignAsync(owner.Email, plan), AssignPlanBySeedCommandHandler.InvalidPlan(plan));
    }

    [Fact]
    public async Task PLAN0018_CA42_AssignPlan_UsuarioInexistente_RechazaSinCambiarFilas()
    {
        var owner = await SeedFreeOwnerAsync();

        await AssertRejectedWithoutChangesAsync(owner.UserId,
            () => AssignAsync($"nadie-{Guid.NewGuid():N}@zentory.test", Plan.Studio),
            AssignPlanBySeedCommandHandler.UserNotFound);
    }

    [Theory]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA42_AssignPlan_UsuarioQueNoEsOwnerDeNingunaOrganizacion_RechazaSinCambiarFilas(string role)
    {
        var tenants = await new TenantBuilder(_factory).ForA(a => a.WithPlan(Plan.Free).WithMember(role)).BuildAsync();
        var member = tenants.A.Member(role);

        await AssertRejectedWithoutChangesAsync(member.UserId,
            () => AssignAsync(member.Email, Plan.Studio), AssignPlanBySeedCommandHandler.NotAnOwner);
    }

    [Fact]
    public async Task PLAN0018_CA42_AssignPlan_OwnerCuyaUnicaOrganizacionEstaEliminada_RechazaComoNoOwner()
    {
        var (owner, organizationId) = await SeedFreeOwnerWithOrganizationAsync();
        await using (var db = NewContext())
        {
            var organization = await db.Organizations.SingleAsync(o => o.OrganizationId == organizationId);
            organization.MarkDeleted(owner.UserId, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        await AssertRejectedWithoutChangesAsync(owner.UserId,
            () => AssignAsync(owner.Email, Plan.Studio), AssignPlanBySeedCommandHandler.NotAnOwner);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PLAN0018_CA42_AssignPlan_SinOperador_RechazaSinCambiarFilas(string? operatorName)
    {
        var owner = await SeedFreeOwnerAsync();

        await AssertRejectedWithoutChangesAsync(owner.UserId,
            () => SendAsync(new AssignPlanBySeedCommand(owner.Email, Plan.Studio, operatorName, Target)),
            AssignPlanBySeedCommandHandler.MissingOperator);
    }

    // ── CA-42: --dry-run ────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PLAN0018_CA42_AssignPlan_DryRun_InformaLoQueHariaSinEscribir(bool fromPaidPlan)
    {
        var owner = await SeedFreeOwnerAsync();
        if (fromPaidPlan) await AssignAsync(owner.Email, Plan.Pro);
        var before = await SnapshotAsync(owner.UserId);

        var result = await SendAsync(new AssignPlanBySeedCommand(owner.Email, Plan.Studio, Operator, Target, DryRun: true));

        Assert.True(result.DryRun);
        Assert.Equal(fromPaidPlan ? AssignPlanOutcome.Replaced : AssignPlanOutcome.Created, result.Outcome);
        Assert.Equal(fromPaidPlan ? Plan.Pro : Plan.Free, result.PreviousPlan);
        Assert.Equal(Plan.Studio, result.NewPlan);
        Assert.Equal(before, await SnapshotAsync(owner.UserId));
    }

    // ── CA-42: bajada a free ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA42_AssignPlan_FreeSobreOwnerPro_CancelaConDowngradedBySeedSinCrearOtraYPlanResueltoFree()
    {
        var owner = await SeedFreeOwnerAsync();
        var pro = await AssignAsync(owner.Email, Plan.Pro);
        int subscriptionsBefore;
        await using (var db = NewContext())
            subscriptionsBefore = await db.Subscriptions.CountAsync(s => s.UserId == owner.UserId);

        var free = await AssignAsync(owner.Email, Plan.Free);

        Assert.Equal(AssignPlanOutcome.Downgraded, free.Outcome);
        Assert.Equal(Plan.Pro, free.PreviousPlan);
        await using var check = NewContext();
        var cancelled = await check.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == pro.SubscriptionId);
        Assert.Equal(SubscriptionStatus.Cancelled, cancelled.Status);
        Assert.Equal(SubscriptionCancellationReason.DowngradedBySeed, cancelled.CancellationReason);
        Assert.Equal(subscriptionsBefore, await check.Subscriptions.CountAsync(s => s.UserId == owner.UserId));
        Assert.Empty(await CurrentSubscriptionsAsync(owner.UserId));
        Assert.Equal(Plan.Free, await ResolveAsync(owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA42_AssignPlan_FreeSobreOwnerPro_ElConteoDeFilasDeTenantNoCambia()
    {
        // RF-75 b: la bajada no borra nada. Solo cambia el estado de la suscripción y suma la auditoría.
        var tenants = await new TenantBuilder(_factory).ForA(a => a.WithPlan(Plan.Pro).WithMember(OrgRole.Member)).BuildAsync();
        Dictionary<string, int> before;
        await using (var db = NewContext()) before = await TableRowCounts.CountAsync(db);

        var result = await AssignAsync(tenants.A.Owner.Email, Plan.Free);

        Assert.Equal(AssignPlanOutcome.Downgraded, result.Outcome);
        await using var check = NewContext();
        Assert.Equal(new[] { nameof(AuditLog) }, TableRowCounts.Changed(before, await TableRowCounts.CountAsync(check)));
    }

    [Fact]
    public async Task PLAN0018_CA42_AssignPlan_FreeSobreOwnerSinSuscripcion_UnchangedSinEscribir()
    {
        var owner = await SeedFreeOwnerAsync();
        var before = await SnapshotAsync(owner.UserId);

        var result = await AssignAsync(owner.Email, Plan.Free);

        Assert.Equal(AssignPlanOutcome.Unchanged, result.Outcome);
        Assert.Equal(before, await SnapshotAsync(owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA42_AssignPlan_FreeSobreOwnerConSuscripcionFreeHeredadaDeAltasAnteriores_UnchangedSinEscribir()
    {
        // Antes de DEC-0101 el alta dejaba una suscripción free en trialing; una base existente puede tenerla: ya es free.
        var (owner, organizationId) = await SeedFreeOwnerWithOrganizationAsync();
        await using (var db = NewContext())
        {
            var customer = BillingCustomer.Create(organizationId, $"local_{owner.UserId}", owner.Email, "alta");
            db.BillingCustomers.Add(customer);
            db.Subscriptions.Add(Subscription.Create(owner.UserId, customer.Id, await BillingPlanIdAsync(Plan.Free)));
            await db.SaveChangesAsync();
        }
        var before = await SnapshotAsync(owner.UserId);

        var result = await AssignAsync(owner.Email, Plan.Free);

        Assert.Equal(AssignPlanOutcome.Unchanged, result.Outcome);
        Assert.Equal(before, await SnapshotAsync(owner.UserId));

        // La subida cancela esa free heredada y deja una sola vigente.
        var studio = await AssignAsync(owner.Email, Plan.Studio);
        Assert.Equal(AssignPlanOutcome.Created, studio.Outcome);
        Assert.Equal(studio.SubscriptionId, Assert.Single(await CurrentSubscriptionsAsync(owner.UserId)).Id);
    }

    [Fact]
    public async Task PLAN0018_CA42_AssignPlan_EmailConMayusculasYEspacios_SeNormalizaYEncuentraAlOwner()
    {
        var owner = await SeedFreeOwnerAsync();

        var result = await AssignAsync($"  {owner.Email.ToUpperInvariant()} ", Plan.Studio);

        Assert.Equal(AssignPlanOutcome.Created, result.Outcome);
        Assert.Equal(owner.UserId, result.UserId);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<User> SeedFreeOwnerAsync() => (await SeedFreeOwnerWithOrganizationAsync()).Owner;

    private async Task<(User Owner, Guid OrganizationId)> SeedFreeOwnerWithOrganizationAsync()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithPlan(Plan.Free).WithOwnerPassword(OwnerPassword))
            .BuildAsync();

        // Los planes de pago que estos tests asignan existen en el catálogo, como tras seed reference.
        await using var db = NewContext();
        await TenantBuilder.EnsureBillingPlanAsync(db, Plan.Pro);
        await TenantBuilder.EnsureBillingPlanAsync(db, Plan.Studio);
        await db.SaveChangesAsync();

        return (tenants.A.Owner, tenants.A.OrganizationId);
    }

    private Task<AssignPlanBySeedResult> AssignAsync(string email, string? plan)
        => SendAsync(new AssignPlanBySeedCommand(email, plan, Operator, Target));

    private async Task<AssignPlanBySeedResult> SendAsync(AssignPlanBySeedCommand command)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command);
    }

    private async Task<string> ResolveAsync(Guid ownerId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPlanResolutionService>().ResolveForOwnerAsync(ownerId);
    }

    // Contexto de un scope propio (sin HttpContext, como la CLI); los scopes se liberan al terminar el test.
    private ZentoryDbContext NewContext()
    {
        var scope = _factory.Services.CreateScope();
        _scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
    }

    private async Task<List<Subscription>> CurrentSubscriptionsAsync(Guid userId)
    {
        await using var db = NewContext();
        return await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId && SubscriptionStatus.Current.Contains(s.Status))
            .ToListAsync();
    }

    private async Task<Guid> BillingPlanIdAsync(string plan)
    {
        await using var db = NewContext();
        return await db.BillingPlans.Where(p => p.Name == plan).Select(p => p.Id).FirstAsync();
    }

    private async Task<AuditLog> LastAuditAsync(Guid userId)
    {
        await using var db = NewContext();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == userId && a.Action == AssignPlanBySeedCommandHandler.AuditAction)
            .OrderByDescending(a => a.CreatedAt)
            .FirstAsync();
    }

    // Foto de lo que el comando podría escribir: filas de todo el modelo y estado de cada suscripción del usuario.
    private async Task<string> SnapshotAsync(Guid userId)
    {
        await using var db = NewContext();
        var counts = await TableRowCounts.CountAsync(db);
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Status, s.CancellationReason, s.PlanId, s.UpdatedAt })
            .ToListAsync();
        return JsonSerializer.Serialize(new { counts, subscriptions });
    }

    private async Task AssertRejectedWithoutChangesAsync(
        Guid userId, Func<Task<AssignPlanBySeedResult>> run, string expectedError)
    {
        var before = await SnapshotAsync(userId);

        var result = await run();

        Assert.Equal(AssignPlanOutcome.Rejected, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.Error);
        Assert.Equal(before, await SnapshotAsync(userId));
    }

    // IUnitOfWork que registra cada SaveChanges: si corrió dentro de ExecuteInTransactionAsync y qué entradas guardó.
    // Con beforeFirstSave, ejecuta una operación competidora completa justo antes del primer SaveChanges (carrera
    // determinista: la operación detenida sigue con lo que leyó).
    private sealed class RecordingUnitOfWork(IUnitOfWork inner, ZentoryDbContext db, Func<Task>? beforeFirstSave = null)
        : IUnitOfWork
    {
        private bool _inTransaction;

        public List<RecordedSave> Saves { get; } = [];

        public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            if (Saves.Count == 0 && beforeFirstSave is not null) await beforeFirstSave();
            Saves.Add(new RecordedSave(_inTransaction, db.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(e => (e.Metadata.ClrType.Name, e.State))
                .ToList()));
            return await inner.SaveChangesAsync(ct);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
            => inner.ExecuteInTransactionAsync(async token =>
            {
                _inTransaction = true;
                try { await operation(token); }
                finally { _inTransaction = false; }
            }, ct);
    }

    private sealed record RecordedSave(bool InTransaction, List<(string Entity, EntityState State)> Changes);
}
