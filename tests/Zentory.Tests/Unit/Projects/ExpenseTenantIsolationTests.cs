using System.Reflection;
using System.Text.Json;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects;
using Zentory.Application.Projects.Commands;
using Zentory.Application.Projects.Queries;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Projects;

// R-2: el store de gastos es un singleton compartido por todas las organizaciones; cada
// lectura y escritura debe quedar acotada a la organización del token. Cada test usa
// organizaciones nuevas para que la semilla de DevOrg no altere los conteos.
public sealed class ExpenseTenantIsolationTests
{
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();

    private readonly ProjectExpenseStore   _store    = new();
    private readonly FakeProjectRepository _projects = new();

    private readonly Project _projectA;
    private readonly Project _projectB;

    private readonly ProjectExpenseDto _projectExpenseA;
    private readonly ProjectExpenseDto _companyExpenseA;
    private readonly ProjectExpenseDto _projectExpenseB;
    private readonly ProjectExpenseDto _companyExpenseB;

    public ExpenseTenantIsolationTests()
    {
        _projectA = Project.Create(_orgA, Guid.NewGuid(), "Proyecto A", BillingType.Hourly, 1000m);
        _projectB = Project.Create(_orgB, Guid.NewGuid(), "Proyecto B", BillingType.Hourly, 1000m);
        _projects.Items.AddRange([_projectA, _projectB]);

        _projectExpenseA = NewExpense(_projectA.Id, "Gasto proyecto A");
        _companyExpenseA = NewExpense(null,         "Gasto empresa A");
        _projectExpenseB = NewExpense(_projectB.Id, "Gasto proyecto B");
        _companyExpenseB = NewExpense(null,         "Gasto empresa B");

        _store.Add(_orgA, _projectExpenseA);
        _store.Add(_orgA, _companyExpenseA);
        _store.Add(_orgB, _projectExpenseB);
        _store.Add(_orgB, _companyExpenseB);
    }

    private static ProjectExpenseDto NewExpense(Guid? projectId, string description) => new(
        Id: Guid.NewGuid(), ProjectId: projectId, Date: "2026-10-01", Category: "otro",
        Description: description, Amount: 100m, Currency: "USD", Vendor: null,
        Billable: false, Status: "pendiente", CreatedBy: "TT");

    private Task<IReadOnlyList<ProjectExpenseDto>> GetAllAs(Guid organizationId)
        => new GetAllExpensesQueryHandler(_store, _projects, FakeTenantContext.For(organizationId))
            .Handle(new GetAllExpensesQuery(), CancellationToken.None);

    // ── CA-5 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CA5_GetAllExpenses_UserOfOrgB_ReturnsExactlyBothOrgBExpenses()
    {
        var result = await GetAllAs(_orgB);

        Assert.Equal(
            new[] { _projectExpenseB.Id, _companyExpenseB.Id }.Order(),
            result.Select(e => e.Id).Order());
    }

    [Fact]
    public async Task CA5_GetAllExpenses_UserOfOrgA_ReturnsExactlyBothOrgAExpenses()
    {
        var result = await GetAllAs(_orgA);

        Assert.Equal(
            new[] { _projectExpenseA.Id, _companyExpenseA.Id }.Order(),
            result.Select(e => e.Id).Order());
    }

    [Fact]
    public async Task CA5_GetAllExpenses_ResolvesProjectNamesOfOwnOrganization()
    {
        var result = await GetAllAs(_orgB);

        Assert.Equal("Proyecto B",       result.Single(e => e.Id == _projectExpenseB.Id).ProjectName);
        Assert.Equal("Gasto de empresa", result.Single(e => e.Id == _companyExpenseB.Id).ProjectName);
    }

    [Fact]
    public async Task CA5_GetAllExpenses_OrganizationWithoutExpenses_ReturnsEmpty()
    {
        var result = await GetAllAs(Guid.NewGuid());

        Assert.Empty(result);
    }

    // ── CA-T3-1: store ───────────────────────────────────────────────────────

    [Fact]
    public void CAT3_1_Store_EveryPublicMethodTakesOrganizationIdAsFirstParameter()
    {
        var methods = typeof(ProjectExpenseStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // getters de propiedades (Count)
            .ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, m =>
        {
            var first = m.GetParameters().FirstOrDefault();
            Assert.True(first is { Name: "organizationId" } && first.ParameterType == typeof(Guid),
                $"{m.Name} no recibe Guid organizationId como primer parámetro");
        });
    }

    [Fact]
    public void CAT3_1_Store_GetByProject_ReturnsOnlyExpensesOfThatOrganization()
    {
        Assert.Empty(_store.GetByProject(_orgA, _projectB.Id));
        Assert.Equal(_projectExpenseB.Id, Assert.Single(_store.GetByProject(_orgB, _projectB.Id)).Id);
    }

    [Fact]
    public void CAT3_1_Store_GetById_OfOtherOrganization_ReturnsNull()
    {
        Assert.Null(_store.GetById(_orgA, _projectExpenseB.Id));
        Assert.NotNull(_store.GetById(_orgB, _projectExpenseB.Id));
    }

    [Fact]
    public void CAT3_1_Store_Update_OfOtherOrganization_ReturnsNullAndLeavesExpenseUntouched()
    {
        var tampered = _projectExpenseB with { Amount = 999m };

        Assert.Null(_store.Update(_orgA, tampered));
        Assert.Equal(100m, _store.GetById(_orgB, _projectExpenseB.Id)!.Amount);
    }

    [Fact]
    public void CAT3_1_Store_Update_KeepsOriginalOrganization()
    {
        var updated = _store.Update(_orgB, _projectExpenseB with { Amount = 250m });

        Assert.Equal(250m, updated!.Amount);
        Assert.Equal(250m, _store.GetById(_orgB, _projectExpenseB.Id)!.Amount);
        Assert.Null(_store.GetById(_orgA, _projectExpenseB.Id));
    }

    [Fact]
    public async Task CAT3_1_Store_ConcurrentAdds_AreAllRecorded()
    {
        var org    = Guid.NewGuid();
        var store  = new ProjectExpenseStore();
        var before = store.Count;

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i =>
            Task.Run(() => store.Add(org, NewExpense(null, $"Gasto {i}")))));

        Assert.Equal(before + 200, store.Count);
        Assert.Equal(200, store.GetAll(org).Count);
    }

    [Fact]
    public void CAT3_1_ProjectExpenseDto_SerializedJson_DoesNotContainOrganizationId()
    {
        var json = JsonSerializer.Serialize(_projectExpenseA, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("organizationId", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(typeof(ProjectExpenseDto).GetProperty("OrganizationId"));
    }

    // RF-5: la semilla del store pertenece solo a la organización de dev. Los dos GUID son privados
    // (ProjectExpenseStore.DevOrg y DevDataSeeder.DevOrgId), así que se leen por reflexión: si alguno
    // cambia de nombre o de valor sin el otro, este test falla en lugar de dejar la semilla invisible.
    [Fact]
    public void CAT3_1_Store_Seed_IsTaggedWithDevOrgOfDevDataSeederOnly()
    {
        var storeDevOrg  = ReadPrivateStaticGuid(typeof(ProjectExpenseStore), "DevOrg");
        var seederDevOrg = ReadPrivateStaticGuid(typeof(Zentory.Infrastructure.Persistence.DevDataSeeder), "DevOrgId");

        Assert.Equal(seederDevOrg, storeDevOrg);

        var fresh = new ProjectExpenseStore();
        Assert.NotEmpty(fresh.GetAll(seederDevOrg));
        Assert.Equal(fresh.Count, fresh.GetAll(seederDevOrg).Count);
        Assert.Empty(fresh.GetAll(Guid.NewGuid()));
    }

    private static Guid ReadPrivateStaticGuid(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsType<Guid>(field!.GetValue(null));
    }

    // ── CA-T3-1: las llamadas ajustadas usan la organización del token ───────

    [Fact]
    public async Task CAT3_1_CreateExpense_TagsExpenseWithTokenOrganization()
    {
        var handler = new CreateProjectExpenseCommandHandler(_store, _projects, FakeTenantContext.For(_orgA));

        var created = await handler.Handle(new CreateProjectExpenseCommand(
            null, "2026-10-02", "otro", "Nuevo", 50m, "USD", null, false), CancellationToken.None);

        Assert.NotNull(_store.GetById(_orgA, created.Id));
        Assert.Null(_store.GetById(_orgB, created.Id));
    }

    [Fact]
    public async Task CAT3_1_GetProjectExpenses_OwnProject_ReturnsOnlyItsExpenses()
    {
        // El caso del proyecto ajeno pasó a 404 en T4 (CA-6, ExpenseCrossTenantNotFoundTests).
        var handler = new GetProjectExpensesQueryHandler(_store, _projects, FakeTenantContext.For(_orgA));

        var result = await handler.Handle(new GetProjectExpensesQuery(_projectA.Id), CancellationToken.None);

        Assert.Equal(_projectExpenseA.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task CAT3_1_UpdateExpense_OfOtherOrganization_ThrowsNotFoundAndLeavesExpenseUntouched()
    {
        var handler = new UpdateProjectExpenseCommandHandler(_store, _projects, FakeTenantContext.For(_orgA));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new UpdateProjectExpenseCommand(
            _projectExpenseB.Id, null, "2026-10-03", "otro", "Modificado", 1m, "USD", null, false, "aprobado"),
            CancellationToken.None));

        var original = _store.GetById(_orgB, _projectExpenseB.Id)!;
        Assert.Equal("Gasto proyecto B", original.Description);
        Assert.Equal(_projectB.Id, original.ProjectId);
    }
}
