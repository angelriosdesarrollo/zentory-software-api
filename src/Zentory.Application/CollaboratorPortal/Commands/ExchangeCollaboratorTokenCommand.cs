using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.CollaboratorPortal.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Commands;

// Canje único para los tres tipos de entrada al portal: un magic link propio (Camino B),
// o un token de solicitud puntual de la empresa (PILA o cuenta de cobro — Camino A). Los
// tres terminan en el mismo lugar: una sesión de portal completa, multi-org por email.
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
        string email;
        Guid?  originCollaboratorId = null;
        CollaboratorHighlightDto? highlight = null;

        switch (request.Kind)
        {
            case "magic_link":
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
                var accessToken = await _db.CollaboratorAccessTokens
                    .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
                if (accessToken is null)
                    throw new NotFoundException("CollaboratorAccessToken", request.Token);
                if (!accessToken.IsValid())
                    throw new ConflictException("TOKEN_EXPIRED", "El enlace ha expirado o ya fue usado. Pide uno nuevo.");

                accessToken.MarkUsed();
                await _uow.SaveChangesAsync(cancellationToken);
                email = accessToken.Email;
                break;
            }
            case "pila_request":
            {
                if (!Guid.TryParse(request.Token, out var token))
                    throw new NotFoundException("PilaVerification", request.Token);
                var row = await _pilaVerifications.GetByTokenAsync(token, cancellationToken);
                if (row is null) throw new NotFoundException("PilaVerification", request.Token);
                if (!row.IsTokenValid())
                    throw new ConflictException("TOKEN_EXPIRED", "El enlace ha expirado. Pide que te reenvíen la solicitud.");

                var collaborator = await _collaborators.GetByIdAsync(row.CollaboratorId, cancellationToken);
                if (collaborator?.Email is null)
                    throw new ConflictException("NO_EMAIL", "Tu registro no tiene correo asociado — contacta a la empresa.");

                email                = collaborator.Email;
                originCollaboratorId = row.CollaboratorId;
                highlight            = new CollaboratorHighlightDto("pila", row.Period, row.Id);
                break;
            }
            case "payout_invoice_request":
            {
                if (!Guid.TryParse(request.Token, out var token))
                    throw new NotFoundException("CollaboratorPayoutInvoice", request.Token);
                var row = await _payoutInvoices.GetByTokenAsync(token, cancellationToken);
                if (row is null) throw new NotFoundException("CollaboratorPayoutInvoice", request.Token);
                if (!row.IsTokenValid())
                    throw new ConflictException("TOKEN_EXPIRED", "El enlace ha expirado. Pide que te reenvíen la solicitud.");

                var collaborator = await _collaborators.GetByIdAsync(row.CollaboratorId, cancellationToken);
                if (collaborator?.Email is null)
                    throw new ConflictException("NO_EMAIL", "Tu registro no tiene correo asociado — contacta a la empresa.");

                email                = collaborator.Email;
                originCollaboratorId = row.CollaboratorId;
                highlight            = new CollaboratorHighlightDto("payout_invoice", row.Period, row.Id);
                break;
            }
            default:
                // Inalcanzable: ExchangeCollaboratorTokenCommandValidator ya restringe Kind
                // a los tres valores manejados arriba.
                throw new ConflictException("INVALID_KIND", $"Kind '{request.Kind}' no es válido.");
        }

        var memberships = await _collaborators.ListByEmailAsync(email, cancellationToken);
        if (memberships.Count == 0)
            throw new NotFoundException("Collaborator", email);

        var activeCollaborator = originCollaboratorId.HasValue
            ? memberships.FirstOrDefault(m => m.Id == originCollaboratorId.Value) ?? memberships[0]
            : memberships[0]; // magic_link: arbitraria — el frontend pide elegir si hay más de una

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
            orgs[activeCollaborator.OrganizationId]);

        return new CollaboratorSessionDto(accessTokenJwt, SessionDurationSeconds, activeDto, membershipDtos, highlight);
    }
}
