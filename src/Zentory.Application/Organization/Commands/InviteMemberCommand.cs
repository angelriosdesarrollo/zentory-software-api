using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Domain.Common;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

// POST /organization/members/invite (PLAN-0018 RF-49, RF-25, RF-51; SR-0018.21, SR-0018.22, SR-0018.24; D-19, D-20, D-21).
// La policy ManageMembers (empresa + plan efectivo pro + owner o admin) y el techo por organización de SR-0018.22
// ([AuthRateLimit] organization-invite) ya corrieron en el controlador; aquí van las reglas finas y el techo por email
// destino, que solo se consume cuando la invitación pasó todas las validaciones. La aceptación es de T29b.
public sealed record InviteMemberCommand(string Email, string? RoleId, string? Name) : IRequest<InviteMemberResult>;

public sealed record InviteMemberResult(Guid InvitationId, DateTime ExpiresAt);

public sealed class InviteMemberCommandValidator : AbstractValidator<InviteMemberCommand>
{
    public const int MaxEmailLength = 255;
    public const int MaxNameLength  = 200;

    public InviteMemberCommandValidator()
    {
        // Mensajes sin el valor recibido.
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo electrónico es obligatorio.")
            .EmailAddress().WithMessage("El correo electrónico no es válido.")
            .MaximumLength(MaxEmailLength).WithMessage("El correo electrónico es demasiado largo.");
        RuleFor(x => x.Name)
            .MaximumLength(MaxNameLength).WithMessage("El nombre es demasiado largo.");
    }
}

public sealed class InviteMemberCommandHandler : IRequestHandler<InviteMemberCommand, InviteMemberResult>
{
    public const string RoleIdField   = "roleId";
    public const string RoleIdMessage = "El rol no se puede asignar.";

    public const string AlreadyMemberCode     = "ALREADY_MEMBER";
    public const string InvitationPendingCode = "INVITATION_PENDING";

    public const string EntityType = "OrganizationInvitation";
    public const string LogAction  = "Invitó a un miembro";

    // Etiqueta fija de la cola: es lo único del correo que puede llegar a los logs (D-14).
    public const string EmailKind  = "organization-invitation";
    public const string AcceptPath = "/invite/accept";

    private readonly IZentoryDbContext       _db;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;
    private readonly IPlanLimitService       _planLimits;
    private readonly IActivityLogService     _activityLog;
    private readonly IAuthEmailQueue         _emails;
    private readonly IApplicationSettings    _settings;
    private readonly IInvitationEmailLimiter _emailLimiter;
    private readonly TimeProvider            _time;

    public InviteMemberCommandHandler(
        IZentoryDbContext       db,
        IUnitOfWork             uow,
        ITenantContext          tenant,
        IPlanLimitService       planLimits,
        IActivityLogService     activityLog,
        IAuthEmailQueue         emails,
        IApplicationSettings    settings,
        IInvitationEmailLimiter emailLimiter,
        TimeProvider            time)
    {
        _db           = db;
        _uow          = uow;
        _tenant       = tenant;
        _planLimits   = planLimits;
        _activityLog  = activityLog;
        _emails       = emails;
        _settings     = settings;
        _emailLimiter = emailLimiter;
        _time         = time;
    }

    // La URL sale de App:BaseUrl, nunca del Host ni del Origin de la petición; el token va codificado (RF-25).
    public static string AcceptUrlFor(string baseUrl, string rawToken)
        => $"{baseUrl}{AcceptPath}?token={Uri.EscapeDataString(rawToken)}";

    public async Task<InviteMemberResult> Handle(InviteMemberCommand request, CancellationToken ct)
    {
        // SR-0018.21: owner solo se obtiene creando la organización o por transferencia; cualquier otro valor tampoco.
        var roleId = request.RoleId;
        if (roleId is null || !OrgRole.Assignable.Contains(roleId, StringComparer.Ordinal))
            throw new Zentory.Application.Exceptions.ValidationException([new ValidationError(RoleIdField, RoleIdMessage)]);

        var email          = EmailAddress.Normalize(request.Email);
        var organizationId = _tenant.OrganizationId;
        var now            = _time.GetUtcNow().UtcDateTime;

        // Miembro vigente de la organización activa con ese email, también quien invita (con otras mayúsculas).
        var alreadyMember = await _db.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.DeletedAt == null)
            .Join(_db.Users, m => m.UserId, u => u.UserId, (m, u) => u.Email)
            .AnyAsync(memberEmail => memberEmail == email, ct);
        if (alreadyMember)
            throw new ConflictException(AlreadyMemberCode, "Esa persona ya es miembro de la organización.");

        if (await PendingInvitations(organizationId, now).AnyAsync(i => i.Email == email, ct))
            throw new ConflictException(InvitationPendingCode, "Ya hay una invitación vigente para ese correo.");

        await EnsureMemberQuotaAsync(organizationId, now, ct);

        // Techo por email destino (SR-0018.22): se consume aquí, después de todas las validaciones y antes de crear la
        // invitación y encolar el correo; un 429 no deja invitación ni correo.
        var emailLimit = _emailLimiter.TryAcquire(email);
        if (!emailLimit.IsAllowed)
            throw new TooManyRequestsException((int)Math.Ceiling(emailLimit.RetryAfter.TotalSeconds));

        var issued     = OneTimeToken.Issue();
        var invitation = OrganizationInvitation.Issue(organizationId, email, roleId, issued.Hash, _tenant.UserId, now);
        _db.OrganizationInvitations.Add(invitation);

        await _activityLog.LogAsync(
            entityType: EntityType,
            entityId:   invitation.Id,
            action:     LogAction,
            metadata:   JsonSerializer.Serialize(new { actorUserId = _tenant.UserId, targetEmail = email, role = roleId }),
            ct:         ct);
        await _uow.SaveChangesAsync(ct);

        await EnqueueEmailAsync(invitation, request.Name, issued.RawToken, ct);
        return new InviteMemberResult(invitation.Id, invitation.ExpiresAt);
    }

    // Pendientes de la organización: ni aceptadas, ni revocadas, ni vencidas (el filtro global quita las borradas).
    private IQueryable<OrganizationInvitation> PendingInvitations(Guid organizationId, DateTime now)
        => _db.OrganizationInvitations.Where(i =>
            i.OrganizationId == organizationId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now);

    // max_org_members = miembros vigentes + invitaciones pendientes, solo de la organización activa (SR-0018.24) y con
    // su plan efectivo (D-19, RF-74 b). Al estar por encima tras una bajada, tampoco se invita (RF-75 d).
    private async Task EnsureMemberQuotaAsync(Guid organizationId, DateTime now, CancellationToken ct)
    {
        var limit = await _planLimits.GetLimitAsync(
            _tenant.Plan, _tenant.LegalType, PlanLimits.FeatureKeys.MaxOrgMembers, ct);
        if (!limit.HasValue)
            return;

        var members = await _db.OrganizationMembers
            .CountAsync(m => m.OrganizationId == organizationId && m.DeletedAt == null, ct);
        var pending = await PendingInvitations(organizationId, now).CountAsync(ct);
        var used    = members + pending;
        if (used >= limit.Value)
            throw new QuotaExceededException(PlanLimits.FeatureKeys.MaxOrgMembers, limit.Value, used);
    }

    // Encola el correo con copias de los datos: la cola corre en otro scope, sin la entidad ni el contexto del request.
    // Los valores que controla un usuario se codifican en la plantilla (SR-0018.22).
    private async Task EnqueueEmailAsync(
        OrganizationInvitation invitation, string? inviteeName, string rawToken, CancellationToken ct)
    {
        var organizationId = invitation.OrganizationId;
        var inviterId      = invitation.InvitedBy;
        var organizationName = await _db.Organizations
            .Where(o => o.OrganizationId == organizationId)
            .Select(o => o.Name)
            .FirstAsync(ct);
        var inviter = await _db.Users
            .Where(u => u.UserId == inviterId)
            .Select(u => new { u.FirstName, u.LastName })
            .FirstAsync(ct);

        var (toEmail, role, expiresAt) = (invitation.Email, invitation.Role, invitation.ExpiresAt);
        var inviterName = $"{inviter.FirstName} {inviter.LastName}".Trim();
        var name        = string.IsNullOrWhiteSpace(inviteeName) ? null : inviteeName.Trim();
        var acceptUrl   = AcceptUrlFor(_settings.BaseUrl, rawToken);

        _emails.TryEnqueue(EmailKind, (email, token) => email.SendOrganizationInvitationEmailAsync(
            toEmail, name, inviterName, organizationName, role, acceptUrl, expiresAt, token));
    }
}
