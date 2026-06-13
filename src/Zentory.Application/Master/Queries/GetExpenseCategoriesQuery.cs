using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Master.DTOs;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Application.Master.Queries;

public record GetExpenseCategoriesQuery(string? Type = null)
    : IRequest<IReadOnlyList<ExpenseCategoryDto>>;

public sealed class GetExpenseCategoriesQueryHandler
    : IRequestHandler<GetExpenseCategoriesQuery, IReadOnlyList<ExpenseCategoryDto>>
{
    private readonly ZentoryDbContext _db;

    public GetExpenseCategoriesQueryHandler(ZentoryDbContext db) => _db = db;

    public async Task<IReadOnlyList<ExpenseCategoryDto>> Handle(
        GetExpenseCategoriesQuery request,
        CancellationToken         cancellationToken)
    {
        var query = _db.ExpenseCategories.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Type))
            query = query.Where(c => c.Type == request.Type || c.Type == "both");

        return await query
            .OrderBy(c => c.Id)
            .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.Slug, c.Type))
            .ToListAsync(cancellationToken);
    }
}
