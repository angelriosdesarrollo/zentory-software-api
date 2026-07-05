using FluentValidation;
using MediatR;
using Zentory.Application.Clients.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;
using DomainValidationException = Zentory.Application.Exceptions.ValidationException;
using DomainValidationError     = Zentory.Application.Exceptions.ValidationError;

namespace Zentory.Application.Clients.Commands;

/// <summary>
/// PATCH — only non-null fields are applied. Name and ContactName are required when provided.
/// </summary>
public record UpdateClientCommand(
    Guid    Id,
    string  Name,
    string  ContactName,
    string? Email   = null,
    string? Phone   = null,
    string? City    = null,
    string? Address = null,
    string? Nit     = null,
    string? Notes   = null) : IRequest<ClientDto>;

public sealed class UpdateClientCommandValidator : AbstractValidator<UpdateClientCommand>
{
    public UpdateClientCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => x.Email is not null);
    }
}

public sealed class UpdateClientCommandHandler : IRequestHandler<UpdateClientCommand, ClientDto>
{
    private readonly IClientRepository _clients;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;

    public UpdateClientCommandHandler(
        IClientRepository clients,
        IUnitOfWork       uow,
        ITenantContext    tenant)
    {
        _clients = clients;
        _uow     = uow;
        _tenant  = tenant;
    }

    public async Task<ClientDto> Handle(UpdateClientCommand request, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(request.Id, cancellationToken);

        if (client is null || client.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Client", request.Id);

        client.Update(
            request.Name,
            request.ContactName,
            request.Email,
            request.Phone,
            request.City,
            request.Address,
            request.Nit,
            request.Notes);

        await _clients.UpdateAsync(client, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ClientDto(
            client.Id,
            client.Name,
            client.ContactName,
            client.Email,
            client.Phone,
            client.City,
            client.Address,
            client.Nit,
            client.Notes,
            ActiveProjects: 0,
            TotalBilled:    0m);
    }
}
