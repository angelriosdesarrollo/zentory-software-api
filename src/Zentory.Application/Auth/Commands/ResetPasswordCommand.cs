using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

// POST /auth/reset-password (PLAN-0018 RF-21 b, RF-22 c, RF-24, RF-26; SR-0018.11 c, SR-0018.14; D-11). La política de
// contraseñas se valida antes del handler (ValidationBehavior), así que una contraseña inválida responde 422 sin leer ni
// consumir el token. Un token inexistente, usado o vencido responde 422 TOKEN_INVALID; el perdedor de dos canjes
// simultáneos también (UsedAt es token de concurrencia). No cambia CurrentSessionId (RF-90, interacción con RF-24).
public record ResetPasswordCommand(string Token, string NewPassword) : IRequest;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        // Sin eco del token ni de la contraseña en los mensajes.
        RuleFor(x => x.Token).NotEmpty().WithMessage("El enlace no es válido.");
        RuleFor(x => x.NewPassword).ApplyPasswordPolicy();
    }
}

// Reglas de PasswordPolicy (RF-26) para FluentValidation, compartidas por register y reset-password.
public static class PasswordPolicyRules
{
    public static IRuleBuilderOptions<T, string> ApplyPasswordPolicy<T>(this IRuleBuilder<T, string> rule)
        => rule
            .Must(password => password is not null && PasswordPolicy.HasValidLength(password))
            .WithMessage($"La contraseña debe tener entre {PasswordPolicy.MinLength} y {PasswordPolicy.MaxLength} caracteres.")
            .Must(password => password is null || PasswordPolicy.IsWithinByteLimit(password))
            .WithMessage($"La contraseña no puede ocupar más de {PasswordPolicy.MaxUtf8Bytes} bytes.");
}

public sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand>
{
    // Una revocación de refresh tokens que choca con una rotación simultánea se reintenta; el choque en el token de
    // reset no, porque significa que otro canje ganó.
    private const int MaxAttempts = 3;

    private readonly IZentoryDbContext       _db;
    private readonly IUserRepository         _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork             _uow;
    private readonly TimeProvider            _time;

    public ResetPasswordCommandHandler(
        IZentoryDbContext       db,
        IUserRepository         users,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork             uow,
        TimeProvider            time)
    {
        _db            = db;
        _users         = users;
        _refreshTokens = refreshTokens;
        _uow           = uow;
        _time          = time;
    }

    public async Task Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = OneTimeToken.HashOf(request.Token);
        // El hash se calcula una vez: BCrypt es lo caro y no depende del intento.
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        for (var attempt = 1; ; attempt++)
        {
            var now   = _time.GetUtcNow().UtcDateTime;
            var token = await _db.PasswordResetTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
            if (token is null || !token.IsValidAt(now))
                throw new InvalidTokenException();

            var user = await _users.GetByIdAsync(token.UserId, cancellationToken)
                ?? throw new InvalidTokenException();

            token.MarkUsedAt(now);
            user.SetPassword(passwordHash);
            // Canjear el enlace prueba control del buzón (SR-0018.11 c).
            user.MarkEmailVerified();
            await _refreshTokens.RevokeAllForUserAsync(user.UserId, cancellationToken);

            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                if (exception.Entries.Any(entry => entry.Entity is PasswordResetToken))
                    throw new InvalidTokenException();
                if (attempt >= MaxAttempts)
                    throw;

                exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            }
        }
    }
}
