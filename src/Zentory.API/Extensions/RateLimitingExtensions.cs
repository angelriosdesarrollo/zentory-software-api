using System.Threading.RateLimiting;
using Zentory.Application.Common.Interfaces;
using Zentory.Infrastructure.Services;

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

    // Limitador de [AuthRateLimit] (PLAN-0018 D-13, RF-29): un único AuthAttemptLimiter por proceso con las políticas de
    // AuthRateLimitPolicies (reautenticación, organización y los endpoints de AuthController), sobrescribibles en
    // RateLimiting:Auth:{Política}:{Partición}:PermitLimit|Window y RateLimiting:Organization:*. Un valor inválido
    // detiene la resolución del limitador con la clave en el mensaje.
    //
    // La IP de las claves es RemoteIpAddress. No se registra el middleware de cabeceras reenviadas: con él, un X-Forwarded-For del cliente
    // reemplazaría la IP y cada petición tendría su propio cupo. La IP real detrás del proxy es de PLAN-0002 (G-5).
    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddSingleton(sp => new AuthAttemptLimiter(
            sp.GetRequiredService<TimeProvider>(),
            AuthRateLimitPolicies.FromConfiguration(sp.GetRequiredService<IConfiguration>())));
        // El techo por email de las invitaciones lo consume el comando, no el filtro (SR-0018.22).
        services.AddScoped<IInvitationEmailLimiter, InvitationEmailLimiter>();

        return services;
    }
}
