using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.DTOs;

namespace Zentory.Application.Organization.Queries;

public record GetOrgPlanQuery : IRequest<OrgPlanDto>;

public sealed class GetOrgPlanQueryHandler : IRequestHandler<GetOrgPlanQuery, OrgPlanDto>
{
    private readonly IZentoryDbContext      _db;
    private readonly ITenantContext         _tenant;
    private readonly IPlanResolutionService _plans;

    public GetOrgPlanQueryHandler(IZentoryDbContext db, ITenantContext tenant, IPlanResolutionService plans)
    {
        _db     = db;
        _tenant = tenant;
        _plans  = plans;
    }

    public async Task<OrgPlanDto> Handle(GetOrgPlanQuery request, CancellationToken ct)
    {
        var org = await _db.Organizations
            .FirstOrDefaultAsync(o => o.OrganizationId == _tenant.OrganizationId, ct);

        if (org is null)
            throw new NotFoundException("Organization", _tenant.OrganizationId);

        // The subscription — and therefore the price/renewal info — belongs to the org's
        // owner, not necessarily the member currently viewing this page.
        var subscription = org.OwnerId is null
            ? null
            : await _db.Subscriptions
                .Where(s => s.UserId == org.OwnerId.Value && (s.Status == "active" || s.Status == "trialing"))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct);

        decimal? price = null;
        if (subscription is not null)
        {
            var billingPlan = await _db.BillingPlans
                .FirstOrDefaultAsync(p => p.Id == subscription.PlanId, ct);
            price = billingPlan?.PriceMonthlyUsd;
        }

        var plan = await _plans.ResolveForOwnerAsync(org.OwnerId, ct);

        return new OrgPlanDto(
            Plan:              plan,
            AccountType:       org.AccountType,
            RenewsAt:          subscription?.CurrentPeriodEnd?.ToString("yyyy-MM-dd"),
            CancelAtPeriodEnd: subscription?.CancelAtPeriodEnd ?? false,
            PriceMonthlyUsd:   price,
            Currency:          subscription?.Currency ?? "USD");
    }
}
