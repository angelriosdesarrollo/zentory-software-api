using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

public sealed class PlanLimitService : IPlanLimitService
{
    private readonly IZentoryDbContext _db;
    private readonly IMemoryCache      _cache;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public PlanLimitService(IZentoryDbContext db, IMemoryCache cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<int?> GetLimitAsync(
        string            plan,
        string            legalType,
        string            featureKey,
        CancellationToken ct = default)
    {
        var cacheKey = $"plan-limit:{plan}:{legalType}:{featureKey}";

        if (_cache.TryGetValue(cacheKey, out LimitEntry? entry) && entry is not null)
            return entry.Value;

        var row = await _db.PlanLimits
            .Join(_db.BillingPlans,
                  pl => pl.PlanId,
                  bp => bp.Id,
                  (pl, bp) => new { bp.Name, pl.LegalType, pl.FeatureKey, pl.LimitValue })
            .Where(x => x.Name        == plan
                     && x.LegalType == legalType
                     && x.FeatureKey  == featureKey)
            .FirstOrDefaultAsync(ct);

        if (row is null)
            throw new InvalidOperationException(
                $"No plan limit configured for plan='{plan}', legalType='{legalType}', featureKey='{featureKey}'. " +
                $"Ensure the PlanLimits table is seeded.");

        entry = new LimitEntry(row.LimitValue);
        _cache.Set(cacheKey, entry, CacheTtl);

        return entry.Value;
    }

    // Wrapper so we can cache null (unlimited) without confusing a missing key with a stored null.
    private sealed record LimitEntry(int? Value);
}
