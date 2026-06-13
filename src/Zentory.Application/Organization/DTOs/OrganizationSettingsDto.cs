namespace Zentory.Application.Organization.DTOs;

public record OrganizationSettingsDto(Dictionary<string, string?> Settings);

// Well-known setting keys — shared between query and command so both sides agree on the contract
public static class OrgSettingKey
{
    public const string CurrencyPrimary   = "currency.primary";
    public const string CurrencyReports   = "currency.reports";
    public const string NumberFormat      = "number.format";
    public const string SymbolPosition    = "symbol.position";
    public const string Timezone         = "timezone";
    public const string DateFormat       = "date.format";
    public const string Language         = "language";
    public const string FirstDayOfWeek   = "week.first_day";
    public const string DefaultPaymentMethod = "invoice.payment_method";
    public const string ProposalValidity  = "proposal.validity_days";
    public const string ProposalRevisions = "proposal.revision_count";
    public const string DefaultCurrency   = "invoice.currency";
    public const string NdaClause        = "contract.nda";
    public const string IpClause         = "contract.ip";
    public const string PaymentPolicy    = "contract.payment_policy";
}
