using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Models;
using Zentory.Application.Exceptions;

namespace Zentory.Application.ActivityLogs.Queries;

public record GetActivityLogQuery(
    string?   EntityType = null,
    Guid?     EntityId   = null,
    DateTime? From       = null,
    DateTime? To         = null,
    int       Page       = 1,
    int       PageSize   = 20) : IRequest<PagedResult<ActivityLogDto>>;

public sealed class GetActivityLogQueryHandler
    : IRequestHandler<GetActivityLogQuery, PagedResult<ActivityLogDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext   _tenant;

    public GetActivityLogQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<PagedResult<ActivityLogDto>> Handle(
        GetActivityLogQuery request,
        CancellationToken   cancellationToken)
    {
        if (!string.Equals(_tenant.AccountType, "empresa", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_tenant.Plan, "free", StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException(ForbiddenReason.PlanRequired, "pro");

        var query = _db.ActivityLogs
            .Where(l => l.OrganizationId == _tenant.OrganizationId);

        if (!string.IsNullOrEmpty(request.EntityType))
            query = query.Where(l => l.EntityType == request.EntityType);

        if (request.EntityId.HasValue)
            query = query.Where(l => l.EntityId == request.EntityId.Value);

        if (request.From.HasValue)
            query = query.Where(l => l.OccurredAt >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(l => l.OccurredAt <= request.To.Value);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(l => l.OccurredAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(l => new ActivityLogDto(
                l.Id,
                l.UserInitials,
                l.Action,
                l.EntityCode ?? string.Empty,
                l.OccurredAt.ToString("o")))
            .ToListAsync(cancellationToken);

        return new PagedResult<ActivityLogDto>(items, total, request.Page, request.PageSize);
    }
}
