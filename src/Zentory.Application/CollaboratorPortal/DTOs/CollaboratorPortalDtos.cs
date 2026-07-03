namespace Zentory.Application.CollaboratorPortal.DTOs;

public record CollaboratorMembershipDto(
    Guid   CollaboratorId,
    Guid   OrganizationId,
    string OrganizationName,
    string CollaboratorName);

// Ítem de origen del canje (solo presente si Kind != magic_link) — le dice al frontend
// qué resaltar/preseleccionar al aterrizar en /portal/home.
public record CollaboratorHighlightDto(
    string  Kind,     // 'pila' | 'payout_invoice'
    string  Period,
    Guid    Id);

public record CollaboratorSessionDto(
    string                                  AccessToken,
    int                                     ExpiresInSeconds,
    CollaboratorMembershipDto               ActiveCollaborator,
    IReadOnlyList<CollaboratorMembershipDto> Memberships,
    CollaboratorHighlightDto?               Highlight);

public record OwnPilaVerificationDto(
    Guid      Id,
    string    Period,
    string    Status,
    DateTime  RequestedAt,
    DateTime? ReceivedAt,
    DateTime? VerifiedAt,
    string?   DocumentFileName,
    long?     DocumentFileSize,
    string    Source);

public record OwnPayoutInvoiceDto(
    Guid      Id,
    string    Period,
    string    Concept,
    decimal   Amount,
    decimal?  DeclaredAmount,
    string    Currency,
    string    Status,
    string    Source,
    string?   DocumentFileName,
    long?     DocumentFileSize);
