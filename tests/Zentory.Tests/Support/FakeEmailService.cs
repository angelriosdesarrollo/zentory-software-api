using System.Collections.Concurrent;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Tests.Support;

// Doble de IEmailService para el host de pruebas: nunca llama a Resend. Registra cada correo (tipo y destinatario)
// para que un test pueda afirmar que se envió, sin depender del contenido de la plantilla.
public sealed class FakeEmailService : IEmailService
{
    // OrganizationName y RestoreDeadlineUtc solo los llenan los avisos de eliminación de la organización (RF-65 f). Url
    // solo la llenan los correos con enlace de un solo uso (reset, verificación e invitación, PLAN-0018 RF-20, RF-23,
    // RF-49), para que el test canjee el token. La invitación llena además OrganizationName, InviterName, InviteeName y
    // Role (RF-49 c): los valores tal como llegan a la plantilla, sin codificar. Los dos correos de la propuesta de
    // transferencia (RF-64 c) llenan OrganizationName, ExpiresAtUtc y OtherPartyName (el proponente en el del destino, el
    // destino en el aviso al owner); no llevan Url.
    public sealed record SentEmail(
        string Kind, string ToEmail, string? OrganizationName = null, DateTime? RestoreDeadlineUtc = null, string? Url = null,
        string? InviterName = null, string? InviteeName = null, string? Role = null, DateTime? ExpiresAtUtc = null,
        string? OtherPartyName = null);

    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyCollection<SentEmail> Sent => _sent.ToArray();

    // Simula a un proveedor lento (PLAN-0018 CA-29): cada envío espera esto antes de registrarse. Por defecto, nada.
    public TimeSpan SendDelay { get; set; } = TimeSpan.Zero;

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

    public Task SendReauthenticationFailureWarningAsync(
        string toEmail, string recipientName, DateTime occurredAtUtc, CancellationToken ct = default)
        => Record(nameof(SendReauthenticationFailureWarningAsync), toEmail, ct);

    public Task SendOrganizationDeletedNoticeAsync(
        string toEmail, string recipientName, string organizationName, CancellationToken ct = default)
        => Record(new SentEmail(nameof(SendOrganizationDeletedNoticeAsync), toEmail, organizationName), ct);

    public Task SendOrganizationDeletedOwnerNoticeAsync(
        string toEmail, string recipientName, string organizationName, DateTime restoreDeadlineUtc,
        CancellationToken ct = default)
        => Record(new SentEmail(nameof(SendOrganizationDeletedOwnerNoticeAsync), toEmail, organizationName, restoreDeadlineUtc), ct);

    public Task SendPasswordResetEmailAsync(
        string toEmail, string recipientName, string resetUrl, DateTime expiresAtUtc, CancellationToken ct = default)
        => Record(new SentEmail(nameof(SendPasswordResetEmailAsync), toEmail, Url: resetUrl), ct);

    public Task SendEmailVerificationEmailAsync(
        string toEmail, string recipientName, string verifyUrl, DateTime expiresAtUtc, CancellationToken ct = default)
        => Record(new SentEmail(nameof(SendEmailVerificationEmailAsync), toEmail, Url: verifyUrl), ct);

    public Task SendOrganizationInvitationEmailAsync(
        string toEmail, string? inviteeName, string inviterName, string organizationName, string role, string acceptUrl,
        DateTime expiresAtUtc, CancellationToken ct = default)
        => Record(new SentEmail(nameof(SendOrganizationInvitationEmailAsync), toEmail, organizationName, Url: acceptUrl,
            InviterName: inviterName, InviteeName: inviteeName, Role: role, ExpiresAtUtc: expiresAtUtc), ct);

    public Task SendOwnershipTransferProposedEmailAsync(
        string toEmail, string recipientName, string proposerName, string organizationName, DateTime expiresAtUtc,
        CancellationToken ct = default)
        => Record(new SentEmail(nameof(SendOwnershipTransferProposedEmailAsync), toEmail, organizationName,
            ExpiresAtUtc: expiresAtUtc, OtherPartyName: proposerName), ct);

    public Task SendOwnershipTransferProposedOwnerNoticeAsync(
        string toEmail, string recipientName, string targetName, string organizationName, DateTime expiresAtUtc,
        CancellationToken ct = default)
        => Record(new SentEmail(nameof(SendOwnershipTransferProposedOwnerNoticeAsync), toEmail, organizationName,
            ExpiresAtUtc: expiresAtUtc, OtherPartyName: targetName), ct);

    private Task Record(string kind, string toEmail, CancellationToken ct) => Record(new SentEmail(kind, toEmail), ct);

    private async Task Record(SentEmail email, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (SendDelay > TimeSpan.Zero)
            await Task.Delay(SendDelay, ct);
        _sent.Enqueue(email);
    }
}
