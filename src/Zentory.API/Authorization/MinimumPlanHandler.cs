using Microsoft.AspNetCore.Authorization;
using Zentory.Application.Common.Authorization;
using Zentory.Domain.Constants;

namespace Zentory.API.Authorization;

public sealed class MinimumPlanHandler : AuthorizationHandler<MinimumPlanRequirement>
{
    private static readonly string[] PlanOrder = [Plan.Free, Plan.Pro, Plan.Studio];

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumPlanRequirement      requirement)
    {
        var plan = context.User.FindFirst("plan")?.Value;

        if (plan is not null &&
            Array.IndexOf(PlanOrder, plan) >= Array.IndexOf(PlanOrder, requirement.RequiredPlan))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
