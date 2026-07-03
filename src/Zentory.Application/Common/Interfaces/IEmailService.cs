namespace Zentory.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendProposalEmailAsync(
        string    toEmail,
        string    proposalTitle,
        string    clientName,
        string    proposalUrl,
        string?   customMessage,
        CancellationToken ct = default);

    Task SendProposalViewedNotificationAsync(
        string    toEmail,
        string    recipientName,
        string    proposalTitle,
        string    proposalDashboardUrl,
        CancellationToken ct = default);

    Task SendPilaRequestEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        string    uploadUrl,
        DateTime  expiresAt,
        CancellationToken ct = default);

    Task SendPayoutInvoiceGeneratedEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        decimal   amount,
        string    currency,
        string    downloadUrl,
        string    portalUrl,
        CancellationToken ct = default);

    Task SendPayoutInvoiceRequestEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        string    uploadUrl,
        DateTime  expiresAt,
        CancellationToken ct = default);

    Task SendCollaboratorPortalAccessEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    magicLinkUrl,
        DateTime  expiresAt,
        CancellationToken ct = default);
}
