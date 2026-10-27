using MediatR;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Auth.Services;
using Zentory.Domain.Common;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

public record LoginCommand(string Email, string Password) : IRequest<AuthTokenDto>;

// Entra a la última organización usada si la membresía sigue vigente (PLAN-0018 RF-70); la elección y las membresías
// salen de IAuthSessionIssuer, nunca del filtro de tenant (R-1: un Bearer válido en este endpoint anónimo las truncaría).
// Sin membresías vigentes: sesión sin organización si tiene organizaciones restaurables (RF-85); si no, 422 genérico (RF-61).
// Cada login correcto inicia una sesión nueva y reemplaza la anterior del usuario (RF-90 b, D-47); un login rechazado no
// toca la sesión vigente.
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthTokenDto>
{
    private readonly IUserRepository    _users;
    private readonly IAuthSessionIssuer _sessions;

    public LoginCommandHandler(IUserRepository users, IAuthSessionIssuer sessions)
    {
        _users    = users;
        _sessions = sessions;
    }

    public async Task<AuthTokenDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // Mismo error para email inexistente, contraseña incorrecta, cuenta solo de Google (sin PasswordHash) o falta de
        // membresías (CA-26, RF-61): no revela cuál falló. El email se normaliza como en el registro (SR-2.13).
        var user = await _users.GetByEmailAsync(EmailAddress.Normalize(request.Email), cancellationToken);
        if (user is null)
            throw AuthCredentialErrors.InvalidCredentials();

        if (user.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw AuthCredentialErrors.InvalidCredentials();

        try
        {
            return await _sessions.StartSessionAsync(user, user.LastActiveOrganizationId, cancellationToken);
        }
        catch (NoActiveMembershipException)
        {
            throw AuthCredentialErrors.InvalidCredentials();
        }
    }
}
