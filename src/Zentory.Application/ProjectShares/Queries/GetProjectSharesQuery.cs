using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.ProjectShares.Commands;
using Zentory.Application.ProjectShares.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectShares.Queries;

public record GetProjectSharesQuery(Guid ProjectId) : IRequest<IReadOnlyList<ProjectShareDto>>;

public sealed class GetProjectSharesQueryHandler : IRequestHandler<GetProjectSharesQuery, IReadOnlyList<ProjectShareDto>>
{
    private readonly IProjectShareRepository _shares;
    private readonly ITenantContext          _tenant;

    public GetProjectSharesQueryHandler(IProjectShareRepository shares, ITenantContext tenant)
    {
        _shares = shares;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ProjectShareDto>> Handle(
        GetProjectSharesQuery request,
        CancellationToken     cancellationToken)
    {
        var shares = await _shares.ListByProjectAsync(request.ProjectId, cancellationToken);
        return shares
            .Where(s => s.OrganizationId == _tenant.OrganizationId)
            .Select(CreateProjectShareCommandHandler.ToDto)
            .ToList();
    }
}
