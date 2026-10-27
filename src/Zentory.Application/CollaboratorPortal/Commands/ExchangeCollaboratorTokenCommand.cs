using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.CollaboratorPortal.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Commands;

// Canje único para los tres tipos de entrada al portal: un magic link propio (Camino B),
// o un token de solicitud puntual de la empresa (PILA o cuenta de cobro — Camino A).
// Solo magic_link abre una sesión multi-org por email: es el único que prueba control del
// buzón. Un token de solicitud solo prueba que alguien recibió ese enlace, así que su sesión
// queda limitada al colaborador del token (auth_method=request), aunque su email coincida
// con colaboradores de otras organizaciones.
public sealed record ExchangeCollaboratorTokenCommand(string Kind, string Token) : IRequest<CollaboratorSessionDto>;

public sealed class ExchangeCollaboratorTokenCommandValidator : AbstractValidator<ExchangeCollaboratorTokenCommand>
{
    public ExchangeCollaboratorTokenCommandValidator()
    {
        RuleFor(x => x.Kind).Must(k => k is "magic_link" or "pila_request" or "payout_invoice_request")
            .WithMessage("Kind must be 'magic_link', 'pila_request' or 'payout_invoice_request'.");
        RuleFor(x => x.Token).NotEmpty();
    }
}

public sealed class ExchangeCollaboratorTokenCommandHandler
    : IRequestHandler<ExchangeCollaboratorTokenCommand, CollaboratorSessionDto>
{
    private const int SessionDurationSeconds = 7 * 24 * 3600;

    private readonly IZentoryDbContext            _db;
    private readonly ICollaboratorRepository      _collaborators;
    private readonly IOrganizationRepository      _organizations;
    private readonly IPilaVerificationRepository  _pilaVerifications;
    private readonly ICollaboratorPayoutInvoiceRepository _payoutInvoices;
    private readonly ICollaboratorJwtService      _jwt;
    private readonly IUnitOfWork                  _uow;

    public ExchangeCollaboratorTokenCommandHandler(
        IZentoryDbContext            db,
        ICollaboratorRepository      collaborators,
        IOrganizationRepository      organizations,
        IPilaVerificationRepository  pilaVerifications,
        ICollaboratorPayoutInvoiceRepository payoutInvoices,
        ICollaboratorJwtService      jwt,
        IUnitOfWork                  uow)
    {
        _db                = db;
        _collaborators     = collaborators;
        _organizations     = organizations;
        _pilaVerifications = pilaVerifications;
        _payoutInvoices    = payoutInvoices;
        _jwt               = jwt;
        _uow               = uow;
    }

    public async Task<CollaboratorSessionDto> Handle(
        ExchangeCollaboratorTokenCommand request, CancellationToken cancellationToken)
    {
        switch (request.Kind)
        {
            case "magic_link":
                return await ExchangeMagicLinkAsync(request.Token, cancellationToken);
            case "pila_request":
            {
                if (!Guid.TryParse(request.Token, out var token))
                    throw new NotFoundException("PilaVerification", request.Token);
                var row = await _pilaVerifications.GetByTokenAsync(token, cancellationToken);
                if (row is null || !await IsLiveOrganizationAsync(row.OrganizationId, cancellationToken))
                    throw new NotFoundException("PilaVerification", request.Token);
                if (!row.IsTokenValid())
                    throw new ConflictException("TOKEN_EXPIRED", "El enlace ha expirado. Pide que te reenvíen la solicitud.");

                return await ExchangeRequestAsync(
                    row.CollaboratorId, new CollaboratorHighlightDto("pila", row.Period, row.Id), cancellationToken);
            }
            case "payout_invoice_request":
            {
                if (!Guid.TryParse(request.Token, out var token))
                    throw new NotFoundException("CollaboratorPayoutInvoice", request.Token);
                var row = await _payoutInvoices.GetByTokenAsync(token, cancellationToken);
                if (row is null || !await IsLiveOrganizationAsync(row.OrganizationId, cancellationToken))
                    throw new NotFoundException("CollaboratorPayoutInvoice", request.Token);
                if (!row.IsTokenValid())
                    throw new ConflictException("TOKEN_EXPIRED", "El enlace ha expirado. Pide que te reenvíen la solicitud.");

                return await ExchangeRequestAsync(
                    row.CollaboratorId, new CollaboratorHighlightDto("payout_invoice", row.Period, row.Id), cancellationToken);
            }
            default:
                // Inalcanzable: ExchangeCollaboratorTokenCommandValidator ya restringe Kind
                // a los tres valores manejados arriba.
                throw new ConflictException("INVALID_KIND", $"Kind '{request.Kind}' no es válido.");
        }
    }

    private async Task<CollaboratorSessionDto> ExchangeMagicLinkAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var accessToken = await _db.CollaboratorAccessTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (accessToken is null)
            throw new NotFoundException("CollaboratorAccessToken", rawToken);

        // ListByEmailAsync ya excluye las organizaciones eliminadas (PLAN-0018 RF-65 d). Se lee antes de la vigencia y de
        // consumir el enlace: sin membresías vigentes responde como un token inexistente, sin efecto (RF-17 rev. 4).
        var email = accessToken.Email;
        var memberships = await _collaborators.ListByEmailAsync(email, cancellationToken);
        if (memberships.Count == 0)
            throw new NotFoundException("CollaboratorAccessToken", rawToken);

        if (!accessToken.IsValid())
            throw new ConflictException("TOKEN_EXPIRED", "El enlace ha expirado o ya fue usado. Pide uno nuevo.");

        accessToken.MarkUsed();
        await _uow.SaveChangesAsync(cancellationToken);

        // Arbitraria: el frontend pide elegir si hay más de una membresía.
        var activeCollaborator = memberships[0];

        return await BuildSessionAsync(
            email, memberships, activeCollaborator, CollaboratorAuthMethod.MagicLink, highlight: null, cancellationToken);
    }

    // Un token de solicitud (PILA o cuenta de cobro) de una organización eliminada responde como un token inexistente, y
    // antes de mirar su vigencia: ni sesión ni un TOKEN_EXPIRED que delate que existe (PLAN-0018 RF-17 rev. 4; SR-0018.26 c;
    // D-31). El repositorio ya no ve las eliminadas (filtro global); DeletedAt se comprueba igual, para que la regla no
    // dependa de cómo lea el repositorio.
    private async Task<bool> IsLiveOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
        => await _organizations.GetByIdAsync(organizationId, cancellationToken) is { DeletedAt: null };

    // Sin ListByEmailAsync a propósito: el email del colaborador lo edita la empresa, así que
    // buscar por email permitiría que un token de la org A abra sesión en colaboradores de B.
    private async Task<CollaboratorSessionDto> ExchangeRequestAsync(
        Guid collaboratorId, CollaboratorHighlightDto highlight, CancellationToken cancellationToken)
    {
        var collaborator = await _collaborators.GetByIdAsync(collaboratorId, cancellationToken);
        // Mismo filtro que aplicaba ListByEmailAsync: solo colaboradores activos y no borrados.
        if (collaborator is null || collaborator.Status != "active" || collaborator.DeletedAt is not null)
            throw new NotFoundException("Collaborator", collaboratorId);
        // El email sigue siendo el sub del JWT, así que sin email no hay sesión posible.
        if (collaborator.Email is null)
            throw new ConflictException("NO_EMAIL", "Tu registro no tiene correo asociado — contacta a la empresa.");

        return await BuildSessionAsync(
            collaborator.Email, [collaborator], collaborator, CollaboratorAuthMethod.Request, highlight, cancellationToken);
    }

    private async Task<CollaboratorSessionDto> BuildSessionAsync(
        string                       email,
        IReadOnlyList<Collaborator>  memberships,
        Collaborator                 activeCollaborator,
        string                       authMethod,
        CollaboratorHighlightDto?    highlight,
        CancellationToken            cancellationToken)
    {
        var orgIds  = memberships.Select(m => m.OrganizationId).Distinct().ToList();
        var orgs    = new Dictionary<Guid, string>();
        foreach (var orgId in orgIds)
        {
            var org = await _organizations.GetByIdAsync(orgId, cancellationToken);
            orgs[orgId] = org?.Name ?? string.Empty;
        }

        var membershipDtos = memberships
            .Select(m => new CollaboratorMembershipDto(m.Id, m.OrganizationId, orgs[m.OrganizationId], m.Name))
            .ToList();
        var activeDto = membershipDtos.First(m => m.CollaboratorId == activeCollaborator.Id);

        var accessTokenJwt = _jwt.GenerateSessionToken(
            email,
            memberships.Select(m => m.Id).ToList(),
            activeCollaborator.Id,
            activeCollaborator.OrganizationId,
            orgs[activeCollaborator.OrganizationId],
            authMethod);

        return new CollaboratorSessionDto(accessTokenJwt, SessionDurationSeconds, activeDto, membershipDtos, highlight);
    }
}
