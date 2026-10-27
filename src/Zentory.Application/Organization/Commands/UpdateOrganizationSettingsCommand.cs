using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Organization.DTOs;
using Zentory.Application.Organization.Queries;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

public record UpdateOrganizationSettingsCommand(Dictionary<string, string?> Settings)
    : IRequest<OrganizationSettingsDto>;

public sealed class UpdateOrganizationSettingsCommandHandler
    : IRequestHandler<UpdateOrganizationSettingsCommand, OrganizationSettingsDto>
{
    // EntityType de las filas de ActivityLog que deja este comando; EntityId es la organización.
    public const string SettingsEntityType = OrganizationSettingsAudit.EntityType;

    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;
    private readonly IActivityLogService _activityLog;

    public UpdateOrganizationSettingsCommandHandler(
        IZentoryDbContext   db,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog)
    {
        _db          = db;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
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

        // Las claves ya pasaron la lista blanca de UpdateOrganizationSettingsCommandValidator (RF-89).
        foreach (var (key, value) in command.Settings)
        {
            var previous = existingDict.TryGetValue(key, out var row) ? row.Value : null;

            if (row is not null)
                row.Update(value);
            else
                _db.OrganizationSettings.Add(OrganizationSettings.Set(orgId, key, value));

            if (OrganizationSettingsAudit.IsAuditedChange(key, previous, value))
                await OrganizationSettingsAudit.LogChangeAsync(_activityLog, orgId, key, previous, value, cancellationToken);
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
