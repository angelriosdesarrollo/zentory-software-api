using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;

namespace Zentory.Infrastructure.Services;

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    public const string ClientIdSetting = "Google:ClientId";

    // Mensaje del token rechazado: fijo y sin el token.
    public const string InvalidTokenMessage = "Token de Google inválido o expirado.";

    private readonly string _clientId;

    public GoogleTokenValidator(IConfiguration configuration)
    {
        _clientId = RequireClientId(configuration);
    }

    // Arranque (PLAN-0018 RF-81 h, SR-0018.30 c): un ClientId vacío o en blanco haría que la audiencia no se compruebe
    // de verdad, así que la API no arranca. El mensaje nombra la clave, nunca el valor.
    public static string RequireClientId(IConfiguration configuration)
    {
        var clientId = configuration[ClientIdSetting];
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException($"{ClientIdSetting} es obligatorio y no puede estar vacío ni en blanco.");
        return clientId.Trim();
    }

    public async Task<GoogleUserInfo> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _clientId }
            });
        }
        catch (InvalidJwtException)
        {
            throw new ValidationException([new ValidationError("idToken", InvalidTokenMessage)]);
        }

        return new GoogleUserInfo(
            Subject:       payload.Subject,
            Email:         payload.Email,
            FirstName:     string.IsNullOrWhiteSpace(payload.GivenName) ? payload.Email : payload.GivenName,
            LastName:      payload.FamilyName ?? "",
            EmailVerified: payload.EmailVerified,
            IssuedAt:      payload.IssuedAtTimeSeconds is { } iat ? DateTimeOffset.FromUnixTimeSeconds(iat) : null,
            Nonce:         payload.Nonce);
    }
}
