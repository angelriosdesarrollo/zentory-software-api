using Microsoft.AspNetCore.Authorization;

namespace Zentory.API.Authorization;

public sealed record MinimumPlanRequirement(string RequiredPlan) : IAuthorizationRequirement;
