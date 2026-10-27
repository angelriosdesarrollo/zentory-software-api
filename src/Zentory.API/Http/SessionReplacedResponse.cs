using Zentory.Application.Exceptions;

namespace Zentory.API.Http;

// Único escritor del 401 de sesión reemplazada (PLAN-0018 RF-90 d, e; RNF-10; architecture §2.2, D-47). El cuerpo es
// exacto, byte a byte, en POST /auth/refresh (vía GlobalExceptionHandlingMiddleware) y en el middleware de membresía:
// solo error y code, en ese orden, sin message ni details, y sin cabeceras propias. No lleva ni el sid, ni la IP, ni el
// User-Agent, ni la hora o el método del inicio de sesión nuevo.
public static class SessionReplacedResponse
{
    public const string Body = "{\"error\":\"unauthorized\",\"code\":\"" + SessionCodes.SessionReplaced + "\"}";

    public const string ContentType = "application/json";

    public static Task WriteAsync(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.StatusCode  = StatusCodes.Status401Unauthorized;
        response.ContentType = ContentType;
        return response.WriteAsync(Body);
    }
}
