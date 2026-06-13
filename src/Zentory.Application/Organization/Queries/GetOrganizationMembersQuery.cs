using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Organization.DTOs;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Application.Organization.Queries;

public record GetOrganizationMembersQuery : IRequest<IReadOnlyList<OrganizationMemberDto>>;

public sealed class GetOrganizationMembersQueryHandler
    : IRequestHandler<GetOrganizationMembersQuery, IReadOnlyList<OrganizationMemberDto>>
{
    private readonly ZentoryDbContext _db;
    private readonly ITenantContext   _tenant;

    public GetOrganizationMembersQueryHandler(ZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<OrganizationMemberDto>> Handle(
        GetOrganizationMembersQuery request,
        CancellationToken           cancellationToken)
    {
        var members = await _db.OrganizationMembers
            .Where(m => m.OrganizationId == _tenant.OrganizationId)
            .Join(
                _db.Users,
                m => m.UserId,
                u => u.UserId,
                (m, u) => new OrganizationMemberDto(
                    u.UserId,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    m.Role,
                    m.JoinedAt))
            .ToListAsync(cancellationToken);

        return members;
    }
}
