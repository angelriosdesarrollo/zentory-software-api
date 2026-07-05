using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

/// <summary>
/// Cambia el estado de un entregable. Al aprobar, "RegisteredByCollaboratorId" identifica
/// al colaborador interno de la organización que registra la aprobación — no es la
/// aprobación legal del cliente (eso vive en el portal público, fuera de este ciclo).
/// </summary>
public record ChangeProjectDeliverableStatusCommand(
    Guid   ProjectId,
    Guid   DeliverableId,
    string Status,
    Guid?  RegisteredByCollaboratorId = null) : IRequest;
// Status: "PENDING" | "IN_REVIEW" | "APPROVED" | "REJECTED"

public sealed class ChangeProjectDeliverableStatusCommandValidator
    : AbstractValidator<ChangeProjectDeliverableStatusCommand>
{
    private static readonly string[] ValidStatuses = ["PENDING", "IN_REVIEW", "APPROVED", "REJECTED"];

    public ChangeProjectDeliverableStatusCommandValidator()
    {
        RuleFor(x => x.Status).Must(ValidStatuses.Contains)
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}.");
        RuleFor(x => x.RegisteredByCollaboratorId)
            .NotNull()
            .When(x => x.Status == "APPROVED")
            .WithMessage("RegisteredByCollaboratorId is required to approve a deliverable.");
    }
}

public sealed class ChangeProjectDeliverableStatusCommandHandler
    : IRequestHandler<ChangeProjectDeliverableStatusCommand>
{
    private readonly IZentoryDbContext      _db;
    private readonly ICollaboratorRepository _collaborators;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;

    public ChangeProjectDeliverableStatusCommandHandler(
        IZentoryDbContext db, ICollaboratorRepository collaborators, IUnitOfWork uow, ITenantContext tenant)
    {
        _db            = db;
        _collaborators = collaborators;
        _uow           = uow;
        _tenant        = tenant;
    }

    public async Task Handle(ChangeProjectDeliverableStatusCommand request, CancellationToken cancellationToken)
    {
        var deliverable = await _db.ProjectDeliverables.FirstOrDefaultAsync(
            d => d.Id == request.DeliverableId && d.ProjectId == request.ProjectId
              && d.OrganizationId == _tenant.OrganizationId,
            cancellationToken);

        if (deliverable is null)
            throw new NotFoundException("ProjectDeliverable", request.DeliverableId);

        string? registeredBy = null;
        if (request.Status == "APPROVED")
        {
            var collaborator = await _collaborators.GetByIdAsync(request.RegisteredByCollaboratorId!.Value, cancellationToken);
            if (collaborator is null || collaborator.OrganizationId != _tenant.OrganizationId)
                throw new NotFoundException("Collaborator", request.RegisteredByCollaboratorId!.Value);

            registeredBy = collaborator.Name;
        }

        deliverable.UpdateStatus(request.Status, registeredBy);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
