using FluentValidation;
using MediatR;
using Zentory.Application.Collaborators.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Collaborators.Commands;

public record UpdateCollaboratorCommand(
    Guid     Id,
    string   Name,
    string   Type,
    string   Status,
    string   Currency,
    string?  Email,
    string?  Phone,
    string?  Role,
    decimal? HourlyRate,
    decimal? MonthlyRate,
    string?  IdNumber,
    short?   ArlRiskLevel) : IRequest<CollaboratorDto>;

public sealed class UpdateCollaboratorCommandValidator : AbstractValidator<UpdateCollaboratorCommand>
{
    private static readonly string[] ValidTypes   = ["employee", "hourly_contractor", "fixed_contractor"];
    private static readonly string[] ValidStatuses = ["active", "inactive", "terminated"];

    public UpdateCollaboratorCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).NotEmpty().Must(t => ValidTypes.Contains(t))
            .WithMessage("Type must be employee, hourly_contractor or fixed_contractor.");
        RuleFor(x => x.Status).NotEmpty().Must(s => ValidStatuses.Contains(s))
            .WithMessage("Status must be active, inactive or terminated.");
        RuleFor(x => x.Email).EmailAddress().When(x => x.Email is not null);
    }
}

public sealed class UpdateCollaboratorCommandHandler : IRequestHandler<UpdateCollaboratorCommand, CollaboratorDto>
{
    private readonly ICollaboratorRepository _collaborators;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;

    public UpdateCollaboratorCommandHandler(
        ICollaboratorRepository collaborators,
        IUnitOfWork             uow,
        ITenantContext          tenant)
    {
        _collaborators = collaborators;
        _uow           = uow;
        _tenant        = tenant;
    }

    public async Task<CollaboratorDto> Handle(UpdateCollaboratorCommand request, CancellationToken cancellationToken)
    {
        var collaborator = await _collaborators.GetByIdAsync(request.Id, cancellationToken);
        if (collaborator is null || collaborator.OrganizationId != _tenant.OrganizationId || collaborator.DeletedAt.HasValue)
            throw new NotFoundException("Collaborator", request.Id);

        collaborator.Update(
            request.Name,
            request.Type,
            request.Status,
            request.Currency,
            request.Email,
            request.Phone,
            request.Role,
            request.HourlyRate,
            request.MonthlyRate,
            request.IdNumber,
            request.ArlRiskLevel);

        await _collaborators.UpdateAsync(collaborator, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return new CollaboratorDto(
            collaborator.Id,
            collaborator.Name,
            collaborator.Type,
            collaborator.Status,
            collaborator.Role,
            collaborator.Email,
            collaborator.Phone,
            collaborator.IdNumber,
            collaborator.HourlyRate,
            collaborator.MonthlyRate,
            collaborator.Currency,
            collaborator.PilaStatus,
            collaborator.PilaLastVerifiedPeriod,
            collaborator.ArlRiskLevel,
            collaborator.UserId,
            collaborator.CreatedAt,
            collaborator.UpdatedAt);
    }
}
