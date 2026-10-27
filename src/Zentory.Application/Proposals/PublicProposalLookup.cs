using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals;

// Única puerta de las acciones por token público de una propuesta (GET, view, accept y reject). Una propuesta de una
// organización eliminada se trata como un token inexistente (PLAN-0018 RF-17 rev. 4, RF-65 e; SR-0018.26 c; D-31): así
// ninguna de las cuatro acciones la lee, la cambia ni manda correo. La condición sobre la organización es explícita y no
// depende del filtro global, para que siga valiendo aunque la consulta del token cambie a IgnoreQueryFilters.
internal static class PublicProposalLookup
{
    public static async Task<Proposal?> FindLiveByPublicTokenAsync(
        this IProposalRepository proposals, IZentoryDbContext db, Guid publicToken, CancellationToken ct)
    {
        var proposal = await proposals.GetByPublicTokenAsync(publicToken, ct);
        if (proposal is null)
            return null;

        var organizationIsLive = await db.Organizations.AnyAsync(
            o => o.OrganizationId == proposal.OrganizationId && o.DeletedAt == null, ct);
        return organizationIsLive ? proposal : null;
    }
}
