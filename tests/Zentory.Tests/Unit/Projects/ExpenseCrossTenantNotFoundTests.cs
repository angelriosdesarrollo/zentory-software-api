using Zentory.Application.Exceptions;
using Zentory.Application.Projects;
using Zentory.Application.Projects.Commands;
using Zentory.Application.Projects.Queries;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Projects;

// CA-6 / RNF-2: un proyecto o gasto de otra organización responde 404 (NotFoundException), no 403,
// y el store queda intacto. FakeProjectRepository.GetByIdAsync no filtra por organización, igual
// que el real, así que estos tests prueban la comparación explícita de OrganizationId (R-1).
public sealed class ExpenseCrossTenantNotFoundTests
{
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();

    private readonly ProjectExpenseStore   _store    = new();
    private readonly FakeProjectRepository _projects = new();

    private readonly Project           _projectA;
    private readonly Project           _projectB;
    private readonly ProjectExpenseDto _expenseA;
    private readonly ProjectExpenseDto _expenseB;

    public ExpenseCrossTenantNotFoundTests()
    {
        _projectA = Project.Create(_orgA, Guid.NewGuid(), "Proyecto A", BillingType.Hourly, 1000m);
        _projectB = Project.Create(_orgB, Guid.NewGuid(), "Proyecto B", BillingType.Hourly, 1000m);
        _projects.Items.AddRange([_projectA, _projectB]);

        _expenseA = new ProjectExpenseDto(
            Id: Guid.NewGuid(), ProjectId: _projectA.Id, Date: "2026-10-01", Category: "software",
            Description: "Licencia A", Amount: 120m, Currency: "USD", Vendor: "Proveedor A",
            Billable: true, Status: "pendiente", CreatedBy: "AA");
        _expenseB = new ProjectExpenseDto(
            Id: Guid.NewGuid(), ProjectId: _projectB.Id, Date: "2026-10-02", Category: "otro",
            Description: "Gasto B", Amount: 80m, Currency: "USD", Vendor: null,
            Billable: false, Status: "pendiente", CreatedBy: "BB");

        _store.Add(_orgA, _expenseA);
        _store.Add(_orgB, _expenseB);
    }

    private FakeTenantContext TenantB => FakeTenantContext.For(_orgB);

    // Count cuenta todas las entradas del store (cualquier organización, incluida la semilla de dev):
    // un handler rechazado que agregara un gasto etiquetado con otra organización también se detecta.
    private int TotalExpenses => _store.Count;

    // El record compara por valor: igualdad = el gasto de A conserva todos sus campos.
    private void AssertExpenseAUntouched()
        => Assert.Equal(_expenseA, _store.GetById(_orgA, _expenseA.Id));

    // ── CA-6 (i) ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CA6_GetProjectExpenses_ProjectOfOtherOrganization_ThrowsNotFoundAndStoreUnchanged()
    {
        var before  = TotalExpenses;
        var handler = new GetProjectExpensesQueryHandler(_store, _projects, TenantB);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetProjectExpensesQuery(_projectA.Id), CancellationToken.None));

        Assert.Equal("Project", ex.ResourceType);
        Assert.Equal(before, TotalExpenses);
        AssertExpenseAUntouched();
    }

    [Fact]
    public async Task CA6_GetProjectExpenses_NonexistentProject_ThrowsNotFound()
    {
        var handler = new GetProjectExpensesQueryHandler(_store, _projects, TenantB);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetProjectExpensesQuery(Guid.NewGuid()), CancellationToken.None));
    }

    // ── CA-6 (ii) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CA6_UpdateExpense_ExpenseOfOtherOrganization_ThrowsNotFoundAndStoreUnchanged()
    {
        var before  = TotalExpenses;
        var handler = new UpdateProjectExpenseCommandHandler(_store, _projects, TenantB);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new UpdateProjectExpenseCommand(
            _expenseA.Id, _projectB.Id, "2026-10-09", "otro", "Modificado por B", 1m, "COP", "X", false, "aprobado"),
            CancellationToken.None));

        Assert.Equal(before, TotalExpenses);
        AssertExpenseAUntouched();
    }

    // ── CA-6 (iii) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CA6_CreateExpense_ProjectOfOtherOrganization_ThrowsNotFoundAndStoreUnchanged()
    {
        var before  = TotalExpenses;
        var handler = new CreateProjectExpenseCommandHandler(_store, _projects, TenantB);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new CreateProjectExpenseCommand(
            _projectA.Id, "2026-10-09", "otro", "Intruso", 50m, "USD", null, false), CancellationToken.None));

        Assert.Equal("Project", ex.ResourceType);
        Assert.Equal(before, TotalExpenses);
        Assert.Empty(_store.GetByProject(_orgB, _projectA.Id));
        AssertExpenseAUntouched();
    }

    [Fact]
    public async Task CA6_CreateExpense_WithoutProject_IsStillAcceptedAsCompanyExpense()
    {
        var before  = TotalExpenses;
        var handler = new CreateProjectExpenseCommandHandler(_store, _projects, TenantB);

        var created = await handler.Handle(new CreateProjectExpenseCommand(
            null, "2026-10-09", "otro", "Gasto empresa B", 30m, "USD", null, false), CancellationToken.None);

        Assert.Null(created.ProjectId);
        Assert.Equal(before + 1, TotalExpenses);
        Assert.NotNull(_store.GetById(_orgB, created.Id));
    }

    [Fact]
    public async Task CA6_CreateExpense_OwnProject_IsAccepted()
    {
        var handler = new CreateProjectExpenseCommandHandler(_store, _projects, TenantB);

        var created = await handler.Handle(new CreateProjectExpenseCommand(
            _projectB.Id, "2026-10-09", "otro", "Gasto proyecto B", 30m, "USD", null, false), CancellationToken.None);

        Assert.Equal(_projectB.Id, _store.GetById(_orgB, created.Id)!.ProjectId);
    }

    // ── CA-T4-1 (D-7) ────────────────────────────────────────────────────────

    [Fact]
    public async Task CAT4_1_UpdateExpense_ReassignOwnExpenseToProjectOfOtherOrganization_ThrowsNotFoundAndKeepsProjectId()
    {
        var before  = TotalExpenses;
        var handler = new UpdateProjectExpenseCommandHandler(_store, _projects, TenantB);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new UpdateProjectExpenseCommand(
            _expenseB.Id, _projectA.Id, "2026-10-09", "otro", "Reasignado", 80m, "USD", null, false, "pendiente"),
            CancellationToken.None));

        Assert.Equal("Project", ex.ResourceType);
        Assert.Equal(before, TotalExpenses);
        Assert.Equal(_expenseB, _store.GetById(_orgB, _expenseB.Id));
        AssertExpenseAUntouched();
    }

    [Fact]
    public async Task CAT4_1_UpdateExpense_SameProjectThatNoLongerResolves_IsAcceptedWithoutRevalidating()
    {
        // El gasto ya es de la organización del token (GetById(org, id)); si el ProjectId no cambia
        // no se vuelve a validar, así que un proyecto que ya no resuelve no bloquea la edición.
        var orphan = _expenseB with { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid() };
        _store.Add(_orgB, orphan);
        var handler = new UpdateProjectExpenseCommandHandler(_store, _projects, TenantB);

        var updated = await handler.Handle(new UpdateProjectExpenseCommand(
            orphan.Id, orphan.ProjectId, orphan.Date, orphan.Category, orphan.Description, orphan.Amount,
            orphan.Currency, orphan.Vendor, orphan.Billable, "aprobado"), CancellationToken.None);

        Assert.Equal("aprobado", updated.Status);
        Assert.Equal(orphan.ProjectId, _store.GetById(_orgB, orphan.Id)!.ProjectId);
    }

    [Fact]
    public async Task CAT4_1_UpdateExpense_KeepOwnProject_IsAccepted()
    {
        var handler = new UpdateProjectExpenseCommandHandler(_store, _projects, TenantB);

        var updated = await handler.Handle(new UpdateProjectExpenseCommand(
            _expenseB.Id, _projectB.Id, "2026-10-09", "otro", "Mismo proyecto", 90m, "USD", null, false, "aprobado"),
            CancellationToken.None);

        Assert.Equal(_projectB.Id, updated.ProjectId);
        Assert.Equal(90m, _store.GetById(_orgB, _expenseB.Id)!.Amount);
    }

    [Fact]
    public async Task CAT4_1_UpdateExpense_ReassignOwnExpenseToCompany_IsAccepted()
    {
        var handler = new UpdateProjectExpenseCommandHandler(_store, _projects, TenantB);

        var updated = await handler.Handle(new UpdateProjectExpenseCommand(
            _expenseB.Id, null, "2026-10-09", "otro", "Ahora de empresa", 80m, "USD", null, false, "pendiente"),
            CancellationToken.None);

        Assert.Null(updated.ProjectId);
        Assert.Null(_store.GetById(_orgB, _expenseB.Id)!.ProjectId);
    }
}
