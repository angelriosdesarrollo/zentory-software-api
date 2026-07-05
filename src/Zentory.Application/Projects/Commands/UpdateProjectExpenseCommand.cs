using MediatR;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.Queries;

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

    public UpdateProjectExpenseCommandHandler(ProjectExpenseStore store) => _store = store;

    public Task<ProjectExpenseDto> Handle(
        UpdateProjectExpenseCommand request,
        CancellationToken           cancellationToken)
    {
        var existing = _store.GetById(request.Id)
            ?? throw new NotFoundException("ProjectExpense", request.Id);

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

        _store.Update(updated);
        return Task.FromResult(updated);
    }
}
