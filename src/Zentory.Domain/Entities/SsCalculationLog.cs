using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class SsCalculationLog : BaseEntity
{
    public Guid     OrganizationId     { get; private set; }
    public Guid     UserId             { get; private set; }  // quien ejecutó el cálculo
    public Guid?    CollaboratorId     { get; private set; }  // NULL = cálculo propio del freelance

    public string   CountryCode        { get; private set; } = default!;
    public string   Period             { get; private set; } = default!;  // 'YYYY-MM'
    public decimal  Income             { get; private set; }
    public string   Currency           { get; private set; } = default!;

    public string   Result             { get; private set; } = default!;  // JSONB snapshot
    public decimal  TotalContribution  { get; private set; }
    public decimal? SmlvUsed           { get; private set; }


    private SsCalculationLog() { }

    public static SsCalculationLog Create(
        Guid    organizationId,
        Guid    userId,
        string  countryCode,
        string  period,
        decimal income,
        string  currency,
        string  resultJson,
        decimal totalContribution,
        decimal? smlvUsed         = null,
        Guid?   collaboratorId    = null)
    {
        return new SsCalculationLog
        {
            OrganizationId    = organizationId,
            UserId            = userId,
            CollaboratorId    = collaboratorId,
            CountryCode       = countryCode,
            Period            = period,
            Income            = income,
            Currency          = currency,
            Result            = resultJson,
            TotalContribution = totalContribution,
            SmlvUsed          = smlvUsed
        };
    }
}
