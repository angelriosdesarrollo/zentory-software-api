using Zentory.Domain.Entities;

namespace Zentory.Application.Common.Security;

// Credencial que viaja en el cuerpo de la acción irreversible (PLAN-0018 RF-81 a): exactamente uno de los dos. Sin
// ToString con valores: un record lo imprimiría completo si alguien lo registrara.
public sealed record ReauthCredential(string? CurrentPassword, string? GoogleIdToken)
{
    public const string FieldName = "credential";

    // Mensaje fijo, sin el valor (SR-0018.30 d).
    public const string ExactlyOneMessage = "Envía exactamente uno de currentPassword o googleIdToken.";

    public bool HasPassword    => !string.IsNullOrEmpty(CurrentPassword);
    public bool HasGoogleToken => !string.IsNullOrEmpty(GoogleIdToken);
    public bool HasExactlyOne  => HasPassword ^ HasGoogleToken;

    public override string ToString() => $"{nameof(ReauthCredential)} {{ Method = {(HasPassword ? "password" : HasGoogleToken ? "google" : "none")} }}";
}

public enum ReauthMethod
{
    Password,
    Google,
}

// Verificación válida y todavía pendiente (D-40): VerifyAsync no tiene efecto secundario. El comando, después de sus
// propias validaciones y justo antes de escribir, llama a TryConsume; si la escritura falla después, a Release.
public sealed class ReauthVerification
{
    private ReauthVerification(ReauthMethod method, string? nonce, DateTimeOffset? nonceExpiresAt)
    {
        Method         = method;
        Nonce          = nonce;
        NonceExpiresAt = nonceExpiresAt;
    }

    public ReauthMethod    Method         { get; }
    public string?         Nonce          { get; }
    public DateTimeOffset? NonceExpiresAt { get; }

    public static ReauthVerification ByPassword() => new(ReauthMethod.Password, null, null);

    public static ReauthVerification ByGoogle(string nonce, DateTimeOffset expiresAt) =>
        new(ReauthMethod.Google, nonce, expiresAt);

    // La contraseña no usa nonce. Con Google consume el nonce de forma atómica: false si otra petición lo consumió entre
    // la verificación y este punto, y el comando responde 403 REAUTHENTICATION_FAILED sin efecto.
    public bool TryConsume(IReauthNonceStore store)
        => Method == ReauthMethod.Password || store.TryConsume(Nonce!, NonceExpiresAt!.Value);

    public void Release(IReauthNonceStore store)
    {
        if (Method == ReauthMethod.Google)
            store.Release(Nonce!);
    }

    public override string ToString() => $"{nameof(ReauthVerification)} {{ Method = {Method} }}";
}

// Reautenticación de las acciones irreversibles (PLAN-0018 RF-81; ADR reautenticacion-reciente). La llaman los comandos
// de eliminar la organización y de proponer y aceptar la transferencia, después de la policy y del rate limit
// reauth:{sub} ([AuthRateLimit]) y antes de sus propias validaciones.
public interface IReauthenticationVerifier
{
    // Ninguna o las dos credenciales → ValidationException (422, sin eco del valor). Credencial inválida por cualquier
    // causa → ReauthenticationFailedException (403 uniforme), después de registrar el fallo (AuditLog, conteo y aviso al
    // tercer fallo). organizationId es la organización afectada, para la fila de AuditLog.
    Task<ReauthVerification> VerifyAsync(
        User              user,
        ReauthCredential  credential,
        string            action,
        Guid?             organizationId = null,
        CancellationToken ct             = default);
}

// 403 { error: "forbidden", code: "REAUTHENTICATION_FAILED" } con el mismo cuerpo en todos los casos (RF-81 d): el
// mensaje no dice qué falló.
public sealed class ReauthenticationFailedException : Exception
{
    public const string Code = "REAUTHENTICATION_FAILED";

    public ReauthenticationFailedException() : base("No pudimos confirmar tu identidad.") { }
}
