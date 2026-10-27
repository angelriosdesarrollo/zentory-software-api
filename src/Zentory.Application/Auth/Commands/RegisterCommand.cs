using FluentValidation;
using MediatR;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Common;
using Zentory.Domain.Repositories;
using OrgRole       = Zentory.Domain.Constants.OrgRole;

namespace Zentory.Application.Auth.Commands;

// POST /auth/register (PLAN-0018 RF-20, RF-22, RF-25, RF-27; D-11, D-14, D-16). El email se normaliza y uno repetido con
// otras mayúsculas responde 409 EMAIL_ALREADY_EXISTS. La cuenta nace sin verificar (EmailVerifiedAt null) y con su primer
// EmailVerificationToken, que se guarda con el usuario; el correo sale por IAuthEmailQueue después de guardar, con la URL
// de App:BaseUrl. Inicia la sesión del usuario (RF-90 b).
public record RegisterCommand(
    string  Email,
    string  Password,
    string  FirstName,
    string  LastName,
    string  OrgName,
    string  LegalType,
    bool    AcceptedTerms,
    string  Country     = "CO",
    string? TermsVersion = null) : IRequest<AuthTokenDto>;

public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthTokenDto>
{
    private readonly IUserRepository         _users;
    private readonly IOrganizationRepository _organizations;
    private readonly IUnitOfWork             _uow;
    private readonly IZentoryDbContext       _db;
    private readonly IAuthSessionIssuer      _sessions;
    private readonly IAuthEmailQueue         _emails;
    private readonly IApplicationSettings    _settings;
    private readonly TimeProvider            _time;

    public RegisterCommandHandler(
        IUserRepository         users,
        IOrganizationRepository organizations,
        IUnitOfWork             uow,
        IZentoryDbContext       db,
        IAuthSessionIssuer      sessions,
        IAuthEmailQueue         emails,
        IApplicationSettings    settings,
        TimeProvider            time)
    {
        _users         = users;
        _organizations = organizations;
        _uow           = uow;
        _db            = db;
        _sessions      = sessions;
        _emails        = emails;
        _settings      = settings;
        _time          = time;
    }

    public async Task<AuthTokenDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        // Email normalizado al buscar; User.Create lo guarda igual (RF-27).
        var existing = await _users.GetByEmailAsync(EmailAddress.Normalize(request.Email), cancellationToken);
        if (existing is not null)
            throw new ConflictException("EMAIL_ALREADY_EXISTS", "El correo electrónico ya está registrado.");

        var org = global::Zentory.Domain.Entities.Organization.Create(request.OrgName, request.LegalType, request.Country);
        await _organizations.AddAsync(org, cancellationToken);

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var termsAcceptedAt = DateTime.UtcNow;
        var termsAcceptedVersion = request.TermsVersion ?? Zentory.Domain.Constants.LegalDocuments.CurrentTermsVersion;
        var user = User.Create(
            request.Email,
            request.FirstName,
            request.LastName,
            role: OrgRole.Owner,
            passwordHash: passwordHash,
            termsAcceptedAt: termsAcceptedAt,
            termsAcceptedVersion: termsAcceptedVersion);

        await _users.AddAsync(user, cancellationToken);

        org.SetOwner(user.UserId);
        _db.OrganizationMembers.Add(OrganizationMember.Create(org.OrganizationId, user.UserId, OrgRole.Owner));

        // Sin suscripción ni cliente de cobro (PLAN-0018 DEC-0101, CA-44, RF-73): el owner nuevo es free porque no tiene
        // suscripción vigente. El plan solo lo escribe seed assign-plan.

        // Primer token de verificación, para el email ya normalizado del usuario (RF-20). Es el primero: no hay
        // anteriores que invalidar. Solo se guarda su hash.
        var issued = OneTimeToken.Issue();
        var verification = EmailVerificationToken.Issue(
            user.UserId, user.Email, issued.Hash, _time.GetUtcNow().UtcDateTime);
        _db.EmailVerificationTokens.Add(verification);

        // La sesión se emite sobre lo ya guardado: IAuthSessionIssuer lee las membresías de la base (PLAN-0018 D-16).
        await _uow.SaveChangesAsync(cancellationToken);

        // Se encola después de guardar: un registro que falla no envía un enlace que no sirve.
        EmailVerificationEmail.Enqueue(_emails, _settings, user, issued.RawToken, verification.ExpiresAt);

        return await _sessions.StartSessionAsync(user, org.OrganizationId, cancellationToken);
    }
}

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    // Una sola casilla cubre Términos y Política de Privacidad (LegalDocuments). La reutiliza la cuenta nueva de
    // POST /auth/invitations/accept (DEC-0110).
    public const string TermsRequiredMessage =
        "Debes aceptar los Términos y Condiciones y la Política de Privacidad para crear una cuenta.";

    public RegisterCommandValidator()
    {
        // PasswordPolicy (PLAN-0018 RF-26; SR-0018.14): con el tope de 72 bytes, BCrypt no trunca la contraseña.
        RuleFor(x => x.Password).ApplyPasswordPolicy();

        RuleFor(x => x.AcceptedTerms)
            .Equal(true)
            .WithMessage(TermsRequiredMessage);
    }
}
