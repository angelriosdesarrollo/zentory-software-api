using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Infrastructure.Services;

namespace Zentory.Tests.Support;

// Doble de IGoogleTokenValidator sin llamar a Google: cada id_token registrado con Register devuelve su GoogleUserInfo
// (con EmailVerified, IssuedAt y Nonce tal como el test los arme); uno desconocido es un token rechazado y lanza la misma
// excepción que GoogleTokenValidator (ValidationException con el mismo campo y mensaje; PLAN-0018 CA-110, SR-0018.30 a).
// Se usa directo en tests unitarios o como singleton en un host derivado (WithWebHostBuilder + ConfigureTestServices).
public sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    private readonly Dictionary<string, GoogleUserInfo> _tokens = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    // Registra un id_token nuevo para esa identidad de Google y lo devuelve.
    public string Register(GoogleUserInfo user)
    {
        var idToken = $"fake-google-id-token-{Guid.NewGuid():N}";
        lock (_gate) _tokens[idToken] = user;
        return idToken;
    }

    // Un id_token que el validador rechaza (nunca registrado).
    public static string RejectedToken() => $"fake-google-rejected-id-token-{Guid.NewGuid():N}";

    public Task<GoogleUserInfo> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_tokens.TryGetValue(idToken, out var user))
                return Task.FromResult(user);
        }
        throw new ValidationException([new ValidationError("idToken", GoogleTokenValidator.InvalidTokenMessage)]);
    }
}
