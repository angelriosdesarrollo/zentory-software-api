using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Projects.Queries;

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
    private readonly ITenantContext      _tenant;

    public CreateProjectExpenseCommandHandler(ProjectExpenseStore store, ITenantContext tenant)
    {
        _store  = store;
        _tenant = tenant;
    }

    public Task<ProjectExpenseDto> Handle(
        CreateProjectExpenseCommand request,
        CancellationToken           cancellationToken)
    {
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

        _store.Add(expense);
        return Task.FromResult(expense);
    }
}
