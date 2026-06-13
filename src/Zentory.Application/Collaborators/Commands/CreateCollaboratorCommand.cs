using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Collaborators.Commands;

public record CreateCollaboratorCommand(
    string   Name,
    string   Type,
    string   Currency     = "COP",
    string?  Email        = null,
    string?  Phone        = null,
    string?  Role         = null,
    decimal? HourlyRate   = null,
    decimal? MonthlyRate  = null,
    string?  IdNumber     = null,
    short?   ArlRiskLevel = null) : IRequest<Guid>;

public sealed class CreateCollaboratorCommandValidator : AbstractValidator<CreateCollaboratorCommand>
{
    private static readonly string[] ValidTypes = ["employee", "hourly_contractor", "fixed_contractor"];

    public CreateCollaboratorCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).NotEmpty().Must(t => ValidTypes.Contains(t))
            .WithMessage("Type must be employee, hourly_contractor or fixed_contractor.");
        RuleFor(x => x.Email).EmailAddress().When(x => x.Email is not null);
        RuleFor(x => x.HourlyRate).GreaterThan(0).When(x => x.HourlyRate.HasValue);
        RuleFor(x => x.MonthlyRate).GreaterThan(0).When(x => x.MonthlyRate.HasValue);
    }
}

public sealed class CreateCollaboratorCommandHandler : IRequestHandler<CreateCollaboratorCommand, Guid>
{
    private readonly ICollaboratorRepository _collaborators;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;

    public CreateCollaboratorCommandHandler(
        ICollaboratorRepository collaborators,
        IUnitOfWork             uow,
        ITenantContext          tenant)
    {
        _collaborators = collaborators;
        _uow           = uow;
        _tenant        = tenant;
    }

    public async Task<Guid> Handle(CreateCollaboratorCommand request, CancellationToken cancellationToken)
    {
        var collaborator = Collaborator.Create(
            _tenant.OrganizationId,
            request.Name,
            request.Type,
            request.Currency,
            email:        request.Email,
            phone:        request.Phone,
            role:         request.Role,
            hourlyRate:   request.HourlyRate,
            monthlyRate:  request.MonthlyRate,
            idNumber:     request.IdNumber,
            arlRiskLevel: request.ArlRiskLevel);

        await _collaborators.AddAsync(collaborator, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return collaborator.Id;
    }
}
