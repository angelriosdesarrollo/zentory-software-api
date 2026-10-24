using System.Collections.Concurrent;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Tests.Support;

// Doble de IEmailService para el host de pruebas: nunca llama a Resend. Registra cada correo (tipo y destinatario)
// para que un test pueda afirmar que se envió, sin depender del contenido de la plantilla.
public sealed class FakeEmailService : IEmailService
{
    public sealed record SentEmail(string Kind, string ToEmail);

    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyCollection<SentEmail> Sent => _sent.ToArray();

    public Task SendProposalEmailAsync(
        string toEmail, string proposalTitle, string clientName, string proposalUrl, string? customMessage,
        CancellationToken ct = default)
        => Record(nameof(SendProposalEmailAsync), toEmail, ct);

    public Task SendProposalViewedNotificationAsync(
        string toEmail, string recipientName, string proposalTitle, string proposalDashboardUrl,
        CancellationToken ct = default)
        => Record(nameof(SendProposalViewedNotificationAsync), toEmail, ct);

    public Task SendPilaRequestEmailAsync(
        string toEmail, string collaboratorName, string companyName, string period, string uploadUrl,
        DateTime expiresAt, CancellationToken ct = default)
        => Record(nameof(SendPilaRequestEmailAsync), toEmail, ct);

    public Task SendPayoutInvoiceGeneratedEmailAsync(
        string toEmail, string collaboratorName, string companyName, string period, decimal amount,
        string currency, string downloadUrl, string portalUrl, CancellationToken ct = default)
        => Record(nameof(SendPayoutInvoiceGeneratedEmailAsync), toEmail, ct);

    public Task SendPayoutInvoiceRequestEmailAsync(
        string toEmail, string collaboratorName, string companyName, string period, string uploadUrl,
        DateTime expiresAt, CancellationToken ct = default)
        => Record(nameof(SendPayoutInvoiceRequestEmailAsync), toEmail, ct);

    public Task SendCollaboratorPortalAccessEmailAsync(
        string toEmail, string collaboratorName, string magicLinkUrl, DateTime expiresAt,
        CancellationToken ct = default)
        => Record(nameof(SendCollaboratorPortalAccessEmailAsync), toEmail, ct);

    public Task SendPilaRejectedEmailAsync(
        string toEmail, string collaboratorName, string companyName, string period, string notes,
        string portalUrl, CancellationToken ct = default)
        => Record(nameof(SendPilaRejectedEmailAsync), toEmail, ct);

    public Task SendPayoutInvoiceRejectedEmailAsync(
        string toEmail, string collaboratorName, string companyName, string period, string notes,
        string portalUrl, CancellationToken ct = default)
        => Record(nameof(SendPayoutInvoiceRejectedEmailAsync), toEmail, ct);

    private Task Record(string kind, string toEmail, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _sent.Enqueue(new SentEmail(kind, toEmail));
        return Task.CompletedTask;
    }
}
