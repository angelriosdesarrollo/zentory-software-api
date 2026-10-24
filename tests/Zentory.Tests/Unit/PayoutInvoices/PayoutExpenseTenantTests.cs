using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.PayoutInvoices.Commands;
using Zentory.Application.Projects;
using Zentory.Application.Projects.Queries;
using Zentory.Application.Stats.Queries;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Repositories;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.PayoutInvoices;

// R-2 (RF-5 vía ReviewPayoutInvoiceCommand, RF-10): el gasto que genera aprobar una cuenta de
// cobro queda etiquetado con la organización del token, así que ni /expenses ni la rentabilidad
// de otra organización lo ven. Proyectos, horas y facturas viven en un ZentoryDbContext
// InMemory compartido por las dos organizaciones, como en producción.
public sealed class PayoutExpenseTenantTests : IDisposable
{
    private const decimal FixedAmount = 777m;
    private const decimal HourlyRate  = 50m;
    private const decimal Hours       = 6m;

    private readonly ZentoryDbContext    _db    = TestDbContextFactory.Create();
    private readonly ProjectExpenseStore _store = new();

    private readonly FakeCollaboratorRepository              _collaborators = new();
    private readonly FakeCollaboratorPayoutInvoiceRepository _invoices      = new();
    private readonly FakeProjectRepository                   _projects      = new();

    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();

    private readonly Project _projectA;
    private readonly Project _projectB;

    public PayoutExpenseTenantTests()
    {
        var clientA = Client.Create(_orgA, "Cliente A", "Contacto A");
        var clientB = Client.Create(_orgB, "Cliente B", "Contacto B");
        _projectA = Project.Create(_orgA, clientA.Id, "Proyecto A", BillingType.Hourly, 1000m);
        _projectB = Project.Create(_orgB, clientB.Id, "Proyecto B", BillingType.Hourly, 1000m);

        _db.Clients.AddRange(clientA, clientB);
        _db.Projects.AddRange(_projectA, _projectB);
        _db.SaveChanges();
        _projects.Items.AddRange([_projectA, _projectB]);
    }

    public void Dispose() => _db.Dispose();

    private CollaboratorPayoutInvoice SeedInvoiceOfOrgA(string collaboratorType, decimal amount, string period = "2026-09")
    {
        var collaborator = Collaborator.Create(_orgA, "Colaborador A", collaboratorType);
        _collaborators.Items.Add(collaborator);

        var invoice = CollaboratorPayoutInvoice.Create(_orgA, collaborator.Id, period, "Servicios", amount, "USD");
        invoice.MarkGenerated("payouts/test.pdf");
        _invoices.Items.Add(invoice);
        return invoice;
    }

    private Task ApproveAs(Guid organizationId, Guid invoiceId, Guid? projectId)
        => new ReviewPayoutInvoiceCommandHandler(
                _invoices, _collaborators, new FakeOrganizationRepository(), new UnusedEmailService(),
                new FixedApplicationSettings(), new FakeUnitOfWork(_db), FakeTenantContext.For(organizationId),
                _store, new InMemoryTimeEntryRepository(_db))
            .Handle(new ReviewPayoutInvoiceCommand(invoiceId, Approved: true, ProjectId: projectId), CancellationToken.None);

    private Task<IReadOnlyList<ProjectExpenseDto>> GetAllExpensesAs(Guid organizationId)
        => new GetAllExpensesQueryHandler(_store, _projects, FakeTenantContext.For(organizationId))
            .Handle(new GetAllExpensesQuery(), CancellationToken.None);

    private Task<ProfitabilityStatsDto> GetProfitabilityAs(Guid organizationId)
        => new GetProfitabilityStatsQueryHandler(_db, FakeTenantContext.For(organizationId), _store)
            .Handle(new GetProfitabilityStatsQuery(), CancellationToken.None);

    // ── CA-7: fixed_contractor, el gasto se imputa al ProjectId del request ──

    [Fact]
    public async Task CA7_ApprovedPayoutOfOrgA_GeneratedExpenseAppearsOnlyInOrgAExpenses()
    {
        var invoice = SeedInvoiceOfOrgA("fixed_contractor", FixedAmount);

        await ApproveAs(_orgA, invoice.Id, _projectA.Id);

        var ofA = await GetAllExpensesAs(_orgA);
        var ofB = await GetAllExpensesAs(_orgB);

        var generated = Assert.Single(ofA, e => e.SourcePayoutInvoiceId == invoice.Id);
        Assert.Equal(FixedAmount, generated.Amount);
        Assert.Equal(_projectA.Id, generated.ProjectId);
        Assert.DoesNotContain(ofB, e => e.SourcePayoutInvoiceId == invoice.Id);
        Assert.Empty(ofB);
    }

    [Fact]
    public async Task CA7_ApprovedPayoutOfOrgA_ProfitabilityOfOrgB_DoesNotIncludeItsAmountInAnyProject()
    {
        var invoice = SeedInvoiceOfOrgA("fixed_contractor", FixedAmount);

        await ApproveAs(_orgA, invoice.Id, _projectA.Id);

        var statsB = await GetProfitabilityAs(_orgB);

        var projectB = Assert.Single(statsB.Projects);
        Assert.Equal(_projectB.Id.ToString(), projectB.Id);
        Assert.All(statsB.Projects, p => Assert.Equal(0m, p.CostHours));
        Assert.DoesNotContain(statsB.Projects, p => p.Id == _projectA.Id.ToString());
    }

    // Regresión de RF-10: fixed_contractor no valida request.ProjectId (R-4, diferido a PLAN-0008),
    // así que A puede dejar su gasto colgado de un proyecto de B. La rentabilidad de B recorre ese
    // proyecto y solo el filtro por organización del store evita que sume el monto de A.
    [Fact]
    public async Task CA7_ApprovedPayoutOfOrgA_ImputedToProjectOfOrgB_ProfitabilityOfOrgB_KeepsCostHoursAtZero()
    {
        var invoice = SeedInvoiceOfOrgA("fixed_contractor", FixedAmount);

        await ApproveAs(_orgA, invoice.Id, _projectB.Id);

        var generated = Assert.Single(await GetAllExpensesAs(_orgA), e => e.SourcePayoutInvoiceId == invoice.Id);
        Assert.Equal(_projectB.Id, generated.ProjectId);

        var statsB = await GetProfitabilityAs(_orgB);

        var projectB = Assert.Single(statsB.Projects);
        Assert.Equal(_projectB.Id.ToString(), projectB.Id);
        Assert.Equal(0m, projectB.CostHours);
    }

    [Fact]
    public async Task CA7_ApprovedPayoutOfOrgA_ProfitabilityOfOrgA_IncludesItsAmountInTheProject()
    {
        var invoice = SeedInvoiceOfOrgA("fixed_contractor", FixedAmount);

        await ApproveAs(_orgA, invoice.Id, _projectA.Id);

        var statsA = await GetProfitabilityAs(_orgA);

        var projectA = Assert.Single(statsA.Projects);
        Assert.Equal(_projectA.Id.ToString(), projectA.Id);
        Assert.Equal(FixedAmount, projectA.CostHours);
    }

    // ── CA-7: hourly_contractor, el reparto sale de los TimeEntry aprobados ──

    [Fact]
    public async Task CA7_ApprovedHourlyPayoutOfOrgA_ExpenseFromTimeEntriesIsInvisibleToOrgB()
    {
        var invoice = SeedInvoiceOfOrgA("hourly_contractor", Hours * HourlyRate);
        var entry = TimeEntry.Create(_orgA, _projectA.Id, new DateOnly(2026, 9, 15), Hours, HourlyRate,
            currency: "USD", collaboratorId: invoice.CollaboratorId);
        entry.Approve();
        _db.TimeEntries.Add(entry);
        await _db.SaveChangesAsync();

        await ApproveAs(_orgA, invoice.Id, projectId: null);

        var generated = Assert.Single(await GetAllExpensesAs(_orgA), e => e.SourcePayoutInvoiceId == invoice.Id);
        Assert.Equal(_projectA.Id, generated.ProjectId);
        Assert.Empty(await GetAllExpensesAs(_orgB));

        var statsB = await GetProfitabilityAs(_orgB);
        Assert.All(statsB.Projects, p => Assert.Equal(0m, p.CostHours));

        // Las horas quedaron 'billed': en A solo cuenta el costo real de la cuenta de cobro.
        var statsA = await GetProfitabilityAs(_orgA);
        Assert.Equal(Hours * HourlyRate, Assert.Single(statsA.Projects).CostHours);
    }

    // ── CA-T5-1: trazabilidad de la spec en la suite (RNF-6) ─────────────────

    [Theory]
    [InlineData("CA1_")]
    [InlineData("CA2_")]
    [InlineData("CA3_")]
    [InlineData("CA4_")]
    [InlineData("CA5_")]
    [InlineData("CA6_")]
    [InlineData("CA7_")]
    public void CAT5_1_Suite_ContainsAtLeastOneTestPerSpecCriterion(string prefix)
    {
        var testNames = typeof(PayoutExpenseTenantTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(m => m.IsDefined(typeof(FactAttribute), inherit: true))
            .Select(m => m.Name);

        Assert.Contains(testNames, name => name.StartsWith(prefix, StringComparison.Ordinal));
    }

    // ── Dobles locales ───────────────────────────────────────────────────────

    // Delega en el TimeEntryRepository real salvo BatchMarkBilledAsync: ExecuteUpdateAsync no
    // existe en EF InMemory, así que se marca cada entidad y se guarda.
    private sealed class InMemoryTimeEntryRepository : ITimeEntryRepository
    {
        private readonly ZentoryDbContext    _db;
        private readonly TimeEntryRepository _inner;

        public InMemoryTimeEntryRepository(ZentoryDbContext db)
        {
            _db    = db;
            _inner = new TimeEntryRepository(db);
        }

        public Task<TimeEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) => _inner.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<TimeEntry>> ListAsync(
            Guid organizationId, DateTime? from = null, DateTime? to = null, string? status = null,
            Guid? projectId = null, Guid? collaboratorId = null, CancellationToken ct = default)
            => _inner.ListAsync(organizationId, from, to, status, projectId, collaboratorId, ct);

        public Task<IReadOnlyList<TimeEntry>> GetByIdsAsync(IEnumerable<Guid> ids, Guid organizationId, CancellationToken ct = default)
            => _inner.GetByIdsAsync(ids, organizationId, ct);

        public Task AddAsync(TimeEntry entry, CancellationToken ct = default) => _inner.AddAsync(entry, ct);

        public Task UpdateAsync(TimeEntry entry, CancellationToken ct = default) => _inner.UpdateAsync(entry, ct);

        public async Task BatchMarkBilledAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            var idList = ids.ToList();
            var entries = await _db.TimeEntries.Where(t => idList.Contains(t.Id)).ToListAsync(ct);
            foreach (var entry in entries) entry.MarkBilled();
            await _db.SaveChangesAsync(ct);
        }
    }

    private sealed class FixedApplicationSettings : IApplicationSettings
    {
        public string BaseUrl => "https://app.zentory.test";
    }

    // Aprobar no envía correo; si el handler empezara a hacerlo, el test lo hace visible.
    private sealed class UnusedEmailService : IEmailService
    {
        public Task SendProposalEmailAsync(string toEmail, string proposalTitle, string clientName, string proposalUrl,
            string? customMessage, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SendProposalViewedNotificationAsync(string toEmail, string recipientName, string proposalTitle,
            string proposalDashboardUrl, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SendPilaRequestEmailAsync(string toEmail, string collaboratorName, string companyName, string period,
            string uploadUrl, DateTime expiresAt, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SendPayoutInvoiceGeneratedEmailAsync(string toEmail, string collaboratorName, string companyName,
            string period, decimal amount, string currency, string downloadUrl, string portalUrl,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task SendPayoutInvoiceRequestEmailAsync(string toEmail, string collaboratorName, string companyName,
            string period, string uploadUrl, DateTime expiresAt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SendCollaboratorPortalAccessEmailAsync(string toEmail, string collaboratorName, string magicLinkUrl,
            DateTime expiresAt, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SendPilaRejectedEmailAsync(string toEmail, string collaboratorName, string companyName, string period,
            string notes, string portalUrl, CancellationToken ct = default) => throw new NotSupportedException();

        public Task SendPayoutInvoiceRejectedEmailAsync(string toEmail, string collaboratorName, string companyName,
            string period, string notes, string portalUrl, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
