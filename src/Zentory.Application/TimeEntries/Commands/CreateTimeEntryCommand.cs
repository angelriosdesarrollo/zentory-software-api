using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.TimeEntries.Commands;

public record CreateTimeEntryCommand(
    Guid      ProjectId,
    DateOnly  Date,
    decimal   Hours,
    decimal   RateCost,
    string    Currency       = "COP",
    bool      Billable       = true,
    decimal?  RateBilled     = null,
    string?   Description    = null,
    Guid?     CollaboratorId = null) : IRequest<Guid>;

public sealed class CreateTimeEntryCommandValidator : AbstractValidator<CreateTimeEntryCommand>
{
    public CreateTimeEntryCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Hours).GreaterThan(0).LessThanOrEqualTo(24);
        RuleFor(x => x.RateCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RateBilled).GreaterThanOrEqualTo(0).When(x => x.RateBilled.HasValue);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
    }
}

public sealed class CreateTimeEntryCommandHandler : IRequestHandler<CreateTimeEntryCommand, Guid>
{
    private readonly ITimeEntryRepository  _timeEntries;
    private readonly IProjectRepository    _projects;
    private readonly IUnitOfWork           _uow;
    private readonly ITenantContext        _tenant;

    public CreateTimeEntryCommandHandler(
        ITimeEntryRepository  timeEntries,
        IProjectRepository    projects,
        IUnitOfWork           uow,
        ITenantContext        tenant)
    {
        _timeEntries = timeEntries;
        _projects    = projects;
        _uow         = uow;
        _tenant      = tenant;
    }

    public async Task<Guid> Handle(CreateTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId || project.IsDeleted)
            throw new NotFoundException("Project", request.ProjectId);

        var entry = TimeEntry.Create(
            _tenant.OrganizationId,
            request.ProjectId,
            request.Date,
            request.Hours,
            request.RateCost,
            request.Currency,
            request.Billable,
            request.RateBilled,
            request.Description,
            request.CollaboratorId,
            createdBy: _tenant.UserId);

        await _timeEntries.AddAsync(entry, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return entry.Id;
    }
}
