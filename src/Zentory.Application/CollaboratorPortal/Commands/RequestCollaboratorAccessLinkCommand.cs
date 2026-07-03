using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Commands;

// Camino B del portal: el colaborador entra por su cuenta (nadie le pidió nada) y pide
// un magic link. Email + cédula es un filtro previo al envío, no autenticación en sí —
// la sesión real nace de hacer clic en el link que llega al correo.
public record RequestCollaboratorAccessLinkCommand(string Email, string? IdNumber) : IRequest;

public sealed class RequestCollaboratorAccessLinkCommandValidator
    : AbstractValidator<RequestCollaboratorAccessLinkCommand>
{
    public RequestCollaboratorAccessLinkCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public sealed class RequestCollaboratorAccessLinkCommandHandler : IRequestHandler<RequestCollaboratorAccessLinkCommand>
{
    private static readonly TimeSpan LinkValidity = TimeSpan.FromMinutes(30);

    private readonly ICollaboratorRepository _collaborators;
    private readonly IZentoryDbContext       _db;
    private readonly IUnitOfWork             _uow;
    private readonly IEmailService           _email;
    private readonly IApplicationSettings    _settings;

    public RequestCollaboratorAccessLinkCommandHandler(
        ICollaboratorRepository collaborators,
        IZentoryDbContext       db,
        IUnitOfWork             uow,
        IEmailService           email,
        IApplicationSettings    settings)
    {
        _collaborators = collaborators;
        _db            = db;
        _uow           = uow;
        _email         = email;
        _settings      = settings;
    }

    public async Task Handle(RequestCollaboratorAccessLinkCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var matches = await _collaborators.ListByEmailAsync(normalizedEmail, cancellationToken);

        // Nunca revelar si el email existe o no — mismo principio que un flujo de
        // "olvidé mi contraseña" bien implementado. Siempre se retorna sin error.
        if (matches.Count == 0) return;

        var anyHasIdNumber = matches.Any(m => !string.IsNullOrWhiteSpace(m.IdNumber));
        bool shouldSend;

        if (!anyHasIdNumber)
        {
            // Ningún registro de este email tiene cédula cargada — dato ausente, no un
            // mismatch. No bloquear a un colaborador por un campo que la empresa nunca
            // diligenció.
            shouldSend = true;
        }
        else
        {
            var normalizedInput = NormalizeIdNumber(request.IdNumber);
            shouldSend = normalizedInput is not null
                && matches.Any(m => NormalizeIdNumber(m.IdNumber) == normalizedInput);
        }

        if (!shouldSend) return;

        var rawToken  = GenerateRawToken();
        var tokenHash = HashToken(rawToken);
        var expiresAt = DateTime.UtcNow.Add(LinkValidity);

        var accessToken = CollaboratorAccessToken.Create(normalizedEmail, tokenHash, expiresAt);
        await _db.CollaboratorAccessTokens.AddAsync(accessToken, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var magicLinkUrl = $"{_settings.BaseUrl}/portal/entrar?token={rawToken}&kind=magic_link";
        await _email.SendCollaboratorPortalAccessEmailAsync(
            normalizedEmail, matches[0].Name, magicLinkUrl, expiresAt, cancellationToken);
    }

    private static string? NormalizeIdNumber(string? idNumber)
        => string.IsNullOrWhiteSpace(idNumber)
            ? null
            : idNumber.Replace(".", "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

    private static string GenerateRawToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string HashToken(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
