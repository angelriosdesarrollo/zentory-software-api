using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.ProjectShares.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectShares.Queries;

public record GetProjectSharePublicQuery(string Token) : IRequest<ProjectSharePublicDto>;

public sealed class GetProjectSharePublicQueryHandler : IRequestHandler<GetProjectSharePublicQuery, ProjectSharePublicDto>
{
    private readonly IProjectShareRepository _shares;
    private readonly IZentoryDbContext        _db;

    public GetProjectSharePublicQueryHandler(IProjectShareRepository shares, IZentoryDbContext db)
    {
        _shares = shares;
        _db     = db;
    }

    public async Task<ProjectSharePublicDto> Handle(
        GetProjectSharePublicQuery request,
        CancellationToken          cancellationToken)
    {
        var share = await _shares.GetByTokenAsync(request.Token, cancellationToken);
        if (share is null)
            throw new NotFoundException("ProjectShare", request.Token);

        if (share.ExpiresAt.HasValue && share.ExpiresAt.Value < DateTime.UtcNow)
            throw new NotFoundException("ProjectShare", request.Token);

        // Load project data bypassing tenant query filters (public endpoint — no auth context).
        var project = await _db.Projects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == share.ProjectId, cancellationToken);

        if (project is null)
            throw new NotFoundException("Project", share.ProjectId);

        var client = await _db.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == project.ClientId, cancellationToken);

        var milestones = await _db.ProjectMilestones
            .IgnoreQueryFilters()
            .Where(m => m.ProjectId == share.ProjectId)
            .OrderBy(m => m.DueDate)
            .ToListAsync(cancellationToken);

        // Progress: percentage of DONE tasks out of total tasks.
        var tasks = await _db.ProjectTasks
            .IgnoreQueryFilters()
            .Where(t => t.ProjectId == share.ProjectId && t.DeletedAt == null)
            .ToListAsync(cancellationToken);

        int total    = tasks.Count;
        int done     = tasks.Count(t => t.Status == "done" || t.Status == "DONE");
        int progress = total > 0 ? (int)Math.Round(done * 100.0 / total) : 0;

        // Health: red if past end date and not completed, yellow if >2 weeks past due.
        string healthStatus = "green";
        if (project.EndDate.HasValue)
        {
            var daysLate = (DateTime.UtcNow - project.EndDate.Value).TotalDays;
            if (daysLate > 14) healthStatus = "red";
            else if (daysLate > 0) healthStatus = "yellow";
        }

        // Documents: files + deliverables selected for this share.
        var fileIds        = new HashSet<string>(share.IncludedFileIds);
        var deliverableIds = new HashSet<string>(share.IncludedDeliverableIds);

        var documents = new List<ProjectShareDocumentDto>();

        if (deliverableIds.Count > 0)
        {
            var deliverables = await _db.ProjectDeliverables
                .IgnoreQueryFilters()
                .Where(d => d.ProjectId == share.ProjectId && deliverableIds.Contains(d.Id.ToString()))
                .ToListAsync(cancellationToken);

            documents.AddRange(deliverables.Select(d => new ProjectShareDocumentDto(
                d.Id.ToString(), d.Name, "file", null, null, null, "deliverable")));
        }

        if (fileIds.Count > 0)
        {
            var files = await _db.ProjectFiles
                .IgnoreQueryFilters()
                .Where(f => f.ProjectId == share.ProjectId && fileIds.Contains(f.Id.ToString()))
                .ToListAsync(cancellationToken);

            documents.AddRange(files.Select(f => new ProjectShareDocumentDto(
                f.Id.ToString(), f.Name, "file", f.FileType, f.Size, null, "file")));
        }

        return new ProjectSharePublicDto(
            project.Name,
            client?.Name ?? string.Empty,
            progress,
            healthStatus,
            project.StartDate?.ToString("yyyy-MM-dd"),
            project.EndDate?.ToString("yyyy-MM-dd"),
            share.Message,
            milestones.Select(m => new ProjectShareMilestoneDto(
                m.Id.ToString(), m.Name, m.Status, m.DueDate?.ToString("yyyy-MM-dd") ?? "", m.Value)).ToList(),
            documents);
    }
}
