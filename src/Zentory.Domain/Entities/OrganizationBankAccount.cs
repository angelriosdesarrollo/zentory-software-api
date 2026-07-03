using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class OrganizationBankAccount : BaseEntity
{
    public Guid     OrganizationId { get; private set; }
    public string   BankName       { get; private set; } = default!;
    public string   LegalType    { get; private set; } = default!;
    // 'corriente' | 'ahorros' | 'nequi' | 'daviplata'
    public string   AccountNumber  { get; private set; } = default!;
    public string   AccountHolder  { get; private set; } = default!;
    public string   Currency       { get; private set; } = "COP";
    public bool     IsDefault      { get; private set; }
    public DateTime? DeletedAt     { get; private set; }

    private OrganizationBankAccount() { }

    public static OrganizationBankAccount Create(
        Guid   organizationId,
        string bankName,
        string legalType,
        string accountNumber,
        string accountHolder,
        string currency   = "COP",
        bool   isDefault  = false)
    {
        return new OrganizationBankAccount
        {
            OrganizationId = organizationId,
            BankName       = bankName,
            LegalType    = legalType,
            AccountNumber  = accountNumber,
            AccountHolder  = accountHolder,
            Currency       = currency,
            IsDefault      = isDefault
        };
    }

    public void SetDefault(bool value) { IsDefault = value; UpdatedAt = DateTime.UtcNow; }

    public void Update(string bankName, string legalType, string accountNumber, string accountHolder, string currency)
    {
        BankName       = bankName;
        LegalType    = legalType;
        AccountNumber  = accountNumber;
        AccountHolder  = accountHolder;
        Currency       = currency;
        UpdatedAt      = DateTime.UtcNow;
    }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}
