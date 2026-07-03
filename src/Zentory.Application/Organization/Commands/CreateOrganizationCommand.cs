using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

public record CreateOrganizationCommand(
    string  LegalType,
    string  Name,
    string  Country  = "CO",
    string? WorkType = null) : IRequest<SwitchOrgResponseDto>;

public sealed class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.LegalType)
            .Must(t => t is Zentory.Domain.Constants.LegalType.Freelance or Zentory.Domain.Constants.LegalType.Empresa)
            .WithMessage("LegalType must be 'freelance' or 'empresa'.");
        RuleFor(x => x.Country).NotEmpty().MaximumLength(5);
        RuleFor(x => x.WorkType)
            .Must(t => t is null || Zentory.Domain.Constants.WorkType.All.Contains(t))
            .WithMessage($"WorkType must be one of: {string.Join(", ", Zentory.Domain.Constants.WorkType.All)}.");
    }
}

public sealed class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, SwitchOrgResponseDto>
{
    private readonly IZentoryDbContext       _db;
    private readonly IOrganizationRepository _organizations;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;
    private readonly IPlanLimitService       _planLimits;
    private readonly IMediator               _mediator;

    public CreateOrganizationCommandHandler(
        IZentoryDbContext       db,
        IOrganizationRepository organizations,
        IUnitOfWork             uow,
        ITenantContext          tenant,
        IPlanLimitService       planLimits,
        IMediator               mediator)
    {
        _db            = db;
        _organizations = organizations;
        _uow           = uow;
        _tenant        = tenant;
        _planLimits    = planLimits;
        _mediator      = mediator;
    }

    public async Task<SwitchOrgResponseDto> Handle(CreateOrganizationCommand request, CancellationToken ct)
    {
        // The limit is about how many orgs this user OWNS, not the resources inside any single
        // org — Plan/LegalType are looked up the same way as any other quantitative plan limit
        // (see TASK-BE-031 Fase 3): free = 1 owned org, pro/studio = unlimited.
        var limit = await _planLimits.GetLimitAsync(
            _tenant.Plan,
            _tenant.LegalType,
            PlanLimits.FeatureKeys.MaxOwnedOrgs,
            ct);

        if (limit.HasValue)
        {
            var ownedCount = await _db.Organizations
                .CountAsync(o => o.OwnerId == _tenant.UserId, ct);

            if (ownedCount >= limit.Value)
                throw new QuotaExceededException(PlanLimits.FeatureKeys.MaxOwnedOrgs, limit.Value, ownedCount);
        }

        var org = global::Zentory.Domain.Entities.Organization.Create(request.Name, request.LegalType, request.Country);
        org.SetOwner(_tenant.UserId);

        await _organizations.AddAsync(org, ct);
        _db.OrganizationMembers.Add(OrganizationMember.Create(org.OrganizationId, _tenant.UserId, "owner"));

        if (request.WorkType is not null)
            _db.OrganizationSettings.Add(OrganizationSettings.Set(org.OrganizationId, "profile.work_type", request.WorkType));

        await _uow.SaveChangesAsync(ct);

        // Reuse the switch flow to mint a fresh access token active on the new org and the
        // refreshed memberships list — same response shape the frontend already handles.
        return await _mediator.Send(new SwitchActiveOrganizationCommand(org.OrganizationId), ct);
    }
}
