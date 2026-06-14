using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Organization.DTOs;

namespace Zentory.Application.Organization.Queries;

public record GetBillingHistoryQuery : IRequest<IReadOnlyList<BillingHistoryItemDto>>;

public sealed class GetBillingHistoryQueryHandler
    : IRequestHandler<GetBillingHistoryQuery, IReadOnlyList<BillingHistoryItemDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetBillingHistoryQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<BillingHistoryItemDto>> Handle(
        GetBillingHistoryQuery request, CancellationToken ct)
    {
        var invoices = await _db.BillingInvoices
            .Where(i => i.OrganizationId == _tenant.OrganizationId && i.Status == "paid")
            .OrderByDescending(i => i.PaidAt)
            .Take(12)
            .ToListAsync(ct);

        return invoices.Select(i => new BillingHistoryItemDto(
            PaidAt:        (i.PaidAt ?? i.CreatedAt).ToString("yyyy-MM-dd"),
            Amount:        i.Amount,
            Currency:      i.Currency,
            InvoiceNumber: i.InvoiceNumber,
            Status:        "paid"
        )).ToList();
    }
}
