using System.Net;
using System.Text.Json;
using Zentory.API.Authorization;

namespace Zentory.Tests.Support;

// Aserción del 403 con cuerpo de las policies (PLAN-0018 RF-58; D-8): { error: "forbidden", message, code,
// requiredPlan, details }, con el código y el plan requerido esperados.
public static class ForbiddenAssert
{
    public static async Task HasCodeAsync(HttpResponseMessage response, string code, string? requiredPlan = null)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("forbidden", root.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        Assert.Equal(code, root.GetProperty("code").GetString());

        var plan = root.GetProperty("requiredPlan");
        if (requiredPlan is null)
            Assert.Equal(JsonValueKind.Null, plan.ValueKind);
        else
            Assert.Equal(requiredPlan, plan.GetString());
    }

    public static Task IsAccessDeniedAsync(HttpResponseMessage response)
        => HasCodeAsync(response, ZentoryAuthorizationResultHandler.AccessDeniedCode);

    public static Task IsPlanRequiredAsync(HttpResponseMessage response, string requiredPlan)
        => HasCodeAsync(response, ZentoryAuthorizationResultHandler.PlanRequiredCode, requiredPlan);
}
