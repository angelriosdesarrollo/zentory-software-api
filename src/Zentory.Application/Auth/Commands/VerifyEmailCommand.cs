using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

// POST /auth/verify-email (PLAN-0018 RF-20, RF-21 c, RF-22 c; D-11). Pública: el token es la credencial. Un token
// inexistente, usado o vencido responde 422 TOKEN_INVALID, y también uno emitido para otro email que el actual del
// usuario: solo prueba control del buzón al que se envió. De dos canjes simultáneos del mismo token gana uno; el otro
// recibe 422 (UsedAt es token de concurrencia). No inicia ni cambia la sesión.
public record VerifyEmailCommand(string Token) : IRequest;

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        // Sin eco del token en el mensaje.
        RuleFor(x => x.Token).NotEmpty().WithMessage("El enlace no es válido.");
    }
}

public sealed class VerifyEmailCommandHandler : IRequestHandler<VerifyEmailCommand>
{
    // Un choque en la fila del usuario (otra escritura concurrente) se reintenta; el choque en el token no, porque
    // significa que otro canje ganó.
    private const int MaxAttempts = 3;

    private readonly IZentoryDbContext _db;
    private readonly IUserRepository   _users;
    private readonly IUnitOfWork       _uow;
    private readonly TimeProvider      _time;

    public VerifyEmailCommandHandler(IZentoryDbContext db, IUserRepository users, IUnitOfWork uow, TimeProvider time)
    {
        _db    = db;
        _users = users;
        _uow   = uow;
        _time  = time;
    }

    public async Task Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = OneTimeToken.HashOf(request.Token);

        for (var attempt = 1; ; attempt++)
        {
            var now   = _time.GetUtcNow().UtcDateTime;
            var token = await _db.EmailVerificationTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
            if (token is null || !token.IsValidAt(now))
                throw new InvalidTokenException();

            var user = await _users.GetByIdAsync(token.UserId, cancellationToken);
            if (user is null || !token.IsFor(user.Email))
                throw new InvalidTokenException();

            token.MarkUsedAt(now);
            user.MarkEmailVerified();

            try
            {
                await _uow.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                if (exception.Entries.Any(entry => entry.Entity is EmailVerificationToken))
                    throw new InvalidTokenException();
                if (attempt >= MaxAttempts)
                    throw;

                exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            }
        }
    }
}
