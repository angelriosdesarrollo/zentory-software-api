using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using DomainValidationException = Zentory.Application.Exceptions.ValidationException;
using DomainValidationError     = Zentory.Application.Exceptions.ValidationError;

namespace Zentory.Application.Projects.Commands;

public record CreateProjectCommand(
    string    Name,
    Guid      ClientId,
    string    BillingType,
    decimal   ContractValue,
    string    Currency     = "USD",
    int       HoursTotal   = 0,
    DateTime? StartDate    = null,
    DateTime? EndDate      = null,
    Guid?     ProposalId   = null) : IRequest<Guid>;

public sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.BillingType).NotEmpty()
            .Must(v => Enum.TryParse<BillingType>(v, ignoreCase: true, out _))
            .WithMessage("BillingType must be Hourly, Milestone or FixedPrice.");
        RuleFor(x => x.ContractValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.HoursTotal).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateProjectCommandHandler : IRequestHandler<CreateProjectCommand, Guid>
{
    private const int FreeProjectLimit = 3;

    private readonly IProjectRepository _projects;
    private readonly IClientRepository  _clients;
    private readonly IUnitOfWork        _uow;
    private readonly ITenantContext     _tenant;
    private readonly IActivityLogService _activityLog;

    public CreateProjectCommandHandler(
        IProjectRepository  projects,
        IClientRepository   clients,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog)
    {
        _projects    = projects;
        _clients     = clients;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
    }

    public async Task<Guid> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        if (_tenant.Plan == Plan.Free)
        {
            var count = await _projects.CountAsync(_tenant.OrganizationId, cancellationToken);
            if (count >= FreeProjectLimit)
                throw new DomainValidationException([
                    new DomainValidationError(
                        "plan",
                        $"El plan Free permite máximo {FreeProjectLimit} proyectos. Actualiza a Pro para proyectos ilimitados.")
                ]);
        }

        var client = await _clients.GetByIdAsync(request.ClientId, cancellationToken);
        if (client is null || client.OrganizationId != _tenant.OrganizationId)
            throw new Exceptions.NotFoundException("Client", request.ClientId);

        var billingType = Enum.Parse<BillingType>(request.BillingType, ignoreCase: true);

        var project = Project.Create(
            _tenant.OrganizationId,
            request.ClientId,
            request.Name,
            billingType,
            request.ContractValue,
            request.Currency,
            request.HoursTotal,
            request.StartDate,
            request.EndDate,
            request.ProposalId);

        await _projects.AddAsync(project, cancellationToken);
        await _activityLog.LogAsync(
            entityType: "Project",
            entityId:   project.Id,
            action:     "Creó el proyecto",
            entityCode: project.Name,
            ct:         cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return project.Id;
    }
}
