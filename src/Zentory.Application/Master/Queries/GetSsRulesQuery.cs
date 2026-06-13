using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Master.DTOs;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Application.Master.Queries;

public record GetSsRulesQuery(string CountryCode = "CO", short? Year = null)
    : IRequest<IReadOnlyList<SsRuleDto>>;

public sealed class GetSsRulesQueryHandler
    : IRequestHandler<GetSsRulesQuery, IReadOnlyList<SsRuleDto>>
{
    private readonly ZentoryDbContext _db;

    public GetSsRulesQueryHandler(ZentoryDbContext db) => _db = db;

    public async Task<IReadOnlyList<SsRuleDto>> Handle(
        GetSsRulesQuery   request,
        CancellationToken cancellationToken)
    {
        var year = request.Year ?? (short)DateTime.UtcNow.Year;

        var rules = await _db.SsRules
            .Where(r => r.CountryCode == request.CountryCode
                     && r.EffectiveYear == year
                     && r.Active)
            .ToListAsync(cancellationToken);

        // Fall back to most recent year if no rules for requested year
        if (rules.Count == 0)
        {
            var latestYear = await _db.SsRules
                .Where(r => r.CountryCode == request.CountryCode && r.Active)
                .MaxAsync(r => (short?)r.EffectiveYear, cancellationToken);

            if (latestYear.HasValue)
            {
                rules = await _db.SsRules
                    .Where(r => r.CountryCode == request.CountryCode
                             && r.EffectiveYear == latestYear.Value
                             && r.Active)
                    .ToListAsync(cancellationToken);
            }
        }

        return rules.Select(r => new SsRuleDto(
            r.Id,
            r.FundType,
            r.ContributorType,
            r.EmployeePct,
            r.EmployerPct,
            r.TotalPct,
            r.MinBaseSmlv,
            r.MaxBaseSmlv,
            r.SmlvCop,
            r.ArlLevel,
            r.Notes)).ToList();
    }
}
