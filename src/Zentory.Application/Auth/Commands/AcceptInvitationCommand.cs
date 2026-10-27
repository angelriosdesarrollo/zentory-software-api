using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Common;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

// POST /auth/invitations/accept (PLAN-0018 RF-50, RF-29 f, RF-60, RF-90 b y c; SR-0018.21, SR-0018.24; D-20, D-21; G-7).
// La acción es [AllowAnonymous] y usa el JWT de usuario si el Bearer es válido (ITenantContext.IsAuthenticated):
// - sin JWT, { token, password, firstName, lastName, acceptedTerms } crea la cuenta del email invitado (verificada, con
//   PasswordPolicy, sin organización propia, con la aceptación de Términos y Privacidad como un registro: DEC-0110) e
//   inicia su sesión (RF-90 b), solo si no existe un usuario con ese email (409 ACCOUNT_EXISTS);
// - con JWT, { token } exige que el sid del token sea la sesión vigente del usuario (RF-90 d, C-16), el email invitado y
//   verificado, y conserva la sesión del token que llama (RF-90 c).
// La membresía va siempre a la organización de la fila de la invitación, con el rol guardado (revalidado contra
// OrgRole.Assignable); un organizationId en el cuerpo no existe en el contrato y se ignora. Token inexistente, usado,
// vencido, revocado o de una organización eliminada → 422 TOKEN_INVALID, sin cuenta ni membresía (RF-50 f y g).
public sealed record AcceptInvitationCommand(
    string  Token,
    string? Password  = null,
    string? FirstName = null,
    string? LastName  = null,
    bool    AcceptedTerms = false) : IRequest<AuthTokenDto>;

public sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public const int MaxNameLength = 100;

    // La cuenta nueva solo se pide sin JWT: con JWT el cuerpo es { token } y los demás campos se ignoran. El validador corre
    // antes del handler, así que una contraseña fuera de la política o sin aceptar los términos responde 422 sin consumir
    // el token (RF-50 a, RF-26; DEC-0110).
    public AcceptInvitationCommandValidator(ITenantContext tenant)
    {
        // Sin eco del token en el mensaje.
        RuleFor(x => x.Token).NotEmpty().WithMessage("El enlace no es válido.");

        When(_ => !tenant.IsAuthenticated, () =>
        {
            RuleFor(x => x.Password!).ApplyPasswordPolicy();
            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage("El nombre es obligatorio.")
                .MaximumLength(MaxNameLength).WithMessage("El nombre es demasiado largo.");
            RuleFor(x => x.LastName)
                .NotEmpty().WithMessage("El apellido es obligatorio.")
                .MaximumLength(MaxNameLength).WithMessage("El apellido es demasiado largo.");
            // La misma regla y el mismo mensaje que RegisterCommandValidator: una casilla cubre Términos y Privacidad.
            RuleFor(x => x.AcceptedTerms)
                .Equal(true)
                .WithMessage(RegisterCommandValidator.TermsRequiredMessage);
        });
    }
}

public static class AcceptInvitationErrors
{
    public const string AccountExistsCode     = "ACCOUNT_EXISTS";
    public const string AccountExistsMessage  = "Ya existe una cuenta con ese correo. Inicia sesión para aceptar la invitación.";
    public const string EmailMismatchCode     = "INVITATION_EMAIL_MISMATCH";
    public const string EmailMismatchMessage  = "La invitación es para otro correo electrónico.";
    public const string AlreadyMemberCode     = InviteMemberCommandHandler.AlreadyMemberCode;
    public const string AlreadyMemberMessage  = "Ya eres miembro de esa organización.";
}

public sealed class AcceptInvitationCommandHandler : IRequestHandler<AcceptInvitationCommand, AuthTokenDto>
{
    // ActivityLog de la organización (D-21; parte de CA-60).
    public const string EntityType = InviteMemberCommandHandler.EntityType;
    public const string LogAction  = "Aceptó una invitación";

    private readonly IZentoryDbContext     _db;
    private readonly IUserRepository       _users;
    private readonly IUnitOfWork           _uow;
    private readonly ITenantContext        _tenant;
    private readonly IUserMembershipReader _memberships;
    private readonly IAuthSessionIssuer    _sessions;
    private readonly TimeProvider          _time;

    public AcceptInvitationCommandHandler(
        IZentoryDbContext     db,
        IUserRepository       users,
        IUnitOfWork           uow,
        ITenantContext        tenant,
        IUserMembershipReader memberships,
        IAuthSessionIssuer    sessions,
        TimeProvider          time)
    {
        _db          = db;
        _users       = users;
        _uow         = uow;
        _tenant      = tenant;
        _memberships = memberships;
        _sessions    = sessions;
        _time        = time;
    }

    public async Task<AuthTokenDto> Handle(AcceptInvitationCommand request, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        if (!_tenant.IsAuthenticated)
            return await AcceptWithNewAccountAsync(await FindRedeemableAsync(request.Token, now, ct), request, now, ct);

        // Con JWT la sesión se compara antes de buscar y canjear la invitación (C-16): una sesión reemplazada no consume
        // el token ni crea la membresía, y recibe SESSION_REPLACED aunque el token de la invitación tampoco sirva.
        var (user, sessionId) = await CurrentSessionCallerAsync(ct);
        var invitation = await FindRedeemableAsync(request.Token, now, ct);
        return await AcceptWithSessionAsync(invitation, user, sessionId, now, ct);
    }

    // Sin JWT (RF-50 a): la cuenta nueva equivale a un registro e inicia sesión (RF-90 b).
    private async Task<AuthTokenDto> AcceptWithNewAccountAsync(
        OrganizationInvitation invitation, AcceptInvitationCommand request, DateTime now, CancellationToken ct)
    {
        if (await _users.GetByEmailAsync(invitation.Email, ct) is not null)
            throw new ConflictException(AcceptInvitationErrors.AccountExistsCode, AcceptInvitationErrors.AccountExistsMessage);

        // Sin organización propia: User.OrganizationId queda null y la única membresía es la de la invitación.
        var user = User.Create(
            invitation.Email,
            request.FirstName!.Trim(),
            request.LastName!.Trim(),
            role:                 invitation.Role,
            passwordHash:         BCrypt.Net.BCrypt.HashPassword(request.Password),
            // Como RegisterCommand (DEC-0110), siempre con la versión vigente: el cliente no elige qué versión aceptó.
            termsAcceptedAt:      now,
            termsAcceptedVersion: LegalDocuments.CurrentTermsVersion);
        // El enlace llegó al buzón invitado: demuestra control del correo.
        user.MarkEmailVerified();

        await RedeemAsync(invitation, user, now, isNewUser: true, ct);

        return await _sessions.StartSessionAsync(user, invitation.OrganizationId, ct);
    }

    // Con JWT, la sesión del que llama (RF-90 d, D-47, C-16): la acción es [AllowAnonymous] y el middleware no compara el
    // sid, así que lo compara este comando, con el mismo orden que el middleware para una acción excluida. Un token sin sid
    // legible (SessionClaims, vía ITenantContext.SessionId) → 401 sin code; un sid distinto de User.CurrentSessionId (o un
    // CurrentSessionId nulo) → 401 SESSION_REPLACED. La lectura es por clave primaria y no escribe nada.
    private async Task<(User User, Guid SessionId)> CurrentSessionCallerAsync(CancellationToken ct)
    {
        Guid sessionId;
        try
        {
            sessionId = _tenant.SessionId;
        }
        catch (InvalidOperationException)
        {
            // ITenantContext.SessionId lanza sin un sid legible; aquí no es un error del servidor sino un token viejo.
            throw new MissingSessionException();
        }

        var user = await _users.GetByIdAsync(_tenant.UserId, ct)
            ?? throw new UnauthorizedException("UNAUTHORIZED");

        if (user.CurrentSessionId != sessionId)
            throw new SessionReplacedException();

        return (user, sessionId);
    }

    // Con JWT (RF-50 b): el usuario sale del sub del token, nunca del cuerpo, y su sesión ya se comparó. Conserva la sesión
    // del token que llama (RF-90 c): emite con su sid, nunca con User.CurrentSessionId, sin StartSession ni revocación de
    // otros refresh. Los errores de aquí (409, 403, 422) no tocan la sesión vigente.
    private async Task<AuthTokenDto> AcceptWithSessionAsync(
        OrganizationInvitation invitation, User user, Guid sessionId, DateTime now, CancellationToken ct)
    {
        if (!string.Equals(user.Email, invitation.Email, StringComparison.Ordinal))
            throw new ConflictException(AcceptInvitationErrors.EmailMismatchCode, AcceptInvitationErrors.EmailMismatchMessage);

        if (!user.IsEmailVerified)
            throw new ForbiddenException(ForbiddenReason.EmailNotVerified);

        // Una membresía vigente en esa organización no se duplica (SR-0018.8). Una borrada no cuenta: el exmiembro recibe
        // una membresía nueva con el rol de la invitación (D-20).
        if (await _memberships.FindActiveAsync(user.UserId, invitation.OrganizationId, ct) is not null)
            throw new ConflictException(AcceptInvitationErrors.AlreadyMemberCode, AcceptInvitationErrors.AlreadyMemberMessage);

        await RedeemAsync(invitation, user, now, isNewUser: false, ct);

        return await _sessions.ContinueSessionAsync(user, sessionId, invitation.OrganizationId, ct);
    }

    // La invitación se busca por el hash del token sin filtros globales: quien acepta no está en esa organización, y con
    // JWT el filtro de tenant la limitaría a su organización activa (RF-50 c; lista blanca de ADR-0008 §3). El predicado
    // explícito y IsPendingAt hacen el resto: ni aceptada, ni revocada, ni borrada, ni vencida. La organización debe
    // seguir vigente (el filtro global de Organization quita las eliminadas, RF-50 g) y el rol guardado, ser asignable
    // (SR-0018.21). Todos los casos responden el mismo 422 TOKEN_INVALID.
    private async Task<OrganizationInvitation> FindRedeemableAsync(string token, DateTime now, CancellationToken ct)
    {
        var tokenHash  = OneTimeToken.HashOf(token);
        var invitation = await _db.OrganizationInvitations
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

        if (invitation is null
            || !invitation.IsPendingAt(now)
            || !OrgRole.Assignable.Contains(invitation.Role, StringComparer.Ordinal))
            throw new InvalidTokenException();

        if (!await IssuerStillAuthorizesAsync(invitation, ct))
            throw new InvalidTokenException();

        return invitation;
    }

    // REV-0005 H-5: la invitación solo se canjea mientras su emisor siga siendo owner (Organization.OwnerId) o admin
    // vigente de la organización, que además debe seguir vigente (el filtro global de Organization quita las
    // eliminadas, RF-50 g). Dar de baja o degradar al emisor la revoca en el origen; esta lectura cubre las anteriores y
    // la carrera, y se repite dentro de la transacción del canje. Sin consumir ni revelar nada: 422 uniforme.
    private async Task<bool> IssuerStillAuthorizesAsync(OrganizationInvitation invitation, CancellationToken ct)
    {
        var organizationId = invitation.OrganizationId;
        var issuerId       = invitation.InvitedBy;

        var organization = await _db.Organizations
            .Where(o => o.OrganizationId == organizationId)
            .Select(o => new { o.OwnerId })
            .FirstOrDefaultAsync(ct);
        if (organization is null) return false;
        if (organization.OwnerId == issuerId) return true;

        // La lectura de membresías entre organizaciones es la de IUserMembershipReader (RF-12): quien acepta puede tener
        // activa otra organización, así que el filtro de tenant no sirve aquí.
        var membership = await _memberships.FindActiveAsync(issuerId, organizationId, ct);
        return membership is not null && (membership.Role == OrgRole.Owner || membership.Role == OrgRole.Admin);
    }

    // Canje atómico (RF-49 b, CA-57): en una transacción, primero se marca AcceptedAt (token de concurrencia, UPDATE ...
    // WHERE accepted_at IS NULL) y se confirma; quien pierde la carrera recibe DbUpdateConcurrencyException → 422
    // TOKEN_INVALID antes de crear la cuenta o la membresía. Después, la cuenta nueva (sin JWT), la membresía en la
    // organización de la invitación y el ActivityLog, en la misma transacción.
    private Task RedeemAsync(OrganizationInvitation invitation, User user, DateTime now, bool isNewUser, CancellationToken ct)
        => _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            // La baja o degradación del emisor entre la lectura y el canje también lo impide (REV-0005 H-5).
            if (!await IssuerStillAuthorizesAsync(invitation, transactionCt))
                throw new InvalidTokenException();

            invitation.Accept(now);
            try
            {
                await _uow.SaveChangesAsync(transactionCt);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidTokenException();
            }

            if (isNewUser)
                await _users.AddAsync(user, transactionCt);

            var membership = OrganizationMember.Create(
                invitation.OrganizationId, user.UserId, invitation.Role, invitedBy: invitation.InvitedBy);
            _db.OrganizationMembers.Add(membership);
            _db.ActivityLogs.Add(LogEntry(invitation, user, now));

            await _uow.SaveChangesAsync(transactionCt);
        }, ct);

    // ActivityLog de la organización de la invitación, no de la activa del token (D-21): actor (quien acepta), objetivo
    // (el email invitado, ahora miembro) y rol. Sin el token ni su hash.
    private static ActivityLog LogEntry(OrganizationInvitation invitation, User user, DateTime now)
        => ActivityLog.Create(
            organizationId: invitation.OrganizationId,
            entityType:     EntityType,
            entityId:       invitation.Id,
            userInitials:   Initials(user),
            action:         LogAction,
            metadata:       JsonSerializer.Serialize(new
            {
                actorUserId  = user.UserId,
                targetUserId = user.UserId,
                targetEmail  = invitation.Email,
                role         = invitation.Role,
                invitedBy    = invitation.InvitedBy,
            }),
            userId:         user.UserId,
            occurredAt:     now);

    // Las mismas iniciales que el claim initials de JwtService.
    private static string Initials(User user)
        => $"{FirstLetter(user.FirstName)}{FirstLetter(user.LastName)}".ToUpperInvariant();

    private static string FirstLetter(string value) => string.IsNullOrEmpty(value) ? "?" : value[..1];
}
