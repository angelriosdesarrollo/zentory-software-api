using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;

namespace Zentory.Infrastructure.Services;

public sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    private readonly string _clientId;

    public GoogleTokenValidator(IConfiguration configuration)
    {
        _clientId = configuration["Google:ClientId"]
            ?? throw new InvalidOperationException("Google:ClientId is required.");
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
            throw new ValidationException([new ValidationError("idToken", "Token de Google inválido o expirado.")]);
        }

        return new GoogleUserInfo(
            Subject:   payload.Subject,
            Email:     payload.Email,
            FirstName: string.IsNullOrWhiteSpace(payload.GivenName)  ? payload.Email : payload.GivenName,
            LastName:  payload.FamilyName ?? "");
    }
}
