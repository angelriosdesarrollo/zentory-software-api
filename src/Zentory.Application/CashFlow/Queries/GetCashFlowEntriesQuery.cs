using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.CashFlow.DTOs;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Application.CashFlow.Queries;

public record GetCashFlowEntriesQuery(
    int?  Year      = null,
    int?  Month     = null,
    string? Type    = null,
    Guid? ProjectId = null) : IRequest<IReadOnlyList<CashFlowEntryDto>>;

public sealed class GetCashFlowEntriesQueryHandler
    : IRequestHandler<GetCashFlowEntriesQuery, IReadOnlyList<CashFlowEntryDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetCashFlowEntriesQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<CashFlowEntryDto>> Handle(
        GetCashFlowEntriesQuery request,
        CancellationToken       ct)
    {
        var oid   = _tenant.OrganizationId;
        var year  = request.Year  ?? DateTime.UtcNow.Year;
        var month = request.Month ?? DateTime.UtcNow.Month;

        // Carga categorías para enriquecer el DTO (son pocas — caben en memoria)
        var categories = await _db.ExpenseCategories
            .ToDictionaryAsync(c => c.Id, c => new { c.Slug, c.Name }, ct);

        var query = _db.CashFlowEntries
            .Where(e => e.OrganizationId == oid
                     && e.TransactionDate.Year  == year
                     && e.TransactionDate.Month == month);

        if (request.Type != null)
            query = query.Where(e => e.Type == request.Type);

        if (request.ProjectId.HasValue)
            query = query.Where(e => e.ProjectId == request.ProjectId.Value);

        var entries = await query
            .OrderByDescending(e => e.TransactionDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        return entries.Select(e =>
        {
            var cat = e.CategoryId.HasValue && categories.TryGetValue(e.CategoryId.Value, out var c) ? c : null;
            return new CashFlowEntryDto(
                Id:           e.Id.ToString(),
                Date:         e.TransactionDate.ToString("yyyy-MM-dd"),
                Concept:      e.Description,
                Type:         e.Type,
                CategorySlug: cat?.Slug,
                CategoryName: cat?.Name,
                ProjectId:    e.ProjectId?.ToString(),
                Amount:       e.Amount,
                Currency:     e.Currency,
                // Sin campo Status en la entidad; "confirmed" para entradas sin pendiente
                Status:       "confirmed"
            );
        }).ToList();
    }
}
