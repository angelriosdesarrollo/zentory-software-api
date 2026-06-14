using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Plans.DTOs;
using Zentory.Domain.Constants;

namespace Zentory.Application.Plans.Queries;

public record GetPlansQuery : IRequest<PlansPageDto>;

public sealed class GetPlansQueryHandler : IRequestHandler<GetPlansQuery, PlansPageDto>
{
    private readonly IZentoryDbContext _db;

    public GetPlansQueryHandler(IZentoryDbContext db) => _db = db;

    public async Task<PlansPageDto> Handle(
        GetPlansQuery     request,
        CancellationToken cancellationToken)
    {
        var plans    = await _db.BillingPlans.Where(p => p.Active).OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);
        var features = await _db.PlanFeatures.ToListAsync(cancellationToken);
        var limits   = await _db.PlanLimits.ToListAsync(cancellationToken);
        var mktg     = await _db.PlanMarketing.ToListAsync(cancellationToken);
        var compare  = await _db.PlanCompareItems.OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);

        return new PlansPageDto(
            Freelance: BuildForAccountType(AccountType.Freelance, plans, features, limits, mktg, compare),
            Empresa:   BuildForAccountType(AccountType.Empresa,   plans, features, limits, mktg, compare));
    }

    private static PlansByAccountTypeDto BuildForAccountType(
        string accountType,
        IEnumerable<Domain.Entities.Billing.BillingPlan>    plans,
        IEnumerable<Domain.Entities.Billing.PlanFeature>    features,
        IEnumerable<Domain.Entities.Billing.PlanLimit>      limits,
        IEnumerable<Domain.Entities.Billing.PlanMarketing>  mktg,
        IEnumerable<Domain.Entities.Billing.PlanCompareItem> compare)
    {
        var planDtos = plans.Select(plan =>
        {
            var m = mktg.FirstOrDefault(x => x.PlanId == plan.Id && x.AccountType == accountType);
            var f = features.Where(x => x.PlanId == plan.Id && x.AccountType == accountType)
                            .OrderBy(x => x.SortOrder)
                            .Select(x => new PlanFeatureItemDto(x.Text, x.IsHighlight, x.BadgeText))
                            .ToList();
            var l = limits.Where(x => x.PlanId == plan.Id && x.AccountType == accountType)
                          .Select(x => new PlanLimitDto(x.FeatureKey, x.LimitValue))
                          .ToList();

            return new PlanDataDto(
                Id:              plan.Name,
                Name:            plan.DisplayName,
                PriceMonthlyUsd: plan.PriceMonthlyUsd,
                PriceAnnualUsd:  plan.PriceAnnualUsd,
                Tagline:         m?.Tagline         ?? string.Empty,
                CtaText:         m?.CtaText          ?? "Comenzar",
                IsPopular:       m?.IsPopular         ?? false,
                FeaturesHeading: m?.FeaturesHeading,
                Features:        f,
                Limits:          l);
        }).ToList();

        var compareRows = compare
            .Where(c => c.AccountType == null || c.AccountType == accountType)
            .Select(c => new CompareRowDto(
                c.FeatureName,
                c.IsEmpresaOnly,
                c.FreeValue,
                c.ProValue,
                c.StudioValue))
            .ToList();

        return new PlansByAccountTypeDto(planDtos, compareRows);
    }
}
