namespace Zentory.Application.Common.Interfaces;

// Datos del id_token de Google ya validado (firma y aud). EmailVerified, IssuedAt y Nonce (PLAN-0018 RF-81 c, RF-28) los
// usan la reautenticación y, desde T18, el login con Google. Valores por defecto restrictivos: quien construye un
// GoogleUserInfo sin ellos obtiene un correo sin verificar, sin iat y sin nonce, que la reautenticación rechaza.
public record GoogleUserInfo(
    string          Subject,
    string          Email,
    string          FirstName,
    string          LastName,
    bool            EmailVerified = false,
    DateTimeOffset? IssuedAt      = null,
    string?         Nonce         = null);

public interface IGoogleTokenValidator
{
    // Un token rechazado lanza ValidationException (Zentory.Application.Exceptions). Quien no quiera un 422 debe
    // capturarla (la reautenticación convierte cualquier excepción en su 403 uniforme, SR-0018.30 a).
    Task<GoogleUserInfo> ValidateAsync(string idToken, CancellationToken ct = default);
}
