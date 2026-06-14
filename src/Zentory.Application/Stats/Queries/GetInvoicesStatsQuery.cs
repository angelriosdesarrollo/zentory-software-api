using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Application.Stats.Queries;

// ── DTO ───────────────────────────────────────────────────────────────────────

public record InvoicesStatsDto(
    decimal TotalIssued,
    decimal Collected,
    decimal Pending,
    decimal Overdue,
    string  Currency
);

// ── Query ─────────────────────────────────────────────────────────────────────

public record GetInvoicesStatsQuery : IRequest<InvoicesStatsDto>;

public sealed class GetInvoicesStatsQueryHandler
    : IRequestHandler<GetInvoicesStatsQuery, InvoicesStatsDto>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetInvoicesStatsQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<InvoicesStatsDto> Handle(
        GetInvoicesStatsQuery request,
        CancellationToken     ct)
    {
        var oid = _tenant.OrganizationId;

        // El query filter de Invoice ya filtra DeletedAt == null y OrganizationId
        var invoices = await _db.Invoices
            .Where(i => i.OrganizationId == oid)
            .Select(i => new
            {
                i.Total,
                i.AmountPaid,
                i.AmountDue,
                i.Status
            })
            .ToListAsync(ct);

        var totalIssued = invoices.Sum(i => i.Total);
        var collected   = invoices.Sum(i => i.AmountPaid);

        // Pendiente: facturas enviadas / vistas / parciales con monto pendiente
        var pending = invoices
            .Where(i => i.Status is "sent" or "viewed" or "partial")
            .Sum(i => i.AmountDue);

        var overdue = invoices
            .Where(i => i.Status == "overdue")
            .Sum(i => i.AmountDue);

        return new InvoicesStatsDto(
            TotalIssued: totalIssued,
            Collected:   collected,
            Pending:     pending,
            Overdue:     overdue,
            Currency:    "USD"
        );
    }
}
