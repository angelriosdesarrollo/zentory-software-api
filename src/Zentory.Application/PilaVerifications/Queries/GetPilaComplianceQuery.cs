using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.PilaVerifications.DTOs;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PilaVerifications.Queries;

public record GetPilaComplianceQuery(string Period) : IRequest<IReadOnlyList<PilaComplianceRowDto>>;

public sealed class GetPilaComplianceQueryHandler
    : IRequestHandler<GetPilaComplianceQuery, IReadOnlyList<PilaComplianceRowDto>>
{
    private readonly ICollaboratorRepository     _collaborators;
    private readonly IPilaVerificationRepository _verifications;
    private readonly IZentoryDbContext           _db;
    private readonly ITenantContext              _tenant;

    public GetPilaComplianceQueryHandler(
        ICollaboratorRepository     collaborators,
        IPilaVerificationRepository verifications,
        IZentoryDbContext           db,
        ITenantContext              tenant)
    {
        _collaborators = collaborators;
        _verifications = verifications;
        _db            = db;
        _tenant        = tenant;
    }

    public async Task<IReadOnlyList<PilaComplianceRowDto>> Handle(
        GetPilaComplianceQuery request, CancellationToken cancellationToken)
    {
        var collaborators = await _collaborators.ListAsync(
            _tenant.OrganizationId, search: null, status: "active", cancellationToken);

        var verifications = await _verifications.ListByOrganizationAndPeriodAsync(
            _tenant.OrganizationId, request.Period, cancellationToken);
        var byCollaborator = verifications.ToDictionary(v => v.CollaboratorId);

        var smlv = await GetCurrentSmlvAsync(cancellationToken);

        return collaborators
            .Where(c => c.Type != "employee")
            .Select(c =>
            {
                byCollaborator.TryGetValue(c.Id, out var v);
                var status = v?.Status ?? (PilaEligibilityRules.IsEligible(c, smlv) ? "pendiente" : "no_aplica");

                return new PilaComplianceRowDto(
                    c.Id,
                    c.Name,
                    c.Role,
                    c.Type,
                    c.Email,
                    c.HourlyRate,
                    c.MonthlyRate,
                    c.Currency,
                    v?.Id,
                    status,
                    v?.RequestedAt,
                    v?.ReceivedAt,
                    v?.VerifiedAt,
                    c.PayoutInvoiceStatus,
                    c.PayoutInvoiceLastPeriod);
            })
            .ToList();
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
