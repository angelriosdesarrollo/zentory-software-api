using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Clients.Commands;

public record CreateClientCommand(
    string  Name,
    string  ContactName,
    string? Email = null,
    string? Phone = null,
    string? City  = null,
    string? Nit   = null,
    string? Notes = null) : IRequest<Guid>;

public sealed class CreateClientCommandValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => x.Email is not null);
    }
}

public sealed class CreateClientCommandHandler : IRequestHandler<CreateClientCommand, Guid>
{
    private readonly IClientRepository   _clients;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;
    private readonly IPlanLimitService   _planLimits;

    public CreateClientCommandHandler(
        IClientRepository   clients,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog,
        IPlanLimitService   planLimits)
    {
        _clients     = clients;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
        _planLimits  = planLimits;
    }

    public async Task<Guid> Handle(CreateClientCommand request, CancellationToken cancellationToken)
    {
        var limit = await _planLimits.GetLimitAsync(
            _tenant.Plan,
            _tenant.LegalType,
            PlanLimits.FeatureKeys.MaxClients,
            cancellationToken);

        if (limit.HasValue)
        {
            var count = await _clients.CountAsync(_tenant.OrganizationId, cancellationToken);
            if (count >= limit.Value)
                throw new QuotaExceededException(
                    PlanLimits.FeatureKeys.MaxClients,
                    limit.Value,
                    count);
        }

        var client = Client.Create(
            _tenant.OrganizationId,
            request.Name,
            request.ContactName,
            request.Email,
            request.Phone,
            request.City,
            request.Nit,
            request.Notes);

        await _clients.AddAsync(client, cancellationToken);

        await _activityLog.LogAsync(
            "Client",
            client.Id,
            $"Creó el cliente",
            client.Name,
            ct: cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return client.Id;
    }
}
