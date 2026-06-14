using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Services;

public sealed class ActivityLogService : IActivityLogService
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext   _tenant;

    public ActivityLogService(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task LogAsync(
        string    entityType,
        Guid      entityId,
        string    action,
        string?   entityCode = null,
        string?   metadata   = null,
        CancellationToken ct = default)
    {
        // ActivityLog is only for Empresa accounts; Freelance accounts are skipped.
        if (!_tenant.IsAuthenticated ||
            !string.Equals(_tenant.AccountType, "empresa", StringComparison.OrdinalIgnoreCase))
            return;

        var entry = ActivityLog.Create(
            organizationId: _tenant.OrganizationId,
            entityType:     entityType,
            entityId:       entityId,
            userInitials:   _tenant.UserInitials,
            action:         action,
            entityCode:     entityCode,
            metadata:       metadata,
            userId:         _tenant.UserId);

        await _db.ActivityLogs.AddAsync(entry, ct);
    }
}
