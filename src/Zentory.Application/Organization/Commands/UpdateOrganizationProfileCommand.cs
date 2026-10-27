using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.DTOs;
using Zentory.Application.Organization.Queries;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

public record UpdateOrganizationProfileCommand(
    string? LegalName,
    string? LogoUrl,
    string? Nit,
    string? CompanyType,
    string? WorkType,
    string? LegalRep,
    string? LegalRepId,
    string? TaxRegime,
    string? Ciiu,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? Country) : IRequest<OrganizationProfileDto>;

public sealed class UpdateOrganizationProfileCommandValidator : AbstractValidator<UpdateOrganizationProfileCommand>
{
    public UpdateOrganizationProfileCommandValidator()
    {
        RuleFor(x => x.WorkType)
            .Must(t => t is null || Zentory.Domain.Constants.WorkType.All.Contains(t))
            .WithMessage($"WorkType must be one of: {string.Join(", ", Zentory.Domain.Constants.WorkType.All)}.");
    }
}

public sealed class UpdateOrganizationProfileCommandHandler
    : IRequestHandler<UpdateOrganizationProfileCommand, OrganizationProfileDto>
{
    private readonly IZentoryDbContext      _db;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;
    private readonly IPlanResolutionService _plans;
    private readonly IActivityLogService    _activityLog;

    public UpdateOrganizationProfileCommandHandler(
        IZentoryDbContext      db,
        IUnitOfWork            uow,
        ITenantContext         tenant,
        IPlanResolutionService plans,
        IActivityLogService    activityLog)
    {
        _activityLog = activityLog;
        _db     = db;
        _uow    = uow;
        _plans  = plans;
        _tenant = tenant;
    }

    public async Task<OrganizationProfileDto> Handle(
        UpdateOrganizationProfileCommand command,
        CancellationToken                cancellationToken)
    {
        var orgId = _tenant.OrganizationId;

        var org = await _db.Organizations
            .FirstOrDefaultAsync(o => o.OrganizationId == orgId, cancellationToken);

        if (org is null) throw new NotFoundException("Organization", orgId);

        // Profile fields are stored in OrganizationSettings as key-value pairs
        var profileFields = new Dictionary<string, string?>
        {
            ["profile.legal_name"]  = command.LegalName,
            ["profile.logo_url"]    = command.LogoUrl,
            ["profile.nit"]         = command.Nit,
            ["profile.company_type"]= command.CompanyType,
            ["profile.work_type"]   = command.WorkType,
            ["profile.legal_rep"]   = command.LegalRep,
            ["profile.legal_rep_id"]= command.LegalRepId,
            ["profile.tax_regime"]  = command.TaxRegime,
            ["profile.ciiu"]        = command.Ciiu,
            ["profile.email"]       = command.Email,
            ["profile.phone"]       = command.Phone,
            ["profile.address"]     = command.Address,
            ["profile.city"]        = command.City,
        };

        var existing = await _db.OrganizationSettings
            .Where(s => s.OrganizationId == orgId && profileFields.Keys.Contains(s.Key))
            .ToListAsync(cancellationToken);

        var existingDict = existing.ToDictionary(s => s.Key);

        // PATCH parcial (REV-0005 H-7): solo cambian las propiedades presentes en el cuerpo. El cliente tipa el cuerpo como
        // Partial<OrganizationProfile> y el onboarding manda solo { workType }; asignar también las ausentes borraba el
        // resto del perfil. Un campo se vacía con "" (el cuerpo se deserializa a un record y null no se distingue de ausente).
        foreach (var (key, value) in profileFields)
        {
            if (value is null) continue;

            var previous = existingDict.TryGetValue(key, out var row) ? row.Value : null;

            if (row is not null)
                row.Update(value);
            else
                _db.OrganizationSettings.Add(OrganizationSettings.Set(orgId, key, value));

            // RF-89, SR-0018.45: NIT, razón social y datos bancarios dejan ActivityLog con el valor anterior y el nuevo,
            // también por esta ruta (la que usa la interfaz).
            if (OrganizationSettingsAudit.IsAuditedChange(key, previous, value))
                await OrganizationSettingsAudit.LogChangeAsync(_activityLog, orgId, key, previous, value, cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);

        // Re-read to return current state
        return await new GetOrganizationProfileQueryHandler(_db, _tenant, _plans)
            .Handle(new GetOrganizationProfileQuery(), cancellationToken);
    }
}
