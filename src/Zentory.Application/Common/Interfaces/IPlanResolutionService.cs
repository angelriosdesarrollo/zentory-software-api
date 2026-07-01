namespace Zentory.Application.Common.Interfaces;

/// <summary>
/// Resolves the subscription plan for an organization's owner.
/// The plan belongs to the person (owner), not to the organization — every org a user
/// owns shares the same plan. Falls back to Plan.Free when there is no owner or no
/// active/trialing subscription.
/// </summary>
public interface IPlanResolutionService
{
    Task<string> ResolveForOwnerAsync(Guid? ownerId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> ResolveForOwnersAsync(
        IEnumerable<Guid> ownerIds, CancellationToken ct = default);
}
