using MediatR;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Common;
using Zentory.Domain.Repositories;
using LegalTypeConstants = Zentory.Domain.Constants.LegalType;

namespace Zentory.Application.Auth.Commands;

public record GoogleLoginCommand(string IdToken) : IRequest<AuthTokenDto>;

// 409 de la vinculación (PLAN-0018 RF-28 d, SR-0018.15): la cuenta con ese email ya está vinculada a otra identidad de
// Google. El cuerpo es fijo: no repite el email ni ningún GoogleId.
public static class GoogleLoginErrors
{
    public const string AccountConflictCode    = "GOOGLE_ACCOUNT_CONFLICT";
    public const string AccountConflictMessage = "Esta cuenta ya está vinculada a otra cuenta de Google.";
}

public sealed class GoogleLoginCommandHandler : IRequestHandler<GoogleLoginCommand, AuthTokenDto>
{
    private readonly IUserRepository         _users;
    private readonly IOrganizationRepository _organizations;
    private readonly IGoogleTokenValidator   _google;
    private readonly IUnitOfWork             _uow;
    private readonly IZentoryDbContext       _db;
    private readonly IAuthSessionIssuer      _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;

    public GoogleLoginCommandHandler(
        IUserRepository         users,
        IOrganizationRepository organizations,
        IGoogleTokenValidator   google,
        IUnitOfWork             uow,
        IZentoryDbContext       db,
        IAuthSessionIssuer      sessions,
        IRefreshTokenRepository refreshTokens)
    {
        _users         = users;
        _organizations = organizations;
        _google        = google;
        _uow           = uow;
        _db            = db;
        _sessions      = sessions;
        _refreshTokens = refreshTokens;
    }

    public async Task<AuthTokenDto> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await _google.ValidateAsync(request.IdToken, cancellationToken);

        // RF-28 a: sin email_verified de Google no se busca, ni se crea, ni se vincula nada. 403 y no 401, porque el
        // cliente trata todo 401 como sesión caducada.
        if (!googleUser.EmailVerified)
            throw new ForbiddenException(ForbiddenReason.EmailNotVerified);

        var user = await _users.GetByGoogleIdAsync(googleUser.Subject, cancellationToken);

        // Cuenta ya vinculada (p. ej. creada con Google antes de T18, sin EmailVerifiedAt): Google acaba de verificar el
        // correo, así que queda verificada. No toca la contraseña ni las sesiones; lo guarda la emisión de la sesión.
        user?.MarkEmailVerified();

        if (user is null)
        {
            var existingByEmail = await _users.GetByEmailAsync(EmailAddress.Normalize(googleUser.Email), cancellationToken);
            if (existingByEmail is not null)
            {
                await LinkGoogleAsync(existingByEmail, googleUser.Subject, cancellationToken);
                user = existingByEmail;
            }
        }

        if (user is not null)
            return await BuildExistingUserResponseAsync(user, cancellationToken);

        return await CreateUserAndOrgAsync(googleUser, cancellationToken);
    }

    // Vincula Google a la cuenta encontrada por email (RF-28 b-d). Una cuenta ya vinculada a otro GoogleId responde 409
    // sin cambios (SR-0018.15). Si el email no estaba verificado, quien tenga la contraseña no demostró ser el dueño del
    // correo: se anula, se revocan todas sus sesiones y el email queda verificado por Google. Si ya estaba verificado, la
    // contraseña se conserva. Los cambios los guarda la emisión de la sesión, junto con el refresh token nuevo.
    private async Task LinkGoogleAsync(User user, string googleId, CancellationToken ct)
    {
        if (user.GoogleId is not null)
            throw new ConflictException(GoogleLoginErrors.AccountConflictCode, GoogleLoginErrors.AccountConflictMessage);

        if (!user.IsEmailVerified)
        {
            user.ClearPassword();
            user.MarkEmailVerified();
            await _refreshTokens.RevokeAllForUserAsync(user.UserId, ct);
        }

        user.LinkGoogleAccount(googleId);
    }

    // Igual que login: entra a la última organización usada si la membresía sigue vigente (PLAN-0018 RF-70). Sin
    // membresías vigentes: sesión sin organización si tiene organizaciones restaurables (RF-85); si no, el mismo 422
    // genérico de login (RF-61, SR-0018.18), nunca 500. La vinculación de Google pendiente no se guarda.
    private async Task<AuthTokenDto> BuildExistingUserResponseAsync(User user, CancellationToken ct)
    {
        try
        {
            return await _sessions.StartSessionAsync(user, user.LastActiveOrganizationId, ct);
        }
        catch (NoActiveMembershipException)
        {
            throw AuthCredentialErrors.InvalidCredentials();
        }
    }

    // Reached for a first-time Google sign-in with no prior account. Mirrors
    // RegisterCommandHandler's "new owner" path: placeholder org named after the
    // user's first name, Freelance/CO defaults — refined later in onboarding.
    private async Task<AuthTokenDto> CreateUserAndOrgAsync(GoogleUserInfo googleUser, CancellationToken ct)
    {
        var org = global::Zentory.Domain.Entities.Organization.Create(googleUser.FirstName, LegalTypeConstants.Freelance, "CO");
        await _organizations.AddAsync(org, ct);

        var user = User.Create(
            googleUser.Email,
            googleUser.FirstName,
            googleUser.LastName,
            role: OrgRole.Owner,
            googleId: googleUser.Subject);
        // Google ya verificó el email (RF-28 a se comprobó antes de llegar aquí).
        user.MarkEmailVerified();
        await _users.AddAsync(user, ct);

        org.SetOwner(user.UserId);
        _db.OrganizationMembers.Add(OrganizationMember.Create(org.OrganizationId, user.UserId, OrgRole.Owner));

        // Sin suscripción ni cliente de cobro (PLAN-0018 DEC-0101, CA-44, RF-73): el owner nuevo es free porque no tiene
        // suscripción vigente. El plan solo lo escribe seed assign-plan.

        // La sesión se emite sobre lo ya guardado: IAuthSessionIssuer lee las membresías de la base.
        await _uow.SaveChangesAsync(ct);

        var session = await _sessions.StartSessionAsync(user, org.OrganizationId, ct);
        return session with { IsNewUser = true };
    }
}
