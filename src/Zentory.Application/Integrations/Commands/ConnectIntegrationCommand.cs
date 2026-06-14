using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Integrations.DTOs;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Integrations.Commands;

public record ConnectIntegrationCommand(
    string    IntegrationId,
    string?   ConnectedAs          = null,
    string?   ExternalAccountId    = null,
    string?   ExternalWorkspaceId  = null,
    string?   AccessToken          = null,
    string?   RefreshToken         = null,
    DateTime? TokenExpiresAt       = null,
    string?   Scopes               = null,
    string?   Metadata             = null
) : IRequest<IntegrationDto>;

public sealed class ConnectIntegrationCommandValidator : AbstractValidator<ConnectIntegrationCommand>
{
    public ConnectIntegrationCommandValidator()
    {
        RuleFor(x => x.IntegrationId).NotEmpty().MaximumLength(100);
    }
}

public sealed class ConnectIntegrationCommandHandler : IRequestHandler<ConnectIntegrationCommand, IntegrationDto>
{
    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;

    public ConnectIntegrationCommandHandler(IZentoryDbContext db, IUnitOfWork uow, ITenantContext tenant)
    {
        _db     = db;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task<IntegrationDto> Handle(ConnectIntegrationCommand request, CancellationToken cancellationToken)
    {
        var catalogEntry = await _db.IntegrationCatalog
            .FirstOrDefaultAsync(i => i.Id == request.IntegrationId && i.IsEnabled && !i.IsHidden, cancellationToken);

        if (catalogEntry is null)
            throw new NotFoundException("Integration", request.IntegrationId);

        var connection = await _db.OrganizationIntegrations
            .FirstOrDefaultAsync(
                oi => oi.OrganizationId == _tenant.OrganizationId && oi.IntegrationId == request.IntegrationId,
                cancellationToken);

        if (connection is null)
        {
            connection = OrganizationIntegration.Create(_tenant.OrganizationId, request.IntegrationId);
            await _db.OrganizationIntegrations.AddAsync(connection, cancellationToken);
        }

        connection.Connect(
            _tenant.UserId,
            request.ConnectedAs,
            request.ExternalAccountId,
            request.ExternalWorkspaceId,
            request.AccessToken,   // TODO: encrypt before storing
            request.RefreshToken,  // TODO: encrypt before storing
            request.TokenExpiresAt,
            request.Scopes,
            request.Metadata);

        await _uow.SaveChangesAsync(cancellationToken);

        return new IntegrationDto(
            catalogEntry.Id,
            catalogEntry.Name,
            catalogEntry.Description,
            catalogEntry.IsEnabled,
            true,
            connection.ConnectedAs);
    }
}
