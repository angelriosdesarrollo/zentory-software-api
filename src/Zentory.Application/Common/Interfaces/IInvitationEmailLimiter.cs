namespace Zentory.Application.Common.Interfaces;

// Techo de invitaciones por email destino (PLAN-0018 RF-49 d; SR-0018.22): 3 por día en todas las organizaciones.
// InviteMemberCommand lo consume solo cuando la invitación pasó todas las validaciones, justo antes de crearla y encolar
// el correo, para que los intentos rechazados (403, 409, 422) no agoten el cupo del buzón de la víctima. El techo por
// organización lo cuenta el filtro [AuthRateLimit] del controlador.
public interface IInvitationEmailLimiter
{
    // Cuenta una invitación al email (se normaliza dentro) si hay cupo. Si no, no la cuenta y devuelve cuánto falta
    // para que se libere.
    InvitationEmailLimit TryAcquire(string email);
}

public readonly record struct InvitationEmailLimit(bool IsAllowed, TimeSpan RetryAfter);
