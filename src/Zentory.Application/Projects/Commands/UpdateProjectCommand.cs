using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.DTOs;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

public record UpdateProjectCommand(
    Guid      Id,
    string    Name,
    string    BillingType,
    decimal   ContractValue,
    string    Currency,
    int       HoursTotal,
    DateTime? StartDate,
    DateTime? EndDate,
    string?   Type = null) : IRequest<ProjectDto>;

public sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BillingType).NotEmpty()
            .Must(v => Enum.TryParse<BillingType>(v, ignoreCase: true, out _))
            .WithMessage("BillingType must be Hourly, Milestone or FixedPrice.");
        RuleFor(x => x.ContractValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.HoursTotal).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Type)
            .Must(v => string.IsNullOrWhiteSpace(v) || WorkType.All.Contains(v))
            .WithMessage($"Type must be one of: {string.Join(", ", WorkType.All)}.");
    }
}

public sealed class UpdateProjectCommandHandler : IRequestHandler<UpdateProjectCommand, ProjectDto>
{
    private readonly IProjectRepository     _projects;
    private readonly IClientRepository      _clients;
    private readonly IInvoiceRepository     _invoices;
    private readonly IProjectTaskRepository _tasks;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;

    public UpdateProjectCommandHandler(
        IProjectRepository     projects,
        IClientRepository      clients,
        IInvoiceRepository     invoices,
        IProjectTaskRepository tasks,
        IUnitOfWork            uow,
        ITenantContext         tenant)
    {
        _projects = projects;
        _clients  = clients;
        _invoices = invoices;
        _tasks    = tasks;
        _uow      = uow;
        _tenant   = tenant;
    }

    public async Task<ProjectDto> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.Id, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId || project.IsDeleted)
            throw new NotFoundException("Project", request.Id);

        var client = await _clients.GetByIdAsync(project.ClientId, cancellationToken);
        var billingType = Enum.Parse<BillingType>(request.BillingType, ignoreCase: true);

        project.Update(
            request.Name,
            billingType,
            request.ContractValue,
            request.Currency,
            request.HoursTotal,
            request.StartDate,
            request.EndDate,
            request.Type ?? project.Type);

        await _projects.UpdateAsync(project, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var amountPaidByProject = await _invoices.GetAmountPaidByProjectIdsAsync(
            [project.Id], _tenant.OrganizationId, cancellationToken);
        var taskCountsByProject = await _tasks.GetTaskCountsByProjectIdsAsync(
            [project.Id], _tenant.OrganizationId, cancellationToken);

        amountPaidByProject.TryGetValue(project.Id, out var amountPaid);
        taskCountsByProject.TryGetValue(project.Id, out var taskCounts);

        var health = ProjectHealthHelper.Compute(new ProjectHealthHelper.HealthInput(
            HoursUsed:     project.HoursUsed,
            HoursTotal:    project.HoursTotal,
            ContractValue: project.ContractValue,
            AmountPaid:    amountPaid,
            StartDate:     project.StartDate,
            EndDate:       project.EndDate,
            TasksTotal:    taskCounts.Total,
            TasksDone:     taskCounts.Done));

        return new ProjectDto(
            Id:            project.Id,
            Code:          $"PRJ-{project.Id.ToString("N")[..8].ToUpperInvariant()}",
            Name:          project.Name,
            ClientId:      project.ClientId,
            ClientName:    client?.Name ?? string.Empty,
            Status:        project.Status.ToString(),
            BillingType:   project.BillingType.ToString(),
            ContractValue: project.ContractValue,
            Currency:      project.Currency,
            HoursTotal:    project.HoursTotal,
            HoursUsed:     project.HoursUsed,
            Progress:      health.Progress,
            HealthScore:   health.HealthScore,
            HealthStatus:  health.HealthStatus,
            Alert:         health.Alert,
            StartDate:     project.StartDate,
            EndDate:       project.EndDate,
            ProposalId:    project.ProposalId,
            CreatedAt:     project.CreatedAt,
            UpdatedAt:     project.UpdatedAt,
            Type:          project.Type);
    }
}
