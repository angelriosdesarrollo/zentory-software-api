using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Application.Projects.Queries;

public record ProjectBillingEntryDto(
    Guid     Id,
    string   Concept,
    string   Period,
    int?     Hours,
    decimal  Rate,
    decimal? Amount,
    string   Status,
    string?  InvoiceRef,
    string   DueDate);

public record GetProjectBillingQuery(Guid ProjectId) : IRequest<IReadOnlyList<ProjectBillingEntryDto>>;

public sealed class GetProjectBillingQueryHandler
    : IRequestHandler<GetProjectBillingQuery, IReadOnlyList<ProjectBillingEntryDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext   _tenant;

    public GetProjectBillingQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ProjectBillingEntryDto>> Handle(
        GetProjectBillingQuery request,
        CancellationToken      cancellationToken)
    {
        return await _db.ProjectBillingEntries
            .Where(b => b.ProjectId == request.ProjectId && b.OrganizationId == _tenant.OrganizationId)
            .OrderBy(b => b.DueDate)
            .Select(b => new ProjectBillingEntryDto(
                b.Id,
                b.Concept,
                b.Period,
                b.Hours,
                b.Rate,
                b.Amount,
                b.Status,
                b.InvoiceRef,
                b.DueDate.ToString("yyyy-MM-dd")))
            .ToListAsync(cancellationToken);
    }
}
