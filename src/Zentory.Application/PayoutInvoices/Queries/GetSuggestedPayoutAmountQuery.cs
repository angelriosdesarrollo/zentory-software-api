using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.PayoutInvoices.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PayoutInvoices.Queries;

public record GetSuggestedPayoutAmountQuery(
    Guid   CollaboratorId,
    string Period) : IRequest<SuggestedPayoutAmountDto>;

// Suma Hours × RateCost de los TimeEntry 'approved' del colaborador en el período,
// como sugerencia para la cuenta de cobro de un contractor por horas. El backend
// no calcula RateCost al crear un TimeEntry (lo decide quien lo registra), así que
// esto es una suma directa, no una tarifa "oficial" recalculada.
public sealed class GetSuggestedPayoutAmountQueryHandler
    : IRequestHandler<GetSuggestedPayoutAmountQuery, SuggestedPayoutAmountDto>
{
    private readonly ICollaboratorRepository _collaborators;
    private readonly ITimeEntryRepository    _timeEntries;
    private readonly ITenantContext          _tenant;

    public GetSuggestedPayoutAmountQueryHandler(
        ICollaboratorRepository collaborators,
        ITimeEntryRepository    timeEntries,
        ITenantContext          tenant)
    {
        _collaborators = collaborators;
        _timeEntries   = timeEntries;
        _tenant        = tenant;
    }

    public async Task<SuggestedPayoutAmountDto> Handle(
        GetSuggestedPayoutAmountQuery request, CancellationToken cancellationToken)
    {
        var collaborator = await _collaborators.GetByIdAsync(request.CollaboratorId, cancellationToken);
        if (collaborator is null || collaborator.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Collaborator", request.CollaboratorId);

        var (from, to) = ParsePeriodRange(request.Period);

        var entries = await _timeEntries.ListAsync(
            _tenant.OrganizationId,
            from: from,
            to: to,
            status: "approved",
            projectId: null,
            collaboratorId: request.CollaboratorId,
            ct: cancellationToken);

        var hours  = entries.Sum(e => e.Hours);
        var amount = entries.Sum(e => e.Hours * e.RateCost);

        return new SuggestedPayoutAmountDto(amount, hours, entries.Count > 0);
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
