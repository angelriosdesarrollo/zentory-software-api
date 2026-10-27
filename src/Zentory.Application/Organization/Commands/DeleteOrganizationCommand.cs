using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Options;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

// DELETE /organization (PLAN-0018 RF-65 a, a2, b, b2, f; RF-81; D-39; ADR purga §1): borrado lógico de la organización
// activa. La policy RequireOrgOwner y los límites organization-delete y reauth:{sub} ya corrieron en el controlador
// (RF-81 e). Cancela la transferencia de la propiedad pendiente (RF-83 h). Sin ToString con valores: un record imprimiría la credencial si alguien lo registrara.
public sealed record DeleteOrganizationCommand(string? ConfirmName, string? CurrentPassword, string? GoogleIdToken)
    : IRequest
{
    public override string ToString() => nameof(DeleteOrganizationCommand);
}

public sealed class DeleteOrganizationCommandHandler : IRequestHandler<DeleteOrganizationCommand>
{
    public const string ConfirmNameField   = "confirmName";
    public const string ConfirmNameMessage = "El nombre no coincide con el de la organización.";
    public const string ChangedCode        = "ORGANIZATION_CHANGED";
    public const string ChangedMessage     = "La organización cambió mientras se eliminaba. Vuelve a cargarla e inténtalo de nuevo.";

    public const string AuditTable  = "organizations";
    public const string AuditAction = "SOFTDELETE";

    public const string MemberNoticeKind = "organization-deleted-notice";
    public const string OwnerNoticeKind  = "organization-deleted-owner-notice";

    private readonly IZentoryDbContext         _db;
    private readonly IUnitOfWork               _uow;
    private readonly ITenantContext            _tenant;
    private readonly IReauthenticationVerifier _reauth;
    private readonly IReauthNonceStore         _nonces;
    private readonly IActivityLogService       _activityLog;
    private readonly IAuthEmailQueue           _emails;
    private readonly OrganizationsOptions      _options;
    private readonly TimeProvider              _time;

    public DeleteOrganizationCommandHandler(
        IZentoryDbContext               db,
        IUnitOfWork                     uow,
        ITenantContext                  tenant,
        IReauthenticationVerifier       reauth,
        IReauthNonceStore               nonces,
        IActivityLogService             activityLog,
        IAuthEmailQueue                 emails,
        IOptions<OrganizationsOptions>  options,
        TimeProvider                    time)
    {
        _db          = db;
        _uow         = uow;
        _tenant      = tenant;
        _reauth      = reauth;
        _nonces      = nonces;
        _activityLog = activityLog;
        _emails      = emails;
        _options     = options.Value;
        _time        = time;
    }

    public async Task Handle(DeleteOrganizationCommand request, CancellationToken ct)
    {
        var organizationId = _tenant.OrganizationId;
        var userId         = _tenant.UserId;

        // Lecturas sin cambios pendientes: el verificador guarda su AuditLog de fallo con SaveChanges sobre este mismo
        // contexto, así que nada de esta acción puede estar modificado antes de verificar (nota de TK-0037).
        var organization = await _db.Organizations.FirstOrDefaultAsync(o => o.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Organization", organizationId);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw new NotFoundException("User", userId);

        // 1. Reautenticación (RF-81): 422 sin credencial o con las dos; 403 uniforme ante cualquier otro fallo.
        var verification = await _reauth.VerifyAsync(
            user, new ReauthCredential(request.CurrentPassword, request.GoogleIdToken), ReauthAction.OrganizationDelete,
            organizationId, ct);

        // 2. Confirmación con el nombre (RF-65 a), después de la credencial: un intento con el nombre mal escrito no
        //    consume el nonce y el reintento correcto funciona (RF-84 d).
        if (!NameMatches(request.ConfirmName, organization.Name))
            throw new ValidationException([new ValidationError(ConfirmNameField, ConfirmNameMessage)]);

        // 3. El owner que pasó la policy debe seguir siéndolo (RF-65 b2). El sello cubre el cambio posterior a esta lectura.
        if (organization.OwnerId != userId)
            throw new ConflictException(ChangedCode, ChangedMessage);

        var recipients = await RecipientsAsync(organizationId, userId, ct);

        // 4. Nonce de Google: un solo uso, consumido justo antes de escribir (RF-84 d). La contraseña no lo usa.
        if (!verification.TryConsume(_nonces))
            throw new ReauthenticationFailedException();

        var deletedAt = _time.GetUtcNow().UtcDateTime;
        try
        {
            await _uow.ExecuteInTransactionAsync(async token =>
            {
                organization.MarkDeleted(userId, deletedAt);

                // RF-83 h: la propuesta de transferencia pendiente queda cancelada, y restaurar no la reactiva. Su Status
                // es token de concurrencia: si el destino la aceptó a la vez, esta escritura pierde (409 de abajo).
                var pendingTransfers = await _db.OrganizationOwnershipTransfers
                    .Where(t => t.OrganizationId == organizationId && t.Status == OwnershipTransferStatus.Pending)
                    .ToListAsync(token);
                foreach (var transfer in pendingTransfers)
                    transfer.Cancel(deletedAt);

                _db.AuditLogs.Add(AuditLog.Record(
                    tableName:      AuditTable,
                    recordId:       organizationId,
                    action:         AuditAction,
                    organizationId: organizationId,
                    userId:         userId,
                    newValues:      JsonSerializer.Serialize(new { deletedAt, deletedBy = userId })));

                await _activityLog.LogAsync(
                    "Organization", organizationId, "Eliminó la organización", organization.Name, ct: token);

                // ConcurrencyStamp leído como token: si otra operación cambió el owner o el estado, no hay efecto.
                await _uow.SaveChangesAsync(token);
            }, ct);
        }
        catch (Exception exception)
        {
            // El reintento legítimo con el mismo nonce debe volver a funcionar si la escritura no se confirmó.
            verification.Release(_nonces);
            if (exception is DbUpdateConcurrencyException)
                throw new ConflictException(ChangedCode, ChangedMessage);
            throw;
        }

        // 5. Correos tras confirmar (D-39): admins y members vigentes, y el owner con la fecha límite para restaurar.
        EnqueueNotices(recipients, user, organization.Name, _options.RestoreDeadline(deletedAt));
    }

    // Igualdad exacta tras quitar espacios en los extremos (RF-65 a): ni mayúsculas distintas ni vacío.
    private static bool NameMatches(string? confirmName, string organizationName)
    {
        var confirmed = confirmName?.Trim();
        return !string.IsNullOrEmpty(confirmed)
            && string.Equals(confirmed, organizationName.Trim(), StringComparison.Ordinal);
    }

    private async Task<List<Recipient>> RecipientsAsync(Guid organizationId, Guid ownerId, CancellationToken ct)
        => await _db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.DeletedAt == null && m.UserId != ownerId)
            .Join(_db.Users, m => m.UserId, u => u.UserId, (m, u) => new Recipient(u.Email, u.FirstName))
            .ToListAsync(ct);

    private void EnqueueNotices(IEnumerable<Recipient> members, User owner, string organizationName, DateTime restoreDeadline)
    {
        foreach (var member in members)
        {
            var (toEmail, name) = (member.Email, member.FirstName);
            _emails.TryEnqueue(MemberNoticeKind, (email, token) =>
                email.SendOrganizationDeletedNoticeAsync(toEmail, name, organizationName, token));
        }

        var (ownerEmail, ownerName) = (owner.Email, owner.FirstName);
        _emails.TryEnqueue(OwnerNoticeKind, (email, token) =>
            email.SendOrganizationDeletedOwnerNoticeAsync(ownerEmail, ownerName, organizationName, restoreDeadline, token));
    }

    private sealed record Recipient(string Email, string FirstName);
}
