using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.DTOs;

namespace Zentory.Application.Organization.Queries;

public record GetOrgPlanQuery : IRequest<OrgPlanDto>;

public sealed class GetOrgPlanQueryHandler : IRequestHandler<GetOrgPlanQuery, OrgPlanDto>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetOrgPlanQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<OrgPlanDto> Handle(GetOrgPlanQuery request, CancellationToken ct)
    {
        var org = await _db.Organizations
            .FirstOrDefaultAsync(o => o.OrganizationId == _tenant.OrganizationId, ct);

        if (org is null)
            throw new NotFoundException("Organization", _tenant.OrganizationId);

        var subscription = await _db.Subscriptions
            .Where(s => s.OrganizationId == _tenant.OrganizationId && s.Status == "active")
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        decimal? price = null;
        if (subscription is not null)
        {
            var billingPlan = await _db.BillingPlans
                .FirstOrDefaultAsync(p => p.Id == subscription.PlanId, ct);
            price = billingPlan?.PriceMonthlyUsd;
        }

        return new OrgPlanDto(
            Plan:              org.Plan,
            AccountType:       org.AccountType,
            RenewsAt:          subscription?.CurrentPeriodEnd?.ToString("yyyy-MM-dd"),
            CancelAtPeriodEnd: subscription?.CancelAtPeriodEnd ?? false,
            PriceMonthlyUsd:   price,
            Currency:          subscription?.Currency ?? "USD");
    }
}
