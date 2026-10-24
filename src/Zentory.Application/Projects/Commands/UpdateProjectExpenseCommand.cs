using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.Queries;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

// Permite reasignar el proyecto de un gasto ya registrado (o dejarlo como gasto de empresa
// con ProjectId = null), además de corregir los demás campos. No está anidado bajo
// /projects/{projectId} porque el propósito es justamente poder cambiar ese projectId.
public record UpdateProjectExpenseCommand(
    Guid    Id,
    Guid?   ProjectId,
    string  Date,
    string  Category,
    string  Description,
    decimal Amount,
    string  Currency,
    string? Vendor,
    bool    Billable,
    string  Status) : IRequest<ProjectExpenseDto>;

public sealed class UpdateProjectExpenseCommandHandler
    : IRequestHandler<UpdateProjectExpenseCommand, ProjectExpenseDto>
{
    private readonly ProjectExpenseStore _store;
    private readonly IProjectRepository  _projects;
    private readonly ITenantContext      _tenant;

    public UpdateProjectExpenseCommandHandler(ProjectExpenseStore store, IProjectRepository projects, ITenantContext tenant)
    {
        _store    = store;
        _projects = projects;
        _tenant   = tenant;
    }

    public async Task<ProjectExpenseDto> Handle(
        UpdateProjectExpenseCommand request,
        CancellationToken           cancellationToken)
    {
        // Un gasto de otra organización es indistinguible de uno inexistente (404).
        var existing = _store.GetById(_tenant.OrganizationId, request.Id)
            ?? throw new NotFoundException("ProjectExpense", request.Id);

        // D-7: reasignar el gasto a un proyecto de otra organización también es 404 y no toca
        // el store. Comparación explícita: GetByIdAsync no filtra por organización. Solo se
        // valida si el proyecto cambia: el actual ya viene del gasto propio de la organización.
        if (request.ProjectId is { } projectId && projectId != existing.ProjectId)
        {
            var project = await _projects.GetByIdAsync(projectId, cancellationToken);
            if (project is null || project.OrganizationId != _tenant.OrganizationId)
                throw new NotFoundException("Project", projectId);
        }

        var updated = existing with
        {
            ProjectId   = request.ProjectId,
            Date        = request.Date,
            Category    = request.Category,
            Description = request.Description,
            Amount      = request.Amount,
            Currency    = request.Currency,
            Vendor      = request.Vendor,
            Billable    = request.Billable,
            Status      = request.Status,
        };

        _ = _store.Update(_tenant.OrganizationId, updated)
            ?? throw new NotFoundException("ProjectExpense", request.Id);
        return updated;
    }
}
