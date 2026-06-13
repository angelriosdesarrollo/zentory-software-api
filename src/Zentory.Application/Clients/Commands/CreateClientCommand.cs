using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using DomainValidationException = Zentory.Application.Exceptions.ValidationException;
using DomainValidationError     = Zentory.Application.Exceptions.ValidationError;
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
    private const int FreeClientLimit = 2;

    private readonly IClientRepository _clients;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;

    public CreateClientCommandHandler(
        IClientRepository clients,
        IUnitOfWork       uow,
        ITenantContext    tenant)
    {
        _clients = clients;
        _uow     = uow;
        _tenant  = tenant;
    }

    public async Task<Guid> Handle(CreateClientCommand request, CancellationToken cancellationToken)
    {
        if (_tenant.Plan == Plan.Free)
        {
            var count = await _clients.CountAsync(_tenant.OrganizationId, cancellationToken);
            if (count >= FreeClientLimit)
            {
                throw new DomainValidationException([
                    new DomainValidationError(
                        "plan",
                        $"El plan Free permite máximo {FreeClientLimit} clientes. Actualiza a Pro para tener clientes ilimitados.")
                ]);
            }
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
        await _uow.SaveChangesAsync(cancellationToken);

        return client.Id;
    }
}
