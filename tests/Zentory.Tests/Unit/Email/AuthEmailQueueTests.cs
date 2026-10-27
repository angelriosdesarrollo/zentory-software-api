using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zentory.Application.Common.Interfaces;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Email;

// PLAN-0018 CA-T10-2 (parte de CA-29; D-14, RF-22, RF-23, SR-0018.10): IAuthEmailQueue entrega a IEmailService fuera
// del request, en un scope propio, y no registra el contenido del correo.
public sealed class AuthEmailQueueTests
{
    private const string Kind = "probe-access-link";

    private static readonly TimeSpan SendDelay   = TimeSpan.FromSeconds(2);
    // Tope de seguridad, no de rendimiento: la espera termina en cuanto se cumple la condición.
    private static readonly TimeSpan WaitTimeout  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    [Fact]
    public async Task PLAN0018_CA29_Encolar_ConEmailServiceQueTarda2s_RespondeSinEsperarYElCorreoLlegaDespues()
    {
        await using var host = await QueueHost.StartAsync(delay: SendDelay);
        var queue   = host.Services.GetRequiredService<IAuthEmailQueue>();
        var content = SensitiveContent.Create();

        var stopwatch = Stopwatch.StartNew();
        var accepted  = EnqueueAccessLink(queue, content);
        stopwatch.Stop();

        Assert.True(accepted);
        Assert.True(stopwatch.Elapsed < SendDelay / 2,
            $"Encolar tardó {stopwatch.ElapsedMilliseconds} ms: esperó el envío.");
        Assert.Empty(host.Emails.Sent);

        await WaitUntilAsync(() => host.Emails.Sent.Count == 1);

        var sent = Assert.Single(host.Emails.Sent);
        Assert.Equal(nameof(IEmailService.SendCollaboratorPortalAccessEmailAsync), sent.Kind);
        Assert.Equal(content.ToEmail, sent.ToEmail);
    }

    [Fact]
    public async Task PLAN0018_CA29_Enviar_NoRegistraElContenidoDelCorreoEnLosLogs()
    {
        await using var host = await QueueHost.StartAsync(delay: TimeSpan.Zero);
        var queue   = host.Services.GetRequiredService<IAuthEmailQueue>();
        var content = SensitiveContent.Create();

        EnqueueAccessLink(queue, content);
        await WaitUntilAsync(() => host.Emails.Sent.Count == 1);

        AssertLogsDoNotContain(host.Logs, content);
    }

    [Fact]
    public async Task PLAN0018_CA29_EnvioFallido_RegistraSoloLaEtiquetaYElTipoDeError_SinElContenido()
    {
        await using var host = await QueueHost.StartAsync(delay: TimeSpan.Zero);
        var queue   = host.Services.GetRequiredService<IAuthEmailQueue>();
        var content = SensitiveContent.Create();

        // El proveedor real puede devolver un error que repite el destinatario o el enlace.
        queue.TryEnqueue(Kind, (_, _) => throw new InvalidOperationException(
            $"Resend rejected {content.ToEmail} {content.MagicLinkUrl}"));

        await WaitUntilAsync(() => host.Logs.Entries.Any(entry => entry.Level == LogLevel.Error));

        var error = Assert.Single(host.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(Kind, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), error.Message, StringComparison.Ordinal);
        AssertLogsDoNotContain(host.Logs, content);

        // La cola sigue viva después del fallo.
        EnqueueAccessLink(queue, content);
        await WaitUntilAsync(() => host.Emails.Sent.Count == 1);
    }

    [Fact]
    public async Task PLAN0018_CA29_HostDePruebas_RegistraLaColaComoUnicaInstanciaYServicioEnSegundoPlano()
    {
        await using var factory = new ZentoryApiFactory();

        var queue = factory.Services.GetRequiredService<IAuthEmailQueue>();

        Assert.Same(factory.Services.GetRequiredService<AuthEmailQueue>(), queue);
        Assert.Contains(factory.Services.GetServices<IHostedService>(), service => ReferenceEquals(service, queue));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static bool EnqueueAccessLink(IAuthEmailQueue queue, SensitiveContent content)
        => queue.TryEnqueue(Kind, (email, ct) => email.SendCollaboratorPortalAccessEmailAsync(
            content.ToEmail, content.Name, content.MagicLinkUrl, content.ExpiresAt, ct));

    private static void AssertLogsDoNotContain(CapturingLoggerProvider logs, SensitiveContent content)
    {
        foreach (var entry in logs.Entries)
        {
            Assert.DoesNotContain(content.ToEmail, entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(content.Name, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(content.Token, entry.Message, StringComparison.Ordinal);
        }
    }

    // Espera activa por condición en el pool de hilos, fuera del contexto de sincronización de xUnit (ver QueueHost), y
    // con una última comprobación al vencer el tope: un sondeo que se reanuda tarde no falla si la condición ya se cumplió.
    private static async Task WaitUntilAsync(Func<bool> condition)
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

        Assert.True(reached, "La cola no procesó el correo a tiempo.");
    }

    private sealed record SensitiveContent(string ToEmail, string Name, string Token, string MagicLinkUrl, DateTime ExpiresAt)
    {
        public static SensitiveContent Create()
        {
            var token = Guid.NewGuid().ToString("N");
            return new SensitiveContent(
                $"dest-{token[..8]}@zentory.test", $"Destinatario {token[..6]}", token,
                $"https://app.zentory.test/portal/access?token={token}", new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
        }
    }

    // Cola real sobre un contenedor mínimo: FakeEmailService detrás de un decorador que tarda, y los logs capturados.
    private sealed class QueueHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AuthEmailQueue  _queue;

        private QueueHost(ServiceProvider provider)
        {
            _provider = provider;
            _queue    = provider.GetRequiredService<AuthEmailQueue>();
        }

        public IServiceProvider        Services => _provider;
        public FakeEmailService        Emails   => _provider.GetRequiredService<FakeEmailService>();
        public CapturingLoggerProvider Logs     => _provider.GetRequiredService<CapturingLoggerProvider>();

        public static async Task<QueueHost> StartAsync(TimeSpan delay)
        {
            var logs     = new CapturingLoggerProvider();
            var services = new ServiceCollection();
            services.AddSingleton(logs);
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            services.AddSingleton<FakeEmailService>();
            services.AddScoped<IEmailService>(sp => new SlowEmailService(sp.GetRequiredService<FakeEmailService>(), delay));
            services.AddSingleton<AuthEmailQueue>();
            services.AddSingleton<IAuthEmailQueue>(sp => sp.GetRequiredService<AuthEmailQueue>());

            var host = new QueueHost(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
            // Se arranca desde el pool de hilos y no desde el hilo del test: ExecuteAsync captura el contexto de
            // sincronización de quien lo arranca, y el de xUnit tiene tantos hilos como tests en paralelo. Arrancada en
            // él, la cola dependía de que esos hilos quedaran libres, y con la suite completa cargada podía quedarse sin
            // procesar más de 15 s (la intermitencia de EnvioFallido). Como en el host real, la cola corre en el pool.
            await Task.Run(() => host._queue.StartAsync(CancellationToken.None));
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            await _queue.StopAsync(CancellationToken.None);
            await _provider.DisposeAsync();
        }
    }

    // IEmailService que tarda antes de delegar: simula a Resend lento (CA-29).
    private sealed class SlowEmailService(IEmailService inner, TimeSpan delay) : IEmailService
    {
        public async Task SendProposalEmailAsync(string toEmail, string proposalTitle, string clientName,
            string proposalUrl, string? customMessage, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendProposalEmailAsync(toEmail, proposalTitle, clientName, proposalUrl, customMessage, ct);
        }

        public async Task SendProposalViewedNotificationAsync(string toEmail, string recipientName,
            string proposalTitle, string proposalDashboardUrl, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendProposalViewedNotificationAsync(toEmail, recipientName, proposalTitle, proposalDashboardUrl, ct);
        }

        public async Task SendPilaRequestEmailAsync(string toEmail, string collaboratorName, string companyName,
            string period, string uploadUrl, DateTime expiresAt, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendPilaRequestEmailAsync(toEmail, collaboratorName, companyName, period, uploadUrl, expiresAt, ct);
        }

        public async Task SendPayoutInvoiceGeneratedEmailAsync(string toEmail, string collaboratorName,
            string companyName, string period, decimal amount, string currency, string downloadUrl, string portalUrl,
            CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendPayoutInvoiceGeneratedEmailAsync(toEmail, collaboratorName, companyName, period, amount,
                currency, downloadUrl, portalUrl, ct);
        }

        public async Task SendPayoutInvoiceRequestEmailAsync(string toEmail, string collaboratorName,
            string companyName, string period, string uploadUrl, DateTime expiresAt, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendPayoutInvoiceRequestEmailAsync(toEmail, collaboratorName, companyName, period, uploadUrl,
                expiresAt, ct);
        }

        public async Task SendCollaboratorPortalAccessEmailAsync(string toEmail, string collaboratorName,
            string magicLinkUrl, DateTime expiresAt, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendCollaboratorPortalAccessEmailAsync(toEmail, collaboratorName, magicLinkUrl, expiresAt, ct);
        }

        public async Task SendPilaRejectedEmailAsync(string toEmail, string collaboratorName, string companyName,
            string period, string notes, string portalUrl, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendPilaRejectedEmailAsync(toEmail, collaboratorName, companyName, period, notes, portalUrl, ct);
        }

        public async Task SendPayoutInvoiceRejectedEmailAsync(string toEmail, string collaboratorName,
            string companyName, string period, string notes, string portalUrl, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendPayoutInvoiceRejectedEmailAsync(toEmail, collaboratorName, companyName, period, notes,
                portalUrl, ct);
        }

        public async Task SendReauthenticationFailureWarningAsync(string toEmail, string recipientName,
            DateTime occurredAtUtc, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendReauthenticationFailureWarningAsync(toEmail, recipientName, occurredAtUtc, ct);
        }

        public async Task SendOrganizationDeletedNoticeAsync(string toEmail, string recipientName,
            string organizationName, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendOrganizationDeletedNoticeAsync(toEmail, recipientName, organizationName, ct);
        }

        public async Task SendOrganizationDeletedOwnerNoticeAsync(string toEmail, string recipientName,
            string organizationName, DateTime restoreDeadlineUtc, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendOrganizationDeletedOwnerNoticeAsync(toEmail, recipientName, organizationName,
                restoreDeadlineUtc, ct);
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string recipientName, string resetUrl,
            DateTime expiresAtUtc, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendPasswordResetEmailAsync(toEmail, recipientName, resetUrl, expiresAtUtc, ct);
        }

        public async Task SendEmailVerificationEmailAsync(string toEmail, string recipientName, string verifyUrl,
            DateTime expiresAtUtc, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendEmailVerificationEmailAsync(toEmail, recipientName, verifyUrl, expiresAtUtc, ct);
        }

        public async Task SendOrganizationInvitationEmailAsync(string toEmail, string? inviteeName, string inviterName,
            string organizationName, string role, string acceptUrl, DateTime expiresAtUtc, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendOrganizationInvitationEmailAsync(toEmail, inviteeName, inviterName, organizationName, role,
                acceptUrl, expiresAtUtc, ct);
        }

        public async Task SendOwnershipTransferProposedEmailAsync(string toEmail, string recipientName,
            string proposerName, string organizationName, DateTime expiresAtUtc, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendOwnershipTransferProposedEmailAsync(toEmail, recipientName, proposerName, organizationName,
                expiresAtUtc, ct);
        }

        public async Task SendOwnershipTransferProposedOwnerNoticeAsync(string toEmail, string recipientName,
            string targetName, string organizationName, DateTime expiresAtUtc, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            await inner.SendOwnershipTransferProposedOwnerNoticeAsync(toEmail, recipientName, targetName, organizationName,
                expiresAtUtc, ct);
        }
    }
}
