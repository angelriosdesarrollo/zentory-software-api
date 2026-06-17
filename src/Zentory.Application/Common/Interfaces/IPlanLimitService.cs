namespace Zentory.Application.Common.Interfaces;

/// <summary>
/// Resolves quantitative plan limits from the PlanLimits table.
/// Limits are cached in memory (10-minute TTL) to avoid per-request DB hits.
/// </summary>
public interface IPlanLimitService
{
    /// <summary>
    /// Returns the configured limit value for the given plan/accountType/featureKey combination,
    /// or null when the limit is unlimited for that plan.
    /// Throws <see cref="InvalidOperationException"/> if no configuration row exists.
    /// </summary>
    Task<int?> GetLimitAsync(
        string            plan,
        string            accountType,
        string            featureKey,
        CancellationToken ct = default);
}
