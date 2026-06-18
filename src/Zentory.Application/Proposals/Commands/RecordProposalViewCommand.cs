using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

// Initials shown in the activity log for events that have no authenticated user.
file static class PublicActor
{
    internal const string ClientView  = "CLI";   // client opened the public link
    internal const string EmailNotify = "ENV";   // system sent the email notification
}

public record RecordProposalViewCommand(Guid PublicToken) : IRequest;

public sealed class RecordProposalViewCommandHandler : IRequestHandler<RecordProposalViewCommand>
{
    private readonly IProposalRepository  _proposals;
    private readonly IClientRepository   _clients;
    private readonly IUserRepository     _users;
    private readonly IUnitOfWork         _uow;
    private readonly IEmailService       _email;
    private readonly IActivityLogService _activityLog;
    private readonly IApplicationSettings _settings;

    public RecordProposalViewCommandHandler(
        IProposalRepository  proposals,
        IClientRepository    clients,
        IUserRepository      users,
        IUnitOfWork          uow,
        IEmailService        email,
        IActivityLogService  activityLog,
        IApplicationSettings settings)
    {
        _proposals   = proposals;
        _clients     = clients;
        _users       = users;
        _uow         = uow;
        _email       = email;
        _activityLog = activityLog;
        _settings    = settings;
    }

    private static string ClientInitials(string? clientName)
    {
        if (string.IsNullOrWhiteSpace(clientName)) return PublicActor.ClientView;
        var words = clientName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2
            ? $"{char.ToUpper(words[0][0])}{char.ToUpper(words[1][0])}"
            : clientName[..Math.Min(2, clientName.Length)].ToUpper();
    }

    public async Task Handle(RecordProposalViewCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByPublicTokenAsync(request.PublicToken, cancellationToken);
        if (proposal is null) return;

        var viewNumber  = proposal.ViewCount + 1;
        var isFirstView = proposal.ViewCount == 0;

        // Resolve client name once for use in log initials
        var client        = await _clients.GetByIdAsync(proposal.ClientId, cancellationToken);
        var clientInitials = ClientInitials(client?.Name);

        proposal.RecordView();
        await _proposals.UpdateAsync(proposal, cancellationToken);

        await _activityLog.LogPublicAsync(
            organizationId: proposal.OrganizationId,
            entityType:     "Proposal",
            entityId:       proposal.Id,
            userInitials:   clientInitials,
            action:         viewNumber == 1
                                ? "El cliente abrió la propuesta por primera vez"
                                : $"El cliente abrió la propuesta (apertura #{viewNumber})",
            ct: cancellationToken);

        // Resolve creator before saving so we can log the email entry in the same SaveChanges
        string? creatorEmail = null;
        string? creatorName  = null;
        string? dashboardUrl = null;

        if (isFirstView && proposal.CreatedBy.HasValue)
        {
            var creator = await _users.GetByIdAsync(proposal.CreatedBy.Value, cancellationToken);
            if (creator is not null)
            {
                creatorEmail = creator.Email;
                creatorName  = creator.FirstName;
                dashboardUrl = $"{_settings.BaseUrl}/proposals/{proposal.Id}";

                await _activityLog.LogPublicAsync(
                    organizationId: proposal.OrganizationId,
                    entityType:     "Proposal",
                    entityId:       proposal.Id,
                    userInitials:   PublicActor.EmailNotify,
                    action:         $"Notificación de apertura enviada a {creatorEmail}",
                    ct: cancellationToken);
            }
        }

        // Single save covers: view update + view log + (optional) email log
        await _uow.SaveChangesAsync(cancellationToken);

        // Fire-and-forget only the external email call (not any DB work)
        if (creatorEmail is not null)
            _ = _email.SendProposalViewedNotificationAsync(
                creatorEmail, creatorName!, proposal.Title, dashboardUrl!, CancellationToken.None);
    }
}
