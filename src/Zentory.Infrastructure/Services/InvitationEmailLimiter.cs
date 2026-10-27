using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

// IInvitationEmailLimiter sobre el AuthAttemptLimiter del proceso, con la política organization-invite-email (PLAN-0018
// RF-49 d; SR-0018.22; D-13): misma ventana deslizante, configuración y memoria que las demás políticas.
public sealed class InvitationEmailLimiter(AuthAttemptLimiter limiter) : IInvitationEmailLimiter
{
    public InvitationEmailLimit TryAcquire(string email)
    {
        var decision = limiter.TryAcquire(
            AuthRateLimitPolicies.OrganizationInviteEmail, new AuthAttemptSubject(UserId: null, Ip: null, Email: email));
        return new InvitationEmailLimit(decision.IsAllowed, decision.RetryAfter);
    }
}
