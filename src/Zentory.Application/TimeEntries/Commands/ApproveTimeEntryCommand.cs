using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.TimeEntries.Commands;

public record ApproveTimeEntryCommand(Guid Id) : IRequest;

public sealed class ApproveTimeEntryCommandHandler : IRequestHandler<ApproveTimeEntryCommand>
{
    private readonly ITimeEntryRepository _timeEntries;
    private readonly IUnitOfWork          _uow;
    private readonly ITenantContext       _tenant;

    public ApproveTimeEntryCommandHandler(
        ITimeEntryRepository timeEntries,
        IUnitOfWork          uow,
        ITenantContext       tenant)
    {
        _timeEntries = timeEntries;
        _uow         = uow;
        _tenant      = tenant;
    }

    public async Task Handle(ApproveTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await _timeEntries.GetByIdAsync(request.Id, cancellationToken);
        if (entry is null || entry.OrganizationId != _tenant.OrganizationId || entry.DeletedAt.HasValue)
            throw new NotFoundException("TimeEntry", request.Id);

        if (entry.Status != "pending")
            throw new ConflictException("ENTRY_NOT_PENDING",
                "Solo las entradas en estado 'pending' pueden aprobarse.");

        entry.Approve();

        await _timeEntries.UpdateAsync(entry, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
