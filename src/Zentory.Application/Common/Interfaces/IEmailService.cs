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
}
