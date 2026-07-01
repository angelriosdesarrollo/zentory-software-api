using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;

namespace Zentory.Infrastructure.Services;

public sealed class PlanResolutionService : IPlanResolutionService
{
    private readonly IZentoryDbContext _db;

    public PlanResolutionService(IZentoryDbContext db) => _db = db;

    public async Task<string> ResolveForOwnerAsync(Guid? ownerId, CancellationToken ct = default)
    {
        if (ownerId is null)
            return Plan.Free;

        var plans = await ResolveForOwnersAsync([ownerId.Value], ct);
        return plans.GetValueOrDefault(ownerId.Value, Plan.Free);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ResolveForOwnersAsync(
        IEnumerable<Guid> ownerIds, CancellationToken ct = default)
    {
        var ids = ownerIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var rows = await _db.Subscriptions
            .Where(s => ids.Contains(s.UserId) && (s.Status == "active" || s.Status == "trialing"))
            .Join(_db.BillingPlans,
                  s => s.PlanId,
                  bp => bp.Id,
                  (s, bp) => new { s.UserId, s.CreatedAt, PlanName = bp.Name })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.CreatedAt).First().PlanName);
    }
}
