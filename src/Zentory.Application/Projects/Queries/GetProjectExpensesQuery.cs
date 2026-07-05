using MediatR;
using Zentory.Application.Projects;

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
    Guid?   SourcePayoutInvoiceId = null);

public record GetProjectExpensesQuery(Guid ProjectId) : IRequest<IReadOnlyList<ProjectExpenseDto>>;

public sealed class GetProjectExpensesQueryHandler
    : IRequestHandler<GetProjectExpensesQuery, IReadOnlyList<ProjectExpenseDto>>
{
    private readonly ProjectExpenseStore _store;

    public GetProjectExpensesQueryHandler(ProjectExpenseStore store) => _store = store;

    public Task<IReadOnlyList<ProjectExpenseDto>> Handle(
        GetProjectExpensesQuery request,
        CancellationToken       cancellationToken)
        => Task.FromResult(_store.GetByProject(request.ProjectId));
}
