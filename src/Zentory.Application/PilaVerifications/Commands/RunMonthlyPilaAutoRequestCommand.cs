using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PilaVerifications.Commands;

// Job mensual (Hangfire, día 25): cierra la brecha de "la empresa se olvidó de
// pedir la planilla". Solo crea solicitudes para colaboradores del período
// siguiente que todavía no tienen ninguna fila ese mes — si ya existe una
// verificación (incluso 'rechazada'), se deja intacta para que el reintento sea
// una decisión manual desde "Solicitar planilla", no un reenvío automático.
public sealed record RunMonthlyPilaAutoRequestCommand : IRequest<MonthlyPilaAutoRequestResult>;

public sealed record MonthlyPilaAutoRequestResult(
    int OrganizationsProcessed,
    int Requested,
    int Skipped);

public sealed class RunMonthlyPilaAutoRequestCommandHandler
    : IRequestHandler<RunMonthlyPilaAutoRequestCommand, MonthlyPilaAutoRequestResult>
{
    private readonly IOrganizationRepository     _organizations;
    private readonly ICollaboratorRepository     _collaborators;
    private readonly IPilaVerificationRepository _verifications;
    private readonly IPlanResolutionService      _plans;
    private readonly IZentoryDbContext           _db;
    private readonly IUnitOfWork                 _uow;
    private readonly IEmailService               _email;
    private readonly IApplicationSettings        _settings;

    public RunMonthlyPilaAutoRequestCommandHandler(
        IOrganizationRepository     organizations,
        ICollaboratorRepository     collaborators,
        IPilaVerificationRepository verifications,
        IPlanResolutionService      plans,
        IZentoryDbContext           db,
        IUnitOfWork                 uow,
        IEmailService               email,
        IApplicationSettings        settings)
    {
        _organizations = organizations;
        _collaborators = collaborators;
        _verifications = verifications;
        _plans         = plans;
        _db            = db;
        _uow           = uow;
        _email         = email;
        _settings      = settings;
    }

    public async Task<MonthlyPilaAutoRequestResult> Handle(
        RunMonthlyPilaAutoRequestCommand request, CancellationToken cancellationToken)
    {
        var period = NextPeriod();

        var organizations = await _organizations.ListActiveByLegalTypeAsync(LegalType.Empresa, cancellationToken);
        if (organizations.Count == 0)
            return new MonthlyPilaAutoRequestResult(0, 0, 0);

        var ownerIds = organizations
            .Where(o => o.OwnerId.HasValue)
            .Select(o => o.OwnerId!.Value)
            .Distinct();
        var plans = await _plans.ResolveForOwnersAsync(ownerIds, cancellationToken);

        var eligibleOrgs = organizations
            .Where(o => o.OwnerId.HasValue
                && plans.TryGetValue(o.OwnerId.Value, out var plan)
                && plan == Plan.Studio)
            .ToList();

        if (eligibleOrgs.Count == 0)
            return new MonthlyPilaAutoRequestResult(0, 0, 0);

        var smlv = await GetCurrentSmlvAsync(cancellationToken);

        var requested = 0;
        var skipped   = 0;

        foreach (var org in eligibleOrgs)
        {
            var collaborators = await _collaborators.ListAsync(
                org.OrganizationId, search: null, status: "active", cancellationToken);
            var existing = await _verifications.ListByOrganizationAndPeriodAsync(
                org.OrganizationId, period, cancellationToken);
            var alreadyHasRow = existing.Select(v => v.CollaboratorId).ToHashSet();

            var toNotify = new List<(Collaborator Collaborator, PilaVerification Verification)>();

            foreach (var collaborator in collaborators.Where(c => c.Type != "employee"))
            {
                if (alreadyHasRow.Contains(collaborator.Id)
                    || string.IsNullOrWhiteSpace(collaborator.Email)
                    || !PilaEligibilityRules.IsEligible(collaborator, smlv))
                {
                    skipped++;
                    continue;
                }

                var verification = PilaVerification.Create(org.OrganizationId, collaborator.Id, period);
                await _verifications.AddAsync(verification, cancellationToken);

                collaborator.UpdatePilaStatus("solicitada", collaborator.PilaLastVerifiedPeriod);
                await _collaborators.UpdateAsync(collaborator, cancellationToken);

                toNotify.Add((collaborator, verification));
                requested++;
            }

            if (toNotify.Count == 0) continue;

            await _uow.SaveChangesAsync(cancellationToken);

            foreach (var (collaborator, verification) in toNotify)
            {
                var url = $"{_settings.BaseUrl}/portal/entrar?token={verification.Token}&kind=pila_request";
                await _email.SendPilaRequestEmailAsync(
                    collaborator.Email!,
                    collaborator.Name,
                    org.Name,
                    period,
                    url,
                    verification.TokenExpiresAt ?? DateTime.UtcNow.AddDays(30),
                    cancellationToken);
            }
        }

        return new MonthlyPilaAutoRequestResult(eligibleOrgs.Count, requested, skipped);
    }

    private static string NextPeriod()
    {
        var next = DateTime.UtcNow.AddMonths(1);
        return $"{next.Year:D4}-{next.Month:D2}";
    }

    private async Task<decimal?> GetCurrentSmlvAsync(CancellationToken ct)
    {
        var year = (short)DateTime.UtcNow.Year;
        var rule = await _db.SsRules
            .Where(r => r.CountryCode == "CO" && r.EffectiveYear == year && r.Active)
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(ct);
        return rule?.SmlvCop;
    }
}
