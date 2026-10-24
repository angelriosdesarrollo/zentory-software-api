using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Queries;

public record ProjectExpenseDto(
    Guid    Id,
    Guid?   ProjectId,
    string  Date,
    string  Category,
    string  Description,
    decimal Amount,
    string  Currency,
    string? Vendor,
    bool    Billable,
    string  Status,
    string  CreatedBy,
    string  Source = "manual",
    Guid?   SourcePayoutInvoiceId = null,
    // Solo poblado por GetAllExpensesQuery (vista cross-proyecto en /finances) — el listado
    // por proyecto (GetProjectExpensesQuery) no lo necesita, el proyecto ya está implícito.
    string? ProjectName = null);

public record GetProjectExpensesQuery(Guid ProjectId) : IRequest<IReadOnlyList<ProjectExpenseDto>>;

public sealed class GetProjectExpensesQueryHandler
    : IRequestHandler<GetProjectExpensesQuery, IReadOnlyList<ProjectExpenseDto>>
{
    private readonly ProjectExpenseStore _store;
    private readonly IProjectRepository  _projects;
    private readonly ITenantContext      _tenant;

    public GetProjectExpensesQueryHandler(ProjectExpenseStore store, IProjectRepository projects, ITenantContext tenant)
    {
        _store    = store;
        _projects = projects;
        _tenant   = tenant;
    }

    public async Task<IReadOnlyList<ProjectExpenseDto>> Handle(
        GetProjectExpensesQuery request,
        CancellationToken       cancellationToken)
    {
        // Comparación explícita: GetByIdAsync no filtra por organización (no se confía en el
        // filtro global de EF). Un proyecto ajeno es indistinguible de uno inexistente (404).
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Project", request.ProjectId);

        return _store.GetByProject(_tenant.OrganizationId, request.ProjectId);
    }
}
