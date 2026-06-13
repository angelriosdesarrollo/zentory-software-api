using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.TimeEntries.Commands;

public record DeleteTimeEntryCommand(Guid Id) : IRequest;

public sealed class DeleteTimeEntryCommandHandler : IRequestHandler<DeleteTimeEntryCommand>
{
    private readonly ITimeEntryRepository _timeEntries;
    private readonly IUnitOfWork          _uow;
    private readonly ITenantContext       _tenant;

    public DeleteTimeEntryCommandHandler(
        ITimeEntryRepository timeEntries,
        IUnitOfWork          uow,
        ITenantContext       tenant)
    {
        _timeEntries = timeEntries;
        _uow         = uow;
        _tenant      = tenant;
    }

    public async Task Handle(DeleteTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await _timeEntries.GetByIdAsync(request.Id, cancellationToken);
        if (entry is null || entry.OrganizationId != _tenant.OrganizationId || entry.DeletedAt.HasValue)
            throw new NotFoundException("TimeEntry", request.Id);

        if (entry.Status == "billed")
            throw new ConflictException("TIME_ENTRY_BILLED", "No se puede eliminar una entrada ya facturada.");

        entry.SoftDelete();

        await _timeEntries.UpdateAsync(entry, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
