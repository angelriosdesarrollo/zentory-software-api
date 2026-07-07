using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities.Ai;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class AiUsageLogRepository : IAiUsageLogRepository
{
    private readonly ZentoryDbContext _db;

    public AiUsageLogRepository(ZentoryDbContext db) => _db = db;

    public async Task AddAsync(AiUsageLog log, CancellationToken ct = default)
        => await _db.AiUsageLogs.AddAsync(log, ct);

    public async Task<int> CountThisMonthAsync(Guid organizationId, Guid featureId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.AiUsageLogs.CountAsync(u =>
            u.OrganizationId  == organizationId &&
            u.FeatureId       == featureId &&
            u.CreatedAt.Year  == now.Year &&
            u.CreatedAt.Month == now.Month, ct);
    }
}
