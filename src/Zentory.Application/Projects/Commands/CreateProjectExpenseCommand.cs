using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.Queries;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

public record CreateProjectExpenseCommand(
    Guid?   ProjectId,
    string  Date,
    string  Category,
    string  Description,
    decimal Amount,
    string  Currency,
    string? Vendor,
    bool    Billable) : IRequest<ProjectExpenseDto>;

public sealed class CreateProjectExpenseCommandHandler
    : IRequestHandler<CreateProjectExpenseCommand, ProjectExpenseDto>
{
    private readonly ProjectExpenseStore _store;
    private readonly IProjectRepository  _projects;
    private readonly ITenantContext      _tenant;

    public CreateProjectExpenseCommandHandler(ProjectExpenseStore store, IProjectRepository projects, ITenantContext tenant)
    {
        _store    = store;
        _projects = projects;
        _tenant   = tenant;
    }

    public async Task<ProjectExpenseDto> Handle(
        CreateProjectExpenseCommand request,
        CancellationToken           cancellationToken)
    {
        // Un gasto sin ProjectId es de empresa y no necesita validación de proyecto. Comparación
        // explícita: GetByIdAsync no filtra por organización (no se confía en el filtro global de EF).
        if (request.ProjectId is { } projectId)
        {
            var project = await _projects.GetByIdAsync(projectId, cancellationToken);
            if (project is null || project.OrganizationId != _tenant.OrganizationId)
                throw new NotFoundException("Project", projectId);
        }

        var expense = new ProjectExpenseDto(
            Id:          Guid.NewGuid(),
            ProjectId:   request.ProjectId,
            Date:        request.Date,
            Category:    request.Category,
            Description: request.Description,
            Amount:      request.Amount,
            Currency:    request.Currency,
            Vendor:      request.Vendor,
            Billable:    request.Billable,
            Status:      "pendiente",
            CreatedBy:   _tenant.UserInitials);

        _store.Add(_tenant.OrganizationId, expense);
        return expense;
    }
}
