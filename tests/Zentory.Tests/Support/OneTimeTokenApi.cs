using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Hangfire.Storage;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Apoyo de los tests de los tokens de un solo uso de reset y verificación (PLAN-0018 RF-20 a RF-25; SR-0018.10; D-11):
// las llamadas a register, forgot-password, reset-password, verify-email y resend-verification, la espera del correo
// que entrega la cola en proceso (IAuthEmailQueue), la lectura del token de la URL del correo, las aserciones de 422 y
// el recorrido de lo que el host persiste (entidades y Hangfire) para CA-80.
public static class OneTimeTokenApi
{
    public const string RegisterEndpoint = "/api/v1/auth/register";
    public const string ForgotEndpoint   = "/api/v1/auth/forgot-password";
    public const string ResetEndpoint    = "/api/v1/auth/reset-password";
    public const string VerifyEndpoint   = "/api/v1/auth/verify-email";
    public const string ResendEndpoint   = "/api/v1/auth/resend-verification";

    public const string ResetKind        = nameof(FakeEmailService.SendPasswordResetEmailAsync);
    public const string VerificationKind = nameof(FakeEmailService.SendEmailVerificationEmailAsync);

    private const string TokenMarker = "?token=";

    // La cola corre en el host: se espera por condición, con un tope de seguridad que no mide rendimiento.
    private static readonly TimeSpan WaitTimeout  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    public static Task<HttpResponseMessage> ForgotAsync(HttpClient http, string email)
        => http.PostAsJsonAsync(ForgotEndpoint, new { email });

    public static Task<HttpResponseMessage> ResetAsync(HttpClient http, string token, string newPassword)
        => http.PostAsJsonAsync(ResetEndpoint, new { token, newPassword });

    public static Task<HttpResponseMessage> VerifyAsync(HttpClient http, string token)
        => http.PostAsJsonAsync(VerifyEndpoint, new { token });

    // Registro válido de una cuenta nueva con el email y la contraseña pedidos (exige el plan free sembrado).
    public static Task<HttpResponseMessage> RegisterAsync(HttpClient http, string email, string password)
        => http.PostAsJsonAsync(RegisterEndpoint, new
        {
            email,
            password,
            firstName     = "Registro",
            lastName      = "Prueba",
            orgName       = $"Org registro {Guid.NewGuid():N}",
            legalType     = LegalType.Freelance,
            acceptedTerms = true,
        });

    public static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@zentory.test";

    // Espera el correo número expected (desde 1) de ese tipo para ese destinatario y lo devuelve.
    public static async Task<FakeEmailService.SentEmail> WaitForEmailAsync(
        FakeEmailService emails, string kind, string toEmail, int expected = 1)
    {
        List<FakeEmailService.SentEmail> Matching() => emails.Sent
            .Where(sent => sent.Kind == kind && sent.ToEmail == toEmail)
            .ToList();

        await WaitUntilAsync(() => Matching().Count >= expected, $"La cola no entregó el correo {kind} a tiempo.");
        return Matching()[expected - 1];
    }

    // Espera por condición en el pool de hilos, con una última comprobación al vencer el tope (como AuthEmailQueueTests).
    public static async Task WaitUntilAsync(Func<bool> condition, string failureMessage)
    {
        var reached = await Task.Run(async () =>
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < WaitTimeout)
            {
                if (condition())
                    return true;
                await Task.Delay(PollInterval);
            }
            return condition();
        });

        Assert.True(reached, failureMessage);
    }

    // El valor del parámetro token de la URL del correo, decodificado.
    public static string TokenOf(FakeEmailService.SentEmail sent)
    {
        Assert.NotNull(sent.Url);
        var marker = sent.Url!.IndexOf(TokenMarker, StringComparison.Ordinal);
        Assert.True(marker > 0, "El correo no trae el enlace con el token.");
        return Uri.UnescapeDataString(sent.Url[(marker + TokenMarker.Length)..]);
    }

    public static async Task AssertValidationErrorAsync(HttpResponseMessage response, string field)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("validation_error", body.RootElement.GetProperty("error").GetString());
            Assert.Contains(body.RootElement.GetProperty("details").EnumerateArray(),
                detail => string.Equals(detail.GetProperty("field").GetString(), field, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static async Task AssertTokenInvalidAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("token_invalid", body.RootElement.GetProperty("error").GetString());
            Assert.Equal("TOKEN_INVALID", body.RootElement.GetProperty("code").GetString());
        }
    }

    // Lecturas y siembra de filas fuera de una petición, en un scope propio del host.
    public static async Task SeedEntitiesAsync(IServiceProvider services, params object[] entities)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    public static async Task<User> UserRowAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.UserId == userId);
    }

    public static async Task<List<EmailVerificationToken>> VerificationTokensOfAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.EmailVerificationTokens.AsNoTracking().Where(t => t.UserId == userId).ToListAsync();
    }

    public static async Task<List<PasswordResetToken>> ResetTokensOfAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.PasswordResetTokens.AsNoTracking().Where(t => t.UserId == userId).ToListAsync();
    }

    // Toda propiedad string (también las sombra y las de tipos propios) de toda entidad del contexto, sin filtros globales.
    public static async Task<List<string>> EntityStringsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        foreach (var type in db.Model.GetEntityTypes().Where(t => !t.IsOwned() && !t.HasSharedClrType && t.FindPrimaryKey() is not null))
            await (Task)LoadAllMethod.MakeGenericMethod(type.ClrType).Invoke(null, [db])!;

        return db.ChangeTracker.Entries()
            .SelectMany(entry => entry.Properties
                .Where(property => property.Metadata.ClrType == typeof(string))
                .Select(property => property.CurrentValue as string))
            .OfType<string>()
            .ToList();
    }

    private static readonly MethodInfo LoadAllMethod =
        typeof(OneTimeTokenApi).GetMethod(nameof(LoadAllAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task LoadAllAsync<TEntity>(DbContext db) where TEntity : class
        => db.Set<TEntity>().IgnoreQueryFilters().LoadAsync();

    // Lo que guarda el storage de Hangfire: jobs en todos sus estados (método, argumentos, propiedades e historial) y
    // los recurrentes.
    public static List<string> HangfireStrings(JobStorage storage)
    {
        const int Page = 10_000;
        var api = storage.GetMonitoringApi();
        var ids = new List<string>();
        foreach (var queue in api.Queues())
        {
            ids.AddRange(api.EnqueuedJobs(queue.Name, 0, Page).Select(job => job.Key));
            ids.AddRange(api.FetchedJobs(queue.Name, 0, Page).Select(job => job.Key));
        }
        ids.AddRange(api.ScheduledJobs(0, Page).Select(job => job.Key));
        ids.AddRange(api.ProcessingJobs(0, Page).Select(job => job.Key));
        ids.AddRange(api.SucceededJobs(0, Page).Select(job => job.Key));
        ids.AddRange(api.FailedJobs(0, Page).Select(job => job.Key));
        ids.AddRange(api.DeletedJobs(0, Page).Select(job => job.Key));

        var values = new List<string>();
        foreach (var id in ids.Distinct())
        {
            values.Add(id);
            var details = api.JobDetails(id);
            if (details is null) continue;
            if (details.Job is not null)
            {
                values.Add(details.Job.ToString());
                values.AddRange(details.Job.Args.Select(arg => JsonSerializer.Serialize(arg)));
            }
            values.AddRange(details.Properties?.Values ?? []);
            foreach (var state in details.History ?? [])
            {
                values.Add(state.StateName ?? string.Empty);
                values.Add(state.Reason ?? string.Empty);
                values.AddRange(state.Data?.Values ?? []);
            }
        }

        using var connection = storage.GetConnection();
        foreach (var recurring in connection.GetRecurringJobs())
        {
            values.Add(recurring.Id);
            if (recurring.Job is not null)
                values.AddRange(recurring.Job.Args.Select(arg => JsonSerializer.Serialize(arg)));
        }
        return values;
    }
}
