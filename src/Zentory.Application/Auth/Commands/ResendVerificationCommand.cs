using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

// POST /auth/resend-verification (PLAN-0018 RF-21 d, RF-22, RF-25; SR-0018.10; D-11, D-14). Exige el JWT de usuario: el
// usuario sale del sub, nunca del cuerpo. Una cuenta ya verificada responde 202 sin correo ni token; si no, invalida los
// tokens anteriores sin usar y encola uno nuevo. La respuesta no espera al proveedor de correo.
public record ResendVerificationCommand(Guid UserId) : IRequest;

// Enlace y encolado del correo de verificación, compartidos por register y resend-verification (RF-20, RF-25).
public static class EmailVerificationEmail
{
    // Etiqueta fija de la cola: es lo único del correo que puede llegar a los logs (D-14).
    public const string EmailKind = "email-verification";

    public const string VerifyPath = "/verify-email";

    // La URL sale de App:BaseUrl, nunca del Host ni del Origin de la petición; el token va codificado (RF-25).
    public static string UrlFor(string baseUrl, string rawToken)
        => $"{baseUrl}{VerifyPath}?token={Uri.EscapeDataString(rawToken)}";

    // Encola el correo con copias de los datos: la cola corre en otro scope, sin la entidad ni el contexto del request.
    public static void Enqueue(
        IAuthEmailQueue queue, IApplicationSettings settings, User user, string rawToken, DateTime expiresAtUtc)
    {
        var (toEmail, recipientName) = (user.Email, user.FirstName);
        var verifyUrl = UrlFor(settings.BaseUrl, rawToken);
        queue.TryEnqueue(EmailKind, (email, ct) =>
            email.SendEmailVerificationEmailAsync(toEmail, recipientName, verifyUrl, expiresAtUtc, ct));
    }
}

public sealed class ResendVerificationCommandHandler : IRequestHandler<ResendVerificationCommand>
{
    // Dos reenvíos simultáneos del mismo usuario pueden invalidar el mismo token anterior; el que pierde lo reintenta.
    private const int MaxAttempts = 3;

    private readonly IUserRepository      _users;
    private readonly IZentoryDbContext    _db;
    private readonly IUnitOfWork          _uow;
    private readonly IAuthEmailQueue      _emails;
    private readonly IApplicationSettings _settings;
    private readonly TimeProvider         _time;

    public ResendVerificationCommandHandler(
        IUserRepository      users,
        IZentoryDbContext    db,
        IUnitOfWork          uow,
        IAuthEmailQueue      emails,
        IApplicationSettings settings,
        TimeProvider         time)
    {
        _users    = users;
        _db       = db;
        _uow      = uow;
        _emails   = emails;
        _settings = settings;
        _time     = time;
    }

    public async Task Handle(ResendVerificationCommand request, CancellationToken cancellationToken)
    {
        // El middleware ya comparó la sesión del JWT con la del usuario (RF-90 d): el usuario existe. Si no, no hay nada
        // que reenviar y la respuesta es la misma.
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.IsEmailVerified)
            return;

        var (token, expiresAt) = await IssueAsync(user.UserId, user.Email, cancellationToken);
        EmailVerificationEmail.Enqueue(_emails, _settings, user, token, expiresAt);
    }

    // Invalida los tokens anteriores sin usar del usuario y guarda el nuevo en el mismo SaveChanges (RF-22 d).
    private async Task<(string RawToken, DateTime ExpiresAt)> IssueAsync(Guid userId, string email, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var previous = await _db.EmailVerificationTokens
                .Where(t => t.UserId == userId && t.UsedAt == null)
                .ToListAsync(ct);
            foreach (var old in previous)
                old.Invalidate(now);

            var issued = OneTimeToken.Issue();
            var token  = EmailVerificationToken.Issue(userId, email, issued.Hash, now);
            _db.EmailVerificationTokens.Add(token);

            try
            {
                await _uow.SaveChangesAsync(ct);
                return (issued.RawToken, token.ExpiresAt);
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < MaxAttempts)
            {
                // Otro reenvío invalidó o canjeó uno de los anteriores entre la lectura y este guardado.
                exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            }
        }
    }
}
