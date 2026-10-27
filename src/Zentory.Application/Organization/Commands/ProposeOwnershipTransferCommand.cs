using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.Services;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

// POST /organization/transfer-ownership (PLAN-0018 RF-64; RF-77; RF-81; SR-0018.29, SR-0018.42; D-33; C-12): el owner
// propone transferir la propiedad. Proponer no cambia OwnerId, roles, sesiones, plan ni Subscriptions: solo deja una
// propuesta pending que el destino acepta o rechaza (T30c). La policy RequireOrgOwner y los límites
// organization-transfer (5/h) y reauth:{sub} (5/15 min) ya corrieron en el controlador (RF-81 e). Sin ToString con
// valores: un record imprimiría la credencial si alguien lo registrara.
public sealed record ProposeOwnershipTransferCommand(
    Guid    UserId,
    string? AcknowledgedPreviewHash,
    string? CurrentPassword,
    string? GoogleIdToken)
    : IRequest<ProposeOwnershipTransferResult>
{
    public override string ToString() => nameof(ProposeOwnershipTransferCommand);
}

public sealed record ProposeOwnershipTransferResult(Guid TransferId, DateTime ExpiresAt);

// 422 QUOTA_EXCEEDED de max_owned_orgs del destino (RF-64 b). El uso informado es el límite: el conteo real de
// organizaciones del destino no sale de la API (SR-0018.46).
public sealed class OwnershipTransferQuotaExceededException(int limit)
    : QuotaExceededException(PlanLimits.FeatureKeys.MaxOwnedOrgs, limit, limit)
{
    public const string TransferMessage =
        "La persona elegida alcanzó el límite de organizaciones propias de su plan. No puede recibir esta organización.";

    public override string Message => TransferMessage;
}

public sealed class ProposeOwnershipTransferCommandHandler
    : IRequestHandler<ProposeOwnershipTransferCommand, ProposeOwnershipTransferResult>
{
    public const string PreviewStaleCode    = "PREVIEW_STALE";
    public const string PreviewStaleMessage = "Las condiciones de la transferencia cambiaron. Revisa el aviso de nuevo.";
    public const string PendingCode         = "TRANSFER_PENDING";
    public const string PendingMessage      = "Ya hay una transferencia de la propiedad pendiente. Cancélala antes de proponer otra.";

    public const string EntityType = "OrganizationOwnershipTransfer";
    public const string LogAction  = "Propuso transferir la propiedad";

    // Etiquetas fijas de la cola: es lo único del correo que puede llegar a los logs (D-14).
    public const string TargetEmailKind = "ownership-transfer-proposed";
    public const string OwnerNoticeKind = "ownership-transfer-proposed-owner-notice";

    private readonly IZentoryDbContext         _db;
    private readonly IUnitOfWork               _uow;
    private readonly ITenantContext            _tenant;
    private readonly IReauthenticationVerifier _reauth;
    private readonly IReauthNonceStore         _nonces;
    private readonly IPlanResolutionService    _plans;
    private readonly IPlanLimitService         _planLimits;
    private readonly IActivityLogService       _activityLog;
    private readonly IAuthEmailQueue           _emails;
    private readonly TimeProvider              _time;

    public ProposeOwnershipTransferCommandHandler(
        IZentoryDbContext         db,
        IUnitOfWork               uow,
        ITenantContext            tenant,
        IReauthenticationVerifier reauth,
        IReauthNonceStore         nonces,
        IPlanResolutionService    plans,
        IPlanLimitService         planLimits,
        IActivityLogService       activityLog,
        IAuthEmailQueue           emails,
        TimeProvider              time)
    {
        _db          = db;
        _uow         = uow;
        _tenant      = tenant;
        _reauth      = reauth;
        _nonces      = nonces;
        _plans       = plans;
        _planLimits  = planLimits;
        _activityLog = activityLog;
        _emails      = emails;
        _time        = time;
    }

    public async Task<ProposeOwnershipTransferResult> Handle(ProposeOwnershipTransferCommand request, CancellationToken ct)
    {
        var organizationId = _tenant.OrganizationId;
        var ownerId        = _tenant.UserId;

        // Lecturas sin cambios pendientes: el verificador guarda su AuditLog de fallo con SaveChanges sobre este mismo
        // contexto, así que nada de esta acción puede estar modificado antes de verificar (nota de TK-0037).
        var organization = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Organization", organizationId);
        var owner = await _db.Users.FirstOrDefaultAsync(u => u.UserId == ownerId, ct)
            ?? throw new NotFoundException("User", ownerId);

        // 1. Reautenticación del owner (RF-81, acción organization.transfer), antes que el destino y el acuse (RF-64 f):
        //    422 sin credencial o con las dos; 403 uniforme ante cualquier otro fallo, también con un nonce ya consumido.
        var verification = await _reauth.VerifyAsync(
            owner, new ReauthCredential(request.CurrentPassword, request.GoogleIdToken), ReauthAction.OrganizationTransfer,
            organizationId, ct);

        // 2. Destino (404 / 422 / 403 EMAIL_NOT_VERIFIED), límite del destino (422) y acuse del aviso (409 PREVIEW_STALE).
        //    Ninguno consume el nonce: el reintento correcto con el mismo nonce funciona (RF-84 d).
        var now    = _time.GetUtcNow().UtcDateTime;
        var target = await OwnershipTransferPreview.ResolveTargetAsync(_db, organizationId, ownerId, request.UserId, ct);
        var preview = await OwnershipTransferPreview.BuildAsync(
            _db, _plans, _planLimits, organization, target.UserId, now, ct);
        if (preview.Blocked)
            throw new OwnershipTransferQuotaExceededException(await OwnedOrganizationLimitAsync(organization.LegalType, preview.NewPlan, ct));
        if (!OwnershipTransferPreview.Acknowledges(request.AcknowledgedPreviewHash, preview.PreviewHash))
            throw new ConflictException(PreviewStaleCode, PreviewStaleMessage);

        // 3. Una sola pendiente por organización (RF-64 c2): una vigente bloquea; una vencida se trata como inexistente y
        //    pasa a cancelled en la misma transacción.
        var pending = await _db.OrganizationOwnershipTransfers
            .Where(t => t.OrganizationId == organizationId && t.Status == OwnershipTransferStatus.Pending)
            .ToListAsync(ct);
        if (pending.Any(t => t.IsPendingAt(now)))
            throw new ConflictException(PendingCode, PendingMessage);

        // 4. Nonce de Google: un solo uso, consumido justo antes de escribir (RF-84 d). La contraseña no lo usa.
        if (!verification.TryConsume(_nonces))
            throw new ReauthenticationFailedException();

        var transfer = OrganizationOwnershipTransfer.Propose(
            organizationId, ownerId, target.UserId, preview.PreviewHash, now, OrgSecurity.OwnershipTransferLifetime);
        try
        {
            await _uow.ExecuteInTransactionAsync(async token =>
            {
                // Las vencidas salen de pending antes de insertar la nueva: el índice único filtrado lo exige en PostgreSQL.
                if (pending.Count > 0)
                {
                    foreach (var expired in pending)
                        expired.Cancel(now);
                    await _uow.SaveChangesAsync(token);
                }

                _db.OrganizationOwnershipTransfers.Add(transfer);
                await _activityLog.LogAsync(
                    entityType: EntityType,
                    entityId:   transfer.Id,
                    action:     LogAction,
                    entityCode: organization.Name,
                    metadata:   JsonSerializer.Serialize(new
                    {
                        fromUserId = ownerId,
                        toUserId   = target.UserId,
                        expiresAt  = transfer.ExpiresAt,
                    }),
                    ct:         token);
                await _uow.SaveChangesAsync(token);
            }, ct);
        }
        catch (DbUpdateException)
        {
            // Otra propuesta simultánea ganó el índice único (o resolvió la vencida): sin efecto, y el reintento legítimo
            // con el mismo nonce vuelve a funcionar.
            verification.Release(_nonces);
            throw new ConflictException(PendingCode, PendingMessage);
        }
        catch
        {
            verification.Release(_nonces);
            throw;
        }

        // 5. Correos tras confirmar (RF-64 c; SR-0018.42): al destino y el aviso al owner, sin token ni enlace con token.
        EnqueueEmails(owner, target, organization.Name, transfer.ExpiresAt);
        return new ProposeOwnershipTransferResult(transfer.Id, transfer.ExpiresAt);
    }

    private async Task<int> OwnedOrganizationLimitAsync(string legalType, string targetPlan, CancellationToken ct)
        => await _planLimits.GetLimitAsync(targetPlan, legalType, PlanLimits.FeatureKeys.MaxOwnedOrgs, ct) ?? 0;

    // Encola con copias de los datos: la cola corre en otro scope, sin las entidades ni el contexto del request. Los
    // nombres los controla un usuario y se codifican en la plantilla (SR-0018.22).
    private void EnqueueEmails(User owner, User target, string organizationName, DateTime expiresAt)
    {
        var (ownerEmail, ownerFirstName, ownerFullName)    = (owner.Email, owner.FirstName, FullName(owner));
        var (targetEmail, targetFirstName, targetFullName) = (target.Email, target.FirstName, FullName(target));

        _emails.TryEnqueue(TargetEmailKind, (email, token) => email.SendOwnershipTransferProposedEmailAsync(
            targetEmail, targetFirstName, ownerFullName, organizationName, expiresAt, token));
        _emails.TryEnqueue(OwnerNoticeKind, (email, token) => email.SendOwnershipTransferProposedOwnerNoticeAsync(
            ownerEmail, ownerFirstName, targetFullName, organizationName, expiresAt, token));
    }

    private static string FullName(User user) => $"{user.FirstName} {user.LastName}".Trim();
}
