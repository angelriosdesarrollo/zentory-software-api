using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Infrastructure.Services;

// Reautenticación de las acciones irreversibles (PLAN-0018 RF-81, RF-84; D-40; ADR reautenticacion-reciente, opción E′).
// Scoped. No consume el nonce: devuelve una verificación pendiente y el comando la consume justo antes de escribir.
//
// Todo fallo es el mismo 403 (ReauthenticationFailedException) y antes deja rastro: cuenta en reauth:{sub}, una fila de
// AuditLog con motivo genérico y, al tercer fallo de la ventana, un correo de aviso al usuario (RF-81 g). Ni la
// contraseña, ni el id_token, ni el nonce llegan a logs, AuditLog, correo o respuesta.
public sealed class ReauthenticationVerifier : IReauthenticationVerifier
{
    public const string AuditTable    = "users";
    public const string AuditAction   = "REAUTHFAIL";
    public const string AuditReason   = "reauthentication_failed";
    public const string WarningKind   = "reauth-failure-warning";
    public const int    WarningOnFailure = 3;

    private readonly IGoogleTokenValidator              _google;
    private readonly ReauthNonce                        _nonces;
    private readonly IReauthNonceStore                  _consumed;
    private readonly AuthAttemptLimiter                 _limiter;
    private readonly IAuthEmailQueue                    _emails;
    private readonly ZentoryDbContext                   _db;
    private readonly TimeProvider                       _time;
    private readonly ILogger<ReauthenticationVerifier>  _logger;

    public ReauthenticationVerifier(
        IGoogleTokenValidator             google,
        ReauthNonce                       nonces,
        IReauthNonceStore                 consumed,
        AuthAttemptLimiter                limiter,
        IAuthEmailQueue                   emails,
        ZentoryDbContext                  db,
        TimeProvider                      time,
        ILogger<ReauthenticationVerifier> logger)
    {
        _google   = google;
        _nonces   = nonces;
        _consumed = consumed;
        _limiter  = limiter;
        _emails   = emails;
        _db       = db;
        _time     = time;
        _logger   = logger;
    }

    public async Task<ReauthVerification> VerifyAsync(
        User              user,
        ReauthCredential  credential,
        string            action,
        Guid?             organizationId = null,
        CancellationToken ct             = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(credential);
        if (!ReauthAction.IsKnown(action))
            throw new ArgumentOutOfRangeException(nameof(action), "Acción de reautenticación desconocida.");

        // Ninguna o las dos: error del contrato, no un intento fallido (no cuenta ni audita). Sin el valor.
        if (!credential.HasExactlyOne)
            throw new ValidationException([new ValidationError(ReauthCredential.FieldName, ReauthCredential.ExactlyOneMessage)]);

        var verification = credential.HasPassword
            ? VerifyPassword(user, credential.CurrentPassword!)
            : await VerifyGoogleAsync(user, credential.GoogleIdToken!, action, ct);

        if (verification is not null)
            return verification;

        await RecordFailureAsync(user, action, organizationId);
        throw new ReauthenticationFailedException();
    }

    private static ReauthVerification? VerifyPassword(User user, string password)
    {
        if (user.PasswordHash is null)
            return null;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? ReauthVerification.ByPassword() : null;
        }
        catch (Exception)
        {
            // Hash corrupto o en otro formato: es un fallo de credencial, no un 500.
            return null;
        }
    }

    private async Task<ReauthVerification?> VerifyGoogleAsync(User user, string idToken, string action, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(user.GoogleId))
            return null;

        GoogleUserInfo info;
        try
        {
            info = await _google.ValidateAsync(idToken, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Toda excepción del validador (hoy ValidationException, que sería un 422) es un fallo uniforme (SR-0018.30 a).
            return null;
        }

        if (info is null
            || !info.EmailVerified
            || !string.Equals(info.Subject, user.GoogleId, StringComparison.Ordinal)
            || !IsRecent(info.IssuedAt))
            return null;

        // El nonce ata el id_token a este usuario y a esta acción (RF-84 c) y sirve una sola vez (RF-84 d).
        if (!_nonces.TryValidate(info.Nonce, user.UserId, action, out var expiresAt)
            || _consumed.IsConsumed(info.Nonce!))
            return null;

        return ReauthVerification.ByGoogle(info.Nonce!, expiresAt);
    }

    // iat entre ahora − 5 min y ahora + 30 s (SR-0018.30 b), ambos extremos incluidos, con el reloj inyectado.
    private bool IsRecent(DateTimeOffset? issuedAt)
    {
        if (issuedAt is not { } iat)
            return false;

        var now = _time.GetUtcNow();
        return iat >= now - OrgSecurity.ReauthenticationWindow
            && iat <= now + OrgSecurity.ReauthenticationFutureSkew;
    }

    private async Task RecordFailureAsync(User user, string action, Guid? organizationId)
    {
        var failures = _limiter.RecordFailure(AuthRateLimitPolicies.Reauth, new AuthAttemptSubject(user.UserId.ToString(), null, null));

        _logger.LogWarning("Reauthentication failed for user {UserId} on {ReauthAction}", user.UserId, action);

        _db.AuditLogs.Add(AuditLog.Record(
            tableName:      AuditTable,
            recordId:       user.UserId,
            action:         AuditAction,
            organizationId: organizationId,
            userId:         user.UserId,
            newValues:      JsonSerializer.Serialize(new { action, reason = AuditReason })));
        // Sin el token de la petición: el fallo queda registrado aunque el cliente corte la conexión.
        await _db.SaveChangesAsync(CancellationToken.None);

        // Un único aviso por ventana: el tercero y no los siguientes (RF-81 g; SR-0018.42).
        if (failures == WarningOnFailure)
            EnqueueWarning(user);
    }

    private void EnqueueWarning(User user)
    {
        var toEmail       = user.Email;
        var recipientName = user.FirstName;
        var occurredAt    = _time.GetUtcNow().UtcDateTime;

        _emails.TryEnqueue(WarningKind, (email, ct) =>
            email.SendReauthenticationFailureWarningAsync(toEmail, recipientName, occurredAt, ct));
    }
}
