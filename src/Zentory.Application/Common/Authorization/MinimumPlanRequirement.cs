using Microsoft.AspNetCore.Authorization;

namespace Zentory.Application.Common.Authorization;

public sealed record MinimumPlanRequirement(string RequiredPlan) : IAuthorizationRequirement;
