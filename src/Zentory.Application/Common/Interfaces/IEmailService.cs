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

    Task SendPilaRejectedEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        string    notes,
        string    portalUrl,
        CancellationToken ct = default);

    Task SendPayoutInvoiceRejectedEmailAsync(
        string    toEmail,
        string    collaboratorName,
        string    companyName,
        string    period,
        string    notes,
        string    portalUrl,
        CancellationToken ct = default);

    // Aviso al usuario del tercer fallo de reautenticación en la ventana de reauth:{sub} (PLAN-0018 RF-81 g; SR-0018.42).
    // Sin la credencial ni el nonce; el nombre se codifica en la plantilla.
    Task SendReauthenticationFailureWarningAsync(
        string    toEmail,
        string    recipientName,
        DateTime  occurredAtUtc,
        CancellationToken ct = default);

    // Eliminación de la organización (PLAN-0018 RF-65 f; D-39; SR-0018.42): aviso a cada admin y member vigente. Sin
    // tokens ni enlaces con token; los nombres se codifican en la plantilla (SR-0018.22).
    Task SendOrganizationDeletedNoticeAsync(
        string    toEmail,
        string    recipientName,
        string    organizationName,
        CancellationToken ct = default);

    // Al owner: "Se eliminó <organización>; puedes restaurarla hasta <fecha>", para que se entere aunque la haya
    // eliminado otro con su sesión. restoreDeadlineUtc = DeletedAt + Organizations:PurgeAfterDays.
    Task SendOrganizationDeletedOwnerNoticeAsync(
        string    toEmail,
        string    recipientName,
        string    organizationName,
        DateTime  restoreDeadlineUtc,
        CancellationToken ct = default);

    // Enlace de reset de contraseña (PLAN-0018 RF-23, RF-25; SR-0018.10, SR-0018.22): resetUrl es
    // {App:BaseUrl}/reset-password?token=… y lleva el token en claro, así que solo viaja por IAuthEmailQueue (nunca por
    // Hangfire) y no se registra. Los valores se codifican en la plantilla. expiresAtUtc es el vencimiento del token.
    Task SendPasswordResetEmailAsync(
        string    toEmail,
        string    recipientName,
        string    resetUrl,
        DateTime  expiresAtUtc,
        CancellationToken ct = default);

    // Enlace de verificación de email (PLAN-0018 RF-20, RF-25; SR-0018.10, SR-0018.11 b, SR-0018.22): verifyUrl es
    // {App:BaseUrl}/verify-email?token=… y lleva el token en claro, así que solo viaja por IAuthEmailQueue (nunca por
    // Hangfire) y no se registra. Los valores se codifican en la plantilla. expiresAtUtc es el vencimiento del token.
    Task SendEmailVerificationEmailAsync(
        string    toEmail,
        string    recipientName,
        string    verifyUrl,
        DateTime  expiresAtUtc,
        CancellationToken ct = default);

    // Invitación a una organización (PLAN-0018 RF-49 c, RF-25; SR-0018.10, SR-0018.22): acceptUrl es
    // {App:BaseUrl}/invite/accept?token=… y lleva el token en claro, así que solo viaja por IAuthEmailQueue (nunca por
    // Hangfire) y no se registra. inviteeName (opcional), inviterName y organizationName los controla un usuario: se
    // codifican en la plantilla. role es un valor de OrgRole.Assignable. expiresAtUtc es el vencimiento de la invitación.
    Task SendOrganizationInvitationEmailAsync(
        string    toEmail,
        string?   inviteeName,
        string    inviterName,
        string    organizationName,
        string    role,
        string    acceptUrl,
        DateTime  expiresAtUtc,
        CancellationToken ct = default);

    // Propuesta de transferencia de la propiedad, al destino (PLAN-0018 RF-64 c; SR-0018.22; D-33): "<owner> te propuso
    // transferirte la propiedad de <organización>; revisa el aviso en Zentory antes del <fecha>". Sin token ni enlace con
    // token. proposerName y organizationName los controla un usuario: se codifican en la plantilla.
    Task SendOwnershipTransferProposedEmailAsync(
        string    toEmail,
        string    recipientName,
        string    proposerName,
        string    organizationName,
        DateTime  expiresAtUtc,
        CancellationToken ct = default);

    // Aviso al owner de que desde su cuenta se propuso transferir la organización (PLAN-0018 RF-64 c; SR-0018.42), para que
    // se entere aunque la haya propuesto otro con su sesión. Sin tokens; los nombres se codifican en la plantilla.
    Task SendOwnershipTransferProposedOwnerNoticeAsync(
        string    toEmail,
        string    recipientName,
        string    targetName,
        string    organizationName,
        DateTime  expiresAtUtc,
        CancellationToken ct = default);
}
