using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Organization.Services;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Services;

public sealed class AuthSessionIssuer : IAuthSessionIssuer
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    // Intentos del SaveChanges de un inicio de sesión ante conflictos de concurrencia (PLAN-0018 RF-90 g, R-28, D-47).
    public const int MaxStartSessionAttempts = 3;

    private readonly IUserMembershipReader      _memberships;
    private readonly IOrganizationRepository    _organizations;
    private readonly IDeletedOrganizationReader _deleted;
    private readonly IRefreshTokenRepository    _refreshTokens;
    private readonly IJwtService                _jwt;
    private readonly IUnitOfWork                _uow;

    public AuthSessionIssuer(
        IUserMembershipReader      memberships,
        IOrganizationRepository    organizations,
        IDeletedOrganizationReader deleted,
        IRefreshTokenRepository    refreshTokens,
        IJwtService                jwt,
        IUnitOfWork                uow)
    {
        _memberships   = memberships;
        _organizations = organizations;
        _deleted       = deleted;
        _refreshTokens = refreshTokens;
        _jwt           = jwt;
        _uow           = uow;
    }

    public async Task<AuthTokenDto> StartSessionAsync(User user, Guid? preferredOrgId, CancellationToken ct = default)
    {
        // Primero se decide si hay sesión que emitir: un intento que termina en 4xx (RF-61) lanza aquí, sin tocar la
        // sesión vigente ni los tokens del usuario.
        var target = await ResolveTargetAsync(user, preferredOrgId, ct);

        var sessionId = user.StartSession();
        await _refreshTokens.RevokeAllForUserAsync(user.UserId, ct);
        var refreshToken = await AddRefreshTokenAsync(user, sessionId, ct);
        await SaveStartedSessionAsync(ct);

        return BuildResponse(user, target, sessionId, refreshToken);
    }

    public async Task<AuthTokenDto> ContinueSessionAsync(
        User user, Guid sessionId, Guid? preferredOrgId, CancellationToken ct = default)
    {
        var target = await ResolveTargetAsync(user, preferredOrgId, ct);

        var refreshToken = await AddRefreshTokenAsync(user, sessionId, ct);
        await _uow.SaveChangesAsync(ct);

        return BuildResponse(user, target, sessionId, refreshToken);
    }

    // Organización activa elegida, o la sesión sin organización (RF-85, RF-61; D-44): solo el owner con organizaciones
    // propias restaurables la recibe, para restaurarlas o crear otra. A cualquier otro usuario no se le emite nada.
    private async Task<SessionTarget> ResolveTargetAsync(User user, Guid? preferredOrgId, CancellationToken ct)
    {
        var memberships = await _memberships.ListActiveAsync(user.UserId, ct);

        var active = SelectActive(memberships, preferredOrgId);
        var org = active is null ? null : await _organizations.GetByIdAsync(active.OrganizationId, ct);

        if (active is not null && org is not null)
            return new SessionTarget(active, org, memberships);

        if (!await _deleted.HasRestorableAsync(user.UserId, ct))
            throw new NoActiveMembershipException(user.UserId);

        return new SessionTarget(null, null, []);
    }

    // Dos logins simultáneos revocan el mismo refresh anterior (IsRevoked es token de concurrencia), y un refresh de la
    // sesión vieja o un OwnershipStamp regenerado (D-45) chocan igual: el que confirma después recibe
    // DbUpdateConcurrencyException. Cada entrada pendiente toma los valores de la base y conserva solo sus cambios
    // propios; si ya no le queda ninguno (un refresh que otro ya revocó), se descarta. Se repite con el mismo sid: gana el
    // último que confirma, y el refresh del primero queda con un SessionId que ya no es el vigente (RF-90 g, R-28).
    // Se revisan todas las entradas pendientes y no solo las del conflicto: en PostgreSQL el SaveChanges fallido se
    // revierte entero, pero un proveedor sin transacciones (InMemory de la suite) deja escritas las anteriores al
    // conflicto, y repetirlas tal cual insertaría dos veces el refresh nuevo.
    private async Task SaveStartedSessionAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _uow.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException exception)
                when (attempt < MaxStartSessionAttempts && exception.Entries.Count > 0)
            {
                var pending = exception.Entries[0].Context.ChangeTracker.Entries()
                    .Where(e => e.State is EntityState.Added or EntityState.Modified);
                foreach (var entry in exception.Entries.Concat(pending).DistinctBy(e => e.Entity).ToList())
                {
                    if (!await TakeDatabaseValuesAsync(entry, ct))
                        throw;
                }
            }
        }
    }

    // false si la fila de una entrada modificada ya no existe y no es un refresh token (no hay valores que tomar).
    private static async Task<bool> TakeDatabaseValuesAsync(EntityEntry entry, CancellationToken ct)
    {
        var database = await entry.GetDatabaseValuesAsync(ct);

        if (entry.State == EntityState.Added)
        {
            // Ya insertada por el intento fallido (solo sin transacciones); si no, se vuelve a insertar.
            if (database is not null)
                entry.State = EntityState.Unchanged;
            return true;
        }

        if (database is null)
        {
            if (entry.Entity is not RefreshToken)
                return false;
            entry.State = EntityState.Detached;
            return true;
        }

        // Los cambios propios se capturan antes de tomar los valores de la base. Las demás propiedades quedan con el valor
        // de la base y sin marcar, para no pisar lo que confirmó la otra operación (p. ej. su OwnershipStamp).
        var modified = entry.Properties.Where(p => p.IsModified).ToList();
        var ownChanges = modified.Select(p => p.Metadata).ToHashSet();
        entry.OriginalValues.SetValues(database);
        foreach (var property in entry.Properties.Where(p => !ownChanges.Contains(p.Metadata)))
        {
            property.CurrentValue = database[property.Metadata];
            property.IsModified   = false;
        }

        if (modified.All(p => Equals(p.CurrentValue, database[p.Metadata])))
            entry.State = EntityState.Unchanged;
        return true;
    }

    private AuthTokenDto BuildResponse(User user, SessionTarget target, Guid sessionId, string refreshToken)
    {
        if (target.Active is not { } active || target.Organization is not { } org)
        {
            return new AuthTokenDto(
                _jwt.GenerateAccessTokenWithoutOrganization(user, sessionId),
                refreshToken,
                (int)_jwt.AccessTokenLifetime.TotalSeconds,
                new UserProfileDto(
                    user.UserId, user.FirstName, user.LastName, user.Email,
                    Plan: null, LegalType: null, user.Role,
                    ActiveOrgId:          null,
                    ActiveOrgName:        null,
                    ActiveOrgRole:        null,
                    TermsAcceptedAt:      user.TermsAcceptedAt,
                    TermsAcceptedVersion: user.TermsAcceptedVersion,
                    HasPassword:          user.PasswordHash is not null,
                    HasGoogle:            user.GoogleId is not null,
                    EmailVerified:        user.IsEmailVerified),
                []);
        }

        // El plan de la membresía ya es el efectivo del owner (IPlanResolutionService.ResolveForOwnersAsync).
        return new AuthTokenDto(
            _jwt.GenerateAccessToken(user, org, active.Role, active.Plan, sessionId),
            refreshToken,
            (int)_jwt.AccessTokenLifetime.TotalSeconds,
            new UserProfileDto(
                user.UserId, user.FirstName, user.LastName, user.Email,
                active.Plan, org.LegalType, user.Role,
                ActiveOrgId:          org.OrganizationId.ToString(),
                ActiveOrgName:        org.Name,
                ActiveOrgRole:        active.Role,
                TermsAcceptedAt:      user.TermsAcceptedAt,
                TermsAcceptedVersion: user.TermsAcceptedVersion,
                HasPassword:          user.PasswordHash is not null,
                HasGoogle:            user.GoogleId is not null,
                EmailVerified:        user.IsEmailVerified),
            target.Memberships.Select(m => m.ToDto()).ToList());
    }

    // Agrega el refresh token nuevo de la sesión; lo guarda el SaveChanges del llamador, junto con los cambios pendientes
    // del usuario (y la revocación del anterior en refresh).
    private async Task<string> AddRefreshTokenAsync(User user, Guid sessionId, CancellationToken ct)
    {
        var refreshToken = _jwt.GenerateRefreshToken();
        await _refreshTokens.AddAsync(
            RefreshToken.Create(user.UserId, refreshToken, DateTime.UtcNow.Add(RefreshTokenLifetime), sessionId), ct);
        return refreshToken;
    }

    // La preferida solo si su membresía sigue vigente (RF-16, RF-70); si no, la primera propia; si no, la más antigua.
    private static UserMembership? SelectActive(IReadOnlyList<UserMembership> memberships, Guid? preferredOrgId)
    {
        if (preferredOrgId is { } preferred
            && memberships.FirstOrDefault(m => m.OrganizationId == preferred) is { } chosen)
            return chosen;

        return memberships
            .OrderBy(m => m.Role == OrgRole.Owner ? 0 : 1)
            .ThenBy(m => m.JoinedAt)
            .FirstOrDefault();
    }

    // Organización activa y membresías de la sesión; Active y Organization nulos = sesión sin organización (D-44).
    private sealed record SessionTarget(
        UserMembership?                                   Active,
        global::Zentory.Domain.Entities.Organization?     Organization,
        IReadOnlyList<UserMembership>                     Memberships);
}

// Error genérico de las credenciales (RF-61, SR-0018.18): 422 con el mismo cuerpo para credenciales incorrectas y para un
// usuario sin membresías vigentes ni organizaciones restaurables, en login y en Google. No revela cuál de los casos fue.
public static class AuthCredentialErrors
{
    public const string Field   = "credentials";
    public const string Message = "Credenciales inválidas.";

    public static Exceptions.ValidationException InvalidCredentials()
        => new([new Exceptions.ValidationError(Field, Message)]);
}
