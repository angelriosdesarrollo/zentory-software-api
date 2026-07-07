using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Organization.DTOs;

namespace Zentory.Application.Organization.Queries;

public record GetOrganizationSettingsQuery : IRequest<OrganizationSettingsDto>;

public sealed class GetOrganizationSettingsQueryHandler
    : IRequestHandler<GetOrganizationSettingsQuery, OrganizationSettingsDto>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetOrganizationSettingsQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<OrganizationSettingsDto> Handle(
        GetOrganizationSettingsQuery request,
        CancellationToken            cancellationToken)
    {
        var rows = await _db.OrganizationSettings
            .Where(s => s.OrganizationId == _tenant.OrganizationId)
            .ToListAsync(cancellationToken);

        var dict = rows.ToDictionary(s => s.Key, s => s.Value);

        // Inject defaults for missing keys so the frontend always receives a complete object
        foreach (var (key, value) in DefaultOrgSettings.All)
            dict.TryAdd(key, value);

        return new OrganizationSettingsDto(dict);
    }
}

// Defaults applied when the key is absent — single source of truth for initial values
public static class DefaultOrgSettings
{
    public static readonly Dictionary<string, string?> All = new()
    {
        [OrgSettingKey.CurrencyPrimary]      = "COP",
        [OrgSettingKey.CurrencyReports]      = "USD",
        [OrgSettingKey.NumberFormat]         = "latam",
        [OrgSettingKey.SymbolPosition]       = "before",
        [OrgSettingKey.Timezone]             = "America/Bogota",
        [OrgSettingKey.DateFormat]           = "DD/MM/YYYY",
        [OrgSettingKey.Language]             = "es",
        [OrgSettingKey.FirstDayOfWeek]       = "monday",
        [OrgSettingKey.DefaultPaymentMethod] = "Transferencia bancaria",
        [OrgSettingKey.ProposalValidity]     = "30",
        [OrgSettingKey.ProposalRevisions]    = "3",
        [OrgSettingKey.DefaultCurrency]      = "USD",
        [OrgSettingKey.NdaClause]            = null,
        [OrgSettingKey.IpClause]             = null,
        [OrgSettingKey.PaymentPolicy]        = null,
        [OrgSettingKey.PilaDueDay]           = null,
    };
}
