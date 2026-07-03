using System.Threading.RateLimiting;

namespace Zentory.API.Extensions;

public static class RateLimitingExtensions
{
    // A diferencia de Proposal/ProjectShare (solo lectura), PublicPilaController y
    // PublicPayoutInvoicesController aceptan escritura (upload) sin sesión — el
    // token es la única barrera, así que se limita por IP para frenar fuerza bruta.
    public static IServiceCollection AddPublicRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("PublicUploadPolicy", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(10),
                        SegmentsPerWindow = 5,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                    }));

            // request-link dispara un correo y exchange acepta un token guessable-by-bruteforce —
            // más estricto que PublicUploadPolicy y sin cola (evita spam de bandeja de entrada).
            options.AddPolicy("CollaboratorPortalAuthPolicy", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(10),
                        SegmentsPerWindow = 5,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}
