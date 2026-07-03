using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.ActivityLogs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;

namespace Zentory.Application.Proposals.Queries;

public record GetProposalActivityLogQuery(Guid ProposalId) : IRequest<IReadOnlyList<ActivityLogDto>>;

public sealed class GetProposalActivityLogQueryHandler
    : IRequestHandler<GetProposalActivityLogQuery, IReadOnlyList<ActivityLogDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext   _tenant;

    public GetProposalActivityLogQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ActivityLogDto>> Handle(
        GetProposalActivityLogQuery request,
        CancellationToken           cancellationToken)
    {
        if (_tenant.LegalType != LegalType.Empresa || _tenant.Plan == Plan.Free)
            throw new ForbiddenException(ForbiddenReason.PlanRequired, Plan.Pro);

        return await _db.ActivityLogs
            .Where(l => l.EntityType     == "Proposal"
                     && l.EntityId       == request.ProposalId
                     && l.OrganizationId == _tenant.OrganizationId)
            .OrderByDescending(l => l.OccurredAt)
            .Select(l => new ActivityLogDto(
                l.Id,
                l.UserInitials,
                l.Action,
                l.EntityCode ?? string.Empty,
                l.OccurredAt.ToString("o")))
            .ToListAsync(cancellationToken);
    }
}
