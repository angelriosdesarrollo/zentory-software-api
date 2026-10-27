using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Zentory.API.Authorization;
using Zentory.API.Extensions;
using Zentory.API.Http;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Security;
using Zentory.Infrastructure.Persistence;

namespace Zentory.API.Middleware;

// Revalida en cada petición del esquema de usuario que la membresía (active_org_id, sub) siga vigente (PLAN-0018 RF-14;
// ADR-0006 anotado; SR-0018.7; D-7). Va entre UseAuthentication y UseAuthorization, así que HttpContext.User es el del
// esquema por defecto: el token de colaborador no valida con Jwt:Key y llega sin autenticar.
//
// - Exclusión solo por metadato de endpoint ([SkipMembershipValidation]), después del routing; nunca por prefijo de
//   ruta, así una ruta en mayúsculas o con otra forma no se salta la comprobación.
// - Falla cerrado: un principal de usuario sin active_org_id o sub válidos, o sin membresía vigente, recibe 401 escrito
//   aquí mismo, sin ejecutar la acción. Una petición sin endpoint (404) también se valida.
// - Con membresía vigente puebla MembershipContext con el rol, el owner y el plan efectivo de la base (RF-40, RF-73;
//   ADR-0006 parte 3, anotado C2-f; D-26): el plan es el del owner de la organización, ResolveForOwners de su
//   suscripción active/trialing, o free sin ella; nunca el claim plan del token. Cuesta dos consultas indexadas como
//   máximo: membresía + organización + sesión + nombre del owner en una, y el plan del owner en la otra (RNF-1).
// - Organización eliminada (RF-65 c, d; D-31; SR-0018.26 a, b): la sesión de usuario con una membresía que sigue vigente
//   en una organización eliminada recibe 401 ORGANIZATION_DELETED. En los endpoints del esquema de colaboradores no hay
//   membresía que validar, pero el JWT de 7 días no basta: se autentica ese esquema aquí y, si la organización de la
//   sesión está eliminada, responde el mismo 401.
// - Sesión única (RF-90 d, DEC-0095, C-16; D-47): antes que la exclusión y que la membresía compara el sid del token con
//   users.current_session_id, y si difieren responde 401 SESSION_REPLACED (SessionReplacedResponse). Orden:
//   1. solo compara la sesión una acción que exige autorización (algún [Authorize] y ningún [AllowAnonymous], por
//      metadatos del endpoint y nunca por prefijo de ruta). Las [AllowAnonymous] (login, refresh, google, register,
//      logout, recuperación) y las públicas sin [Authorize] (/api/v1/public/*, decisión del orquestador que extiende
//      C-16) no la comparan: el cliente adjunta el Bearer también ahí y uno viejo bloquearía el login que lo reemplaza
//      o una página pública;
//   2. sin sid legible (SessionClaims) → 401 { error: "unauthorized" }, sin code (token anterior a M2c);
//   3. acción excluida ([SkipMembershipValidation]): lectura por clave primaria de CurrentSessionId;
//   4. acción con membresía: CurrentSessionId llega en la misma consulta de FindActiveAsync (RNF-1). Sin fila, la lectura
//      por clave va antes de distinguir ORGANIZATION_DELETED o MEMBERSHIP_REVOKED, así una sesión reemplazada siempre
//      recibe SESSION_REPLACED.
//   Un CurrentSessionId nulo no coincide con ningún sid.
// - Acciones que no exigen autorización (REV-0005 H-4): no validan membresía ni sesión; ver el comentario en InvokeAsync.
public sealed class MembershipValidationMiddleware
{
    public const string MembershipRevokedCode   = "MEMBERSHIP_REVOKED";
    public const string OrganizationDeletedCode = "ORGANIZATION_DELETED";

    // Token de usuario sin sid (RF-90 d; architecture §2.2): sin code, para que el cliente siga el camino de RF-18.
    public const string MissingSessionBody = "{\"error\":\"unauthorized\"}";

    public const string CollaboratorScheme = "CollaboratorScheme";

    private const string ActiveOrgClaim = "active_org_id";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly RequestDelegate _next;

    public MembershipValidationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext           context,
        MembershipContext     membership,
        IUserMembershipReader memberships,
        ZentoryDbContext      db)
    {
        var endpoint = context.GetEndpoint();

        if (context.User.Identity?.IsAuthenticated != true)
        {
            if (IsCollaboratorEndpoint(endpoint) && await IsCollaboratorOrganizationDeletedAsync(context, db))
            {
                await WriteOrganizationDeletedAsync(context);
                return;
            }

            await _next(context);
            return;
        }

        // REV-0005 H-4: una acción que no exige autorización ([AllowAnonymous] o sin [Authorize]: logout, aceptar una
        // invitación, rutas públicas) no depende de la membresía. Un Bearer válido de una sesión sin organización
        // (RF-85) o de una organización eliminada no la bloquea con MEMBERSHIP_REVOKED / ORGANIZATION_DELETED: pasa sin
        // poblar MembershipContext, y los comandos anónimos resuelven IsAuthenticated por su cuenta (el de aceptar compara
        // el sid, C-16). Sin endpoint (404) se sigue validando, y toda acción con [Authorize] queda igual.
        if (endpoint is not null && !RequiresAuthorization(endpoint))
        {
            await _next(context);
            return;
        }

        // Paso 1: solo comparan la sesión las acciones que exigen autorización; la membresía sigue como antes.
        var compareSession = RequiresAuthorization(endpoint);

        var sessionId = Guid.Empty;
        if (compareSession && !SessionClaims.TryRead(context.User, out sessionId))
        {
            await WriteMissingSessionAsync(context);
            return;
        }

        var hasUserId = TryReadGuid(context.User, ClaimTypes.NameIdentifier, out var userId);

        if (endpoint?.Metadata.GetMetadata<SkipMembershipValidationAttribute>() is not null)
        {
            if (compareSession)
            {
                if (!hasUserId)
                {
                    await WriteMissingSessionAsync(context);
                    return;
                }
                if (!await IsCurrentSessionAsync(db, userId, sessionId, context.RequestAborted))
                {
                    await SessionReplacedResponse.WriteAsync(context.Response);
                    return;
                }
            }

            await _next(context);
            return;
        }

        if (!hasUserId)
        {
            await WriteMembershipRevokedAsync(context);
            return;
        }

        if (!TryReadGuid(context.User, ActiveOrgClaim, out var organizationId))
        {
            if (compareSession && !await IsCurrentSessionAsync(db, userId, sessionId, context.RequestAborted))
                await SessionReplacedResponse.WriteAsync(context.Response);
            else
                await WriteMembershipRevokedAsync(context);
            return;
        }

        var current = await memberships.FindActiveAsync(userId, organizationId, context.RequestAborted);
        if (current is null)
        {
            // Solo en el camino de fallo: la sesión primero (sin fila no llegó CurrentSessionId) y después distingue la
            // organización eliminada de la membresía revocada.
            if (compareSession && !await IsCurrentSessionAsync(db, userId, sessionId, context.RequestAborted))
                await SessionReplacedResponse.WriteAsync(context.Response);
            else if (await IsOrganizationDeletedForMemberAsync(db, organizationId, userId, context.RequestAborted))
                await WriteOrganizationDeletedAsync(context);
            else
                await WriteMembershipRevokedAsync(context);
            return;
        }

        if (compareSession && current.CurrentSessionId != sessionId)
        {
            await SessionReplacedResponse.WriteAsync(context.Response);
            return;
        }

        // Plan efectivo del owner y su nombre (D-26): llegan de FindActiveAsync, que no suma más de una consulta a la de
        // la membresía (RNF-1: dos como máximo).
        membership.Resolve(organizationId, userId, current.Role, current.OwnerId, current.Plan, current.OwnerName);
        await _next(context);
    }

    // Lectura por clave primaria de la sesión vigente (D-47 paso 3), sin seguimiento. Un CurrentSessionId nulo, o un
    // usuario que no existe, no coincide con ningún sid.
    private static async Task<bool> IsCurrentSessionAsync(
        ZentoryDbContext db, Guid userId, Guid sessionId, CancellationToken ct)
    {
        var current = await db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => u.CurrentSessionId)
            .FirstOrDefaultAsync(ct);
        return current == sessionId;
    }

    // Entrada de la lista blanca de IgnoreQueryFilters (ADR-0008 §3; ADR purga §1): el filtro de Organization oculta
    // justo la fila que hay que ver. Predicado explícito: la organización del claim, eliminada, y una membresía vigente
    // del que llama (sin ella la respuesta es MEMBERSHIP_REVOKED y no revela el estado de una organización ajena).
    private static Task<bool> IsOrganizationDeletedForMemberAsync(
        ZentoryDbContext db, Guid organizationId, Guid userId, CancellationToken ct)
        => db.Organizations
            .IgnoreQueryFilters()
            .AnyAsync(o => o.OrganizationId == organizationId
                && o.DeletedAt != null
                && db.OrganizationMembers.Any(m => m.OrganizationId == organizationId
                    && m.UserId == userId
                    && m.DeletedAt == null), ct);

    // Sesión de colaborador: una lectura por clave de la organización de active_org_id. Un token que no valida no es
    // asunto de este middleware: la autorización del endpoint responde su 401.
    private static async Task<bool> IsCollaboratorOrganizationDeletedAsync(HttpContext context, ZentoryDbContext db)
    {
        var result = await context.AuthenticateAsync(CollaboratorScheme);
        if (!result.Succeeded || !TryReadGuid(result.Principal, ActiveOrgClaim, out var organizationId))
            return false;

        return await IsOrganizationDeletedAsync(db, organizationId, context.RequestAborted);
    }

    // Entrada de la lista blanca de IgnoreQueryFilters, con el mismo motivo: solo la organización del claim y eliminada.
    private static Task<bool> IsOrganizationDeletedAsync(ZentoryDbContext db, Guid organizationId, CancellationToken ct)
        => db.Organizations
            .IgnoreQueryFilters()
            .AnyAsync(o => o.OrganizationId == organizationId && o.DeletedAt != null, ct);

    // Sin FallbackPolicy en el host, una acción sin IAuthorizeData es anónima de hecho, igual que una con IAllowAnonymous.
    private static bool RequiresAuthorization(Endpoint? endpoint)
        => endpoint is not null
           && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
           && endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;

    private static bool IsCollaboratorEndpoint(Endpoint? endpoint)
        => endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data =>
               string.Equals(data.Policy, PolicyNames.CollaboratorAuth, StringComparison.Ordinal)
               || (data.AuthenticationSchemes?.Split(',').Any(scheme =>
                   string.Equals(scheme.Trim(), CollaboratorScheme, StringComparison.Ordinal)) ?? false))
           ?? false;

    private static bool TryReadGuid(ClaimsPrincipal user, string claimType, out Guid value)
        => Guid.TryParse(user.FindFirstValue(claimType), out value) && value != Guid.Empty;

    // Mismo cuerpo para "sin organización activa válida" y "membresía revocada": en los dos casos no hay membresía
    // vigente que respalde la petición, y no se distingue cuál de los dos ocurrió.
    private static Task WriteMembershipRevokedAsync(HttpContext context)
        => WriteUnauthorizedAsync(context, MembershipRevokedCode, "La membresía en la organización activa no está vigente.");

    private static Task WriteMissingSessionAsync(HttpContext context)
    {
        context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(MissingSessionBody);
    }

    private static Task WriteOrganizationDeletedAsync(HttpContext context)
        => WriteUnauthorizedAsync(context, OrganizationDeletedCode, "La organización activa fue eliminada.");

    private static async Task WriteUnauthorizedAsync(HttpContext context, string code, string message)
    {
        context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error   = "unauthorized",
            message,
            code,
            details = (object[]?)null,
        }, JsonOptions));
    }
}
