using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PayoutInvoices.Queries;

public record PayoutInvoiceProjectBreakdownItemDto(
    Guid    ProjectId,
    string  ProjectName,
    decimal Hours,
    decimal Amount,
    string  Currency);

// Solo lectura: de qué proyecto(s) viene el monto de una cuenta de cobro de un hourly_contractor,
// derivado de los mismos TimeEntry 'approved' que ya usa GetSuggestedPayoutAmountQuery para sugerir
// el monto. No crea ni modifica nada — el costo de estas horas ya se contabiliza por proyecto vía
// GetProfitabilityStatsQuery, esto es solo para que quien aprueba vea a qué se imputa sin adivinar.
public record GetPayoutInvoiceProjectBreakdownQuery(Guid PayoutInvoiceId)
    : IRequest<IReadOnlyList<PayoutInvoiceProjectBreakdownItemDto>>;

public sealed class GetPayoutInvoiceProjectBreakdownQueryHandler
    : IRequestHandler<GetPayoutInvoiceProjectBreakdownQuery, IReadOnlyList<PayoutInvoiceProjectBreakdownItemDto>>
{
    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ITimeEntryRepository                 _timeEntries;
    private readonly IProjectRepository                   _projects;
    private readonly ITenantContext                       _tenant;

    public GetPayoutInvoiceProjectBreakdownQueryHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        ITimeEntryRepository                 timeEntries,
        IProjectRepository                   projects,
        ITenantContext                       tenant)
    {
        _invoices    = invoices;
        _timeEntries = timeEntries;
        _projects    = projects;
        _tenant      = tenant;
    }

    public async Task<IReadOnlyList<PayoutInvoiceProjectBreakdownItemDto>> Handle(
        GetPayoutInvoiceProjectBreakdownQuery request,
        CancellationToken                     cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.PayoutInvoiceId, cancellationToken);
        if (invoice is null || invoice.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("CollaboratorPayoutInvoice", request.PayoutInvoiceId);

        var (from, to) = ParsePeriodRange(invoice.Period);

        var entries = await _timeEntries.ListAsync(
            _tenant.OrganizationId,
            from: from,
            to: to,
            status: "approved",
            projectId: null,
            collaboratorId: invoice.CollaboratorId,
            ct: cancellationToken);

        if (entries.Count == 0)
            return [];

        var projects = await _projects.ListByOrganizationAsync(_tenant.OrganizationId, cancellationToken);
        var projectNames = projects.ToDictionary(p => p.Id, p => p.Name);

        return entries
            .GroupBy(e => e.ProjectId)
            .Select(g => new PayoutInvoiceProjectBreakdownItemDto(
                ProjectId:   g.Key,
                ProjectName: projectNames.GetValueOrDefault(g.Key, "Proyecto eliminado"),
                Hours:       g.Sum(e => e.Hours),
                Amount:      g.Sum(e => e.Hours * e.RateCost),
                Currency:    g.First().Currency))
            .OrderByDescending(x => x.Amount)
            .ToList();
    }

    private static (DateTime From, DateTime To) ParsePeriodRange(string period)
    {
        var parts = period.Split('-');
        var year  = int.Parse(parts[0]);
        var month = int.Parse(parts[1]);
        var from  = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to    = from.AddMonths(1).AddTicks(-1);
        return (from, to);
    }
}
