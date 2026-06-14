using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Master.DTOs;

namespace Zentory.Application.Master.Queries;

public record GetExpenseCategoriesQuery(string? Type = null)
    : IRequest<IReadOnlyList<ExpenseCategoryDto>>;

public sealed class GetExpenseCategoriesQueryHandler
    : IRequestHandler<GetExpenseCategoriesQuery, IReadOnlyList<ExpenseCategoryDto>>
{
    private readonly IZentoryDbContext _db;

    public GetExpenseCategoriesQueryHandler(IZentoryDbContext db) => _db = db;

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
