using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Security;
using Zentory.Domain.Common;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// Parte 4 de los escenarios (PLAN-0018 RF-68, CA-92): una persona sin organización con una invitación pendiente a la
/// Agencia con rol member. No es una organización más: la corre <see cref="AgencyScenario"/> al final, porque la
/// invitación es de la Agencia y la emite su admin. La vigencia se cuenta desde el día de referencia.
/// </summary>
public static class PendingInvitationScenario
{
    public static readonly DemoPerson Invitee = new(
        new Guid("b2000000-0000-0000-0000-000000000009"), DemoPeople.Email("tomas.herrera"), "Tomás", "Herrera");

    public static readonly Guid InvitationId = DemoIds.For(AgencyScenario.OrganizationId, $"invitation:{Invitee.Id:N}");

    public const string Role = OrgRole.Member;

    // Emitida hace dos días: con la vigencia de RF-49 c le quedan cinco respecto al día de referencia.
    public const int IssuedOffsetDays = -2;

    public static async Task SeedAsync(DemoSeedContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.EnsureUserAsync(Invitee, OrgRole.Member, ct);

        var organizationId = AgencyScenario.OrganizationId;
        var email          = EmailAddress.Normalize(Invitee.Email);
        var db             = context.Db;

        // Idempotente: con el ID fijo, o con cualquier invitación previa a ese email en la Agencia (aceptada, revocada o
        // emitida desde la UI), no se inserta otra; si ya es miembro, tampoco. Nunca se toca una fila existente.
        var invited = await db.OrganizationInvitations.IgnoreQueryFilters()
            .AnyAsync(i => i.Id == InvitationId || (i.OrganizationId == organizationId && i.Email == email), ct);
        if (invited) return;

        var member = await db.OrganizationMembers.IgnoreQueryFilters()
            .AnyAsync(m => m.OrganizationId == organizationId && m.UserId == Invitee.Id, ct);
        if (member) return;

        // El mismo emisor que InviteMemberCommand. Solo se guarda el hash: el valor en claro se descarta aquí, sin
        // persistirse, registrarse ni imprimirse (SR-0018.10). Para usarla en local se emite una nueva desde la UI.
        var tokenHash  = OneTimeToken.Issue().Hash;
        var invitation = OrganizationInvitation.Issue(
            organizationId, email, Role, tokenHash, DemoPeople.AgencyAdmin.Id, context.UtcDay(IssuedOffsetDays, 10));
        context.WithId(invitation, InvitationId);

        db.OrganizationInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);
    }
}
