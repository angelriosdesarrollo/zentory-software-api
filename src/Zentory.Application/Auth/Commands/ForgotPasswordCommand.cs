using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Domain.Common;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

// POST /auth/forgot-password (PLAN-0018 RF-21 a, RF-22, RF-23, RF-25, RF-27; SR-0018.10; D-11, D-14). Responde siempre lo
// mismo, exista o no el email y sea o no una cuenta solo de Google: el cuerpo no distingue los casos y el correo sale
// por IAuthEmailQueue, fuera del request, así que la respuesta tampoco espera al proveedor.
public record ForgotPasswordCommand(string Email) : IRequest<ForgotPasswordResponse>;

public sealed record ForgotPasswordResponse(string Message)
{
    // Cuerpo fijo de la respuesta 200 (RF-23).
    public static readonly ForgotPasswordResponse Fixed = new(
        "Si el correo corresponde a una cuenta con contraseña, te enviaremos un enlace para restablecerla.");
}

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        // Mensajes sin el valor recibido. La validación no consulta la base: no revela si el email existe.
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress().WithMessage("El correo electrónico no es válido.");
    }
}

public sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, ForgotPasswordResponse>
{
    // Etiqueta fija de la cola: es lo único del correo que puede llegar a los logs (D-14).
    public const string EmailKind = "password-reset";

    public const string ResetPath = "/reset-password";

    // Dos solicitudes simultáneas del mismo usuario pueden invalidar el mismo token anterior; la que pierde lo reintenta.
    private const int MaxAttempts = 3;

    private readonly IUserRepository      _users;
    private readonly IZentoryDbContext    _db;
    private readonly IUnitOfWork          _uow;
    private readonly IAuthEmailQueue      _emails;
    private readonly IApplicationSettings _settings;
    private readonly TimeProvider         _time;

    public ForgotPasswordCommandHandler(
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

    public async Task<ForgotPasswordResponse> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByEmailAsync(EmailAddress.Normalize(request.Email), cancellationToken);

        // Solo una cuenta con contraseña recibe el enlace; una cuenta solo de Google entra con Google (RF-23).
        if (user?.PasswordHash is null)
            return ForgotPasswordResponse.Fixed;

        var (toEmail, recipientName, userId) = (user.Email, user.FirstName, user.UserId);
        var (token, expiresAt) = await IssueAsync(userId, cancellationToken);

        // La URL sale de App:BaseUrl, nunca del Host ni del Origin de la petición (RF-25).
        var resetUrl = $"{_settings.BaseUrl}{ResetPath}?token={token}";
        _emails.TryEnqueue(EmailKind, (email, ct) =>
            email.SendPasswordResetEmailAsync(toEmail, recipientName, resetUrl, expiresAt, ct));

        return ForgotPasswordResponse.Fixed;
    }

    // Invalida los tokens anteriores sin usar del usuario y guarda el nuevo en el mismo SaveChanges (RF-22 d).
    private async Task<(string RawToken, DateTime ExpiresAt)> IssueAsync(Guid userId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var previous = await _db.PasswordResetTokens
                .Where(t => t.UserId == userId && t.UsedAt == null)
                .ToListAsync(ct);
            foreach (var old in previous)
                old.Invalidate(now);

            var issued = OneTimeToken.Issue();
            var token  = PasswordResetToken.Issue(userId, issued.Hash, now);
            _db.PasswordResetTokens.Add(token);

            try
            {
                await _uow.SaveChangesAsync(ct);
                return (issued.RawToken, token.ExpiresAt);
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < MaxAttempts)
            {
                // Otra solicitud invalidó o canjeó uno de los anteriores entre la lectura y este guardado.
                exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            }
        }
    }
}
