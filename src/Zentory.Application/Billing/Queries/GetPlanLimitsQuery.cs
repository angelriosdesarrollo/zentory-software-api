using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using static Zentory.Domain.Constants.PlanLimits;

namespace Zentory.Application.Billing.Queries;

public record GetPlanLimitsQuery : IRequest<PlanLimitsDto>;

public record PlanLimitsDto(
    int?  MaxClients,
    int?  MaxInvoicesMonth,
    int?  MaxOrgMembers,
    int?  MaxProjects,
    int?  MaxCollaborators);

public sealed class GetPlanLimitsQueryHandler : IRequestHandler<GetPlanLimitsQuery, PlanLimitsDto>
{
    private readonly IPlanLimitService _limits;
    private readonly ITenantContext    _tenant;

    public GetPlanLimitsQueryHandler(IPlanLimitService limits, ITenantContext tenant)
    {
        _limits = limits;
        _tenant = tenant;
    }

    public async Task<PlanLimitsDto> Handle(GetPlanLimitsQuery request, CancellationToken ct)
    {
        var plan        = _tenant.Plan;
        var accountType = _tenant.AccountType;

        var t0 = _limits.GetLimitAsync(plan, accountType, FeatureKeys.MaxClients,       ct);
        var t1 = _limits.GetLimitAsync(plan, accountType, FeatureKeys.MaxInvoicesMonth, ct);
        var t2 = _limits.GetLimitAsync(plan, accountType, FeatureKeys.MaxOrgMembers,    ct);
        var t3 = _limits.GetLimitAsync(plan, accountType, FeatureKeys.MaxProjects,      ct);
        var t4 = _limits.GetLimitAsync(plan, accountType, FeatureKeys.MaxCollaborators, ct);

        await Task.WhenAll(t0, t1, t2, t3, t4);

        return new PlanLimitsDto(t0.Result, t1.Result, t2.Result, t3.Result, t4.Result);
    }
}
