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

// POST /organizations (PLAN-0018 RF-74 a; SR-0018.35; D-30, D-45; C-9). Acción de usuario con [SkipMembershipValidation]:
// la organización activa no cuenta, ni su plan ni su legalType. max_owned_orgs se evalúa con el plan propio del que
// llama (ResolveForOwnerAsync) y el legalType de la organización que se crea. La escritura va en una transacción
// que termina regenerando el OwnershipStamp del usuario (D-45, como RestoreOrganizationCommand): de dos creaciones o
// restauraciones simultáneas del mismo usuario, la segunda pierde, vuelve a contar una vez y responde 422 si ya no cabe.
public sealed class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, SwitchOrgResponseDto>
{
    public const string ChangedCode    = "OWNERSHIP_CHANGED";
    public const string ChangedMessage = "Tus organizaciones cambiaron mientras se creaba esta. Vuelve a intentarlo.";

    // Un intento y un único reintento tras perder ante otra operación del mismo usuario sobre max_owned_orgs (D-45).
    private const int MaxAttempts = 2;

    private readonly IZentoryDbContext       _db;
    private readonly IOrganizationRepository _organizations;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;
    private readonly IPlanResolutionService  _plans;
    private readonly IPlanLimitService       _planLimits;
    private readonly IMediator               _mediator;

    public CreateOrganizationCommandHandler(
        IZentoryDbContext       db,
        IOrganizationRepository organizations,
        IUnitOfWork             uow,
        ITenantContext          tenant,
        IPlanResolutionService  plans,
        IPlanLimitService       planLimits,
        IMediator               mediator)
    {
        _db            = db;
        _organizations = organizations;
        _uow           = uow;
        _tenant        = tenant;
        _plans         = plans;
        _planLimits    = planLimits;
        _mediator      = mediator;
    }

    public async Task<SwitchOrgResponseDto> Handle(CreateOrganizationCommand request, CancellationToken ct)
    {
        var userId = _tenant.UserId;
        Guid organizationId;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                organizationId = await CreateAsync(request, userId, ct);
                break;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                if (attempt >= MaxAttempts)
                    throw new ConflictException(ChangedCode, ChangedMessage);

                // Otra creación o restauración del mismo usuario ganó: se descarta lo pendiente y se vuelve a contar.
                exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            }
        }

        // Reuse the switch flow to mint a fresh access token active on the new org and the
        // refreshed memberships list — same response shape the frontend already handles.
        return await _mediator.Send(new SwitchActiveOrganizationCommand(organizationId), ct);
    }

    private async Task<Guid> CreateAsync(CreateOrganizationCommand request, Guid userId, CancellationToken ct)
    {
        // El usuario primero: su OwnershipStamp debe ser el leído antes de contar (D-45).
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw new NotFoundException("User", userId);

        await EnsureOwnedOrganizationQuotaAsync(request.LegalType, userId, ct);

        var org = global::Zentory.Domain.Entities.Organization.Create(request.Name, request.LegalType, request.Country);
        org.SetOwner(userId);
        var owner = OrganizationMember.Create(org.OrganizationId, userId, OrgRole.Owner);
        var workType = request.WorkType is null
            ? null
            : OrganizationSettings.Set(org.OrganizationId, OrgSettingKey.ProfileWorkType, request.WorkType);

        // Una transacción con dos escrituras: primero la organización y después el sello del usuario, que es la que
        // compite. Así la escritura con el token de concurrencia lleva solo el User: si otra operación ganó, falla sin
        // haber escrito nada más, también en un proveedor sin transacciones (EF InMemory de la suite), donde un
        // SaveChanges con varias filas no es atómico. Quien lea el sello nuevo ya ve la organización creada.
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            await _organizations.AddAsync(org, token);
            _db.OrganizationMembers.Add(owner);
            if (workType is not null)
                _db.OrganizationSettings.Add(workType);
            await _uow.SaveChangesAsync(token);

            user.TouchOwnership();
            try
            {
                await _uow.SaveChangesAsync(token);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                // Perdió ante otra creación o restauración del mismo usuario. Con transacción (PostgreSQL) el rollback
                // deshace la organización; sin ella se borra aquí para no dejarla huérfana. Después se vuelve a contar.
                exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
                if (workType is not null)
                    _db.OrganizationSettings.Remove(workType);
                _db.OrganizationMembers.Remove(owner);
                _db.Organizations.Remove(org);
                await _uow.SaveChangesAsync(token);
                throw;
            }
        }, ct);

        return org.OrganizationId;
    }

    // RF-74 a: cuenta las organizaciones propias vigentes (el filtro global deja fuera las eliminadas) con el límite del
    // plan propio; ser member o admin en organizaciones ajenas no cuenta (FEATURE_PERMISSIONS §3).
    private async Task EnsureOwnedOrganizationQuotaAsync(string legalType, Guid userId, CancellationToken ct)
    {
        var plan  = await _plans.ResolveForOwnerAsync(userId, ct);
        var limit = await _planLimits.GetLimitAsync(plan, legalType, PlanLimits.FeatureKeys.MaxOwnedOrgs, ct);
        if (limit is not { } max)
            return;

        var ownedCount = await _db.Organizations.CountAsync(o => o.OwnerId == userId, ct);
        if (ownedCount >= max)
            throw new QuotaExceededException(PlanLimits.FeatureKeys.MaxOwnedOrgs, max, ownedCount);
    }
}
