namespace Zentory.Domain.Constants;

// Claves conocidas de OrganizationSettings: única fuente del contrato entre la query, los comandos y la lista blanca
// de PATCH /organization/settings (PLAN-0018 RF-89, D-46).
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
    public const string PilaDueDay       = "pila.due_day";  // día del mes (1-31), autodeclarado por el freelance

    // Perfil de la organización (UpdateOrganizationProfileCommand / GetOrganizationProfileQuery).
    public const string ProfileLegalName   = "profile.legal_name";
    public const string ProfileLogoUrl     = "profile.logo_url";
    public const string ProfileNit         = "profile.nit";
    public const string ProfileCompanyType = "profile.company_type";
    public const string ProfileWorkType    = "profile.work_type";
    public const string ProfileLegalRep    = "profile.legal_rep";
    public const string ProfileLegalRepId  = "profile.legal_rep_id";
    public const string ProfileTaxRegime   = "profile.tax_regime";
    public const string ProfileCiiu        = "profile.ciiu";
    public const string ProfileEmail       = "profile.email";
    public const string ProfilePhone       = "profile.phone";
    public const string ProfileAddress     = "profile.address";
    public const string ProfileCity        = "profile.city";

    // Pestaña "Cuenta bancaria" de zentory-app (app/(app)/settings/page.tsx). Son las "cuentas bancarias de la
    // organización" de RF-89: ningún endpoint escribe OrganizationBankAccount (C-15).
    public const string BankName        = "bank.name";
    public const string BankType        = "bank.type";
    public const string BankNumber      = "bank.number";
    public const string BankHolder      = "bank.holder";
    public const string BankDocument    = "bank.document";
    public const string BankCurrency    = "bank.currency";
    public const string BankUsdPlatform = "bank.usd_platform";
    public const string BankUsdAccount  = "bank.usd_account";

    public static readonly IReadOnlyList<string> Profile =
    [
        ProfileLegalName, ProfileLogoUrl, ProfileNit, ProfileCompanyType, ProfileWorkType, ProfileLegalRep,
        ProfileLegalRepId, ProfileTaxRegime, ProfileCiiu, ProfileEmail, ProfilePhone, ProfileAddress, ProfileCity,
    ];

    public static readonly IReadOnlyList<string> Bank =
    [
        BankName, BankType, BankNumber, BankHolder, BankDocument, BankCurrency, BankUsdPlatform, BankUsdAccount,
    ];

    // Claves cuyo cambio queda en ActivityLog con el valor anterior y el nuevo (D-46): identidad fiscal y datos de pago.
    public static readonly IReadOnlyList<string> Audited = [ProfileNit, ProfileLegalName, .. Bank];
}
