using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Organization.DTOs;
using Zentory.Application.Organization.Queries;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

public record UpdateOrganizationSettingsCommand(Dictionary<string, string?> Settings)
    : IRequest<OrganizationSettingsDto>;

public sealed class UpdateOrganizationSettingsCommandHandler
    : IRequestHandler<UpdateOrganizationSettingsCommand, OrganizationSettingsDto>
{
    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;

    public UpdateOrganizationSettingsCommandHandler(
        IZentoryDbContext db,
        IUnitOfWork       uow,
        ITenantContext    tenant)
    {
        _db     = db;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task<OrganizationSettingsDto> Handle(
        UpdateOrganizationSettingsCommand command,
        CancellationToken                 cancellationToken)
    {
        var orgId = _tenant.OrganizationId;

        var existing = await _db.OrganizationSettings
            .Where(s => s.OrganizationId == orgId)
            .ToListAsync(cancellationToken);

        var existingDict = existing.ToDictionary(s => s.Key);

        foreach (var (key, value) in command.Settings)
        {
            if (existingDict.TryGetValue(key, out var row))
                row.Update(value);
            else
                _db.OrganizationSettings.Add(OrganizationSettings.Set(orgId, key, value));
        }

        await _uow.SaveChangesAsync(cancellationToken);

        // Return the current full state
        var all = await _db.OrganizationSettings
            .Where(s => s.OrganizationId == orgId)
            .ToListAsync(cancellationToken);

        var dict = all.ToDictionary(s => s.Key, s => s.Value);
        foreach (var (key, value) in DefaultOrgSettings.All)
            dict.TryAdd(key, value);

        return new OrganizationSettingsDto(dict);
    }
}
