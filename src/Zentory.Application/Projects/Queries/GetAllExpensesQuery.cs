using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Queries;

// Vista cross-proyecto para /finances: todos los gastos (de cualquier proyecto, y los de
// empresa con ProjectId = null), con el nombre de proyecto resuelto para poder agruparlos
// en la UI. GetProjectExpensesQuery sigue siendo el listado normal de un solo proyecto.
public record GetAllExpensesQuery : IRequest<IReadOnlyList<ProjectExpenseDto>>;

public sealed class GetAllExpensesQueryHandler
    : IRequestHandler<GetAllExpensesQuery, IReadOnlyList<ProjectExpenseDto>>
{
    private readonly ProjectExpenseStore _store;
    private readonly IProjectRepository  _projects;
    private readonly ITenantContext      _tenant;

    public GetAllExpensesQueryHandler(ProjectExpenseStore store, IProjectRepository projects, ITenantContext tenant)
    {
        _store    = store;
        _projects = projects;
        _tenant   = tenant;
    }

    public async Task<IReadOnlyList<ProjectExpenseDto>> Handle(
        GetAllExpensesQuery request,
        CancellationToken    cancellationToken)
    {
        var expenses = _store.GetAll();
        if (expenses.Count == 0) return expenses;

        var projects = await _projects.ListByOrganizationAsync(_tenant.OrganizationId, cancellationToken);
        var projectNames = projects.ToDictionary(p => p.Id, p => p.Name);

        return expenses
            .Select(e => e with
            {
                ProjectName = e.ProjectId is null
                    ? "Gasto de empresa"
                    : projectNames.GetValueOrDefault(e.ProjectId.Value, "Proyecto eliminado"),
            })
            .ToList();
    }
}
