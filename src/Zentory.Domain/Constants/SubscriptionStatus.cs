namespace Zentory.Domain.Constants;

// Estados de una suscripción (PLAN-0018 RF-37, RF-38). Única fuente de los valores para el índice único filtrado de M3
// (ux_subscriptions_user_active) y para AssignPlanBySeedCommand.
public static class SubscriptionStatus
{
    public const string Trialing   = "trialing";
    public const string Active     = "active";
    public const string PastDue    = "past_due";
    public const string Cancelled  = "cancelled";
    public const string Unpaid     = "unpaid";
    public const string Incomplete = "incomplete";

    // Estados que cuentan como suscripción vigente: de ellos sale el plan resuelto del owner y el índice único de M3
    // admite como máximo una fila por usuario.
    public static readonly IReadOnlyList<string> Current = [Active, Trialing];

    public static bool IsCurrent(string status) => status is Active or Trialing;
}

// Motivos de cancelación que deja seed assign-plan (RF-37 d, d2).
public static class SubscriptionCancellationReason
{
    public const string ReassignedBySeed = "reassigned_by_seed";
    public const string DowngradedBySeed = "downgraded_by_seed";
}
