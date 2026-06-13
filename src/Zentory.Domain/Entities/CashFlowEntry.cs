using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class CashFlowEntry : TenantEntity
{
    public Guid?    ProjectId       { get; private set; }
    public Guid?    InvoiceId       { get; private set; }
    public short?   CategoryId      { get; private set; }

    public string   Type            { get; private set; } = default!;  // 'income' | 'expense'
    public string   Description     { get; private set; } = default!;

    public decimal  Amount          { get; private set; }
    public string   Currency        { get; private set; } = default!;
    public decimal  ExchangeRate    { get; private set; } = 1m;
    public decimal  AmountBase      { get; private set; }  // en moneda base del tenant

    public DateOnly TransactionDate { get; private set; }
    public bool     IsRecurring     { get; private set; }
    public string?  RecurrenceRule  { get; private set; }  // JSONB as string

    public Guid?    CreatedBy       { get; private set; }
    public DateTime? DeletedAt      { get; private set; }

    private CashFlowEntry() { }

    public static CashFlowEntry CreateFromInvoice(
        Guid    organizationId,
        Guid    invoiceId,
        string  description,
        decimal amount,
        string  currency,
        decimal exchangeRate,
        decimal amountBase,
        DateOnly transactionDate,
        Guid?   createdBy = null)
    {
        return new CashFlowEntry
        {
            OrganizationId  = organizationId,
            InvoiceId       = invoiceId,
            Type            = "income",
            Description     = description,
            Amount          = amount,
            Currency        = currency,
            ExchangeRate    = exchangeRate,
            AmountBase      = amountBase,
            TransactionDate = transactionDate,
            CreatedBy       = createdBy
        };
    }

    public static CashFlowEntry CreateManual(
        Guid     organizationId,
        string   type,
        string   description,
        decimal  amount,
        string   currency,
        decimal  exchangeRate,
        decimal  amountBase,
        DateOnly transactionDate,
        short?   categoryId    = null,
        Guid?    projectId     = null,
        bool     isRecurring   = false,
        string?  recurrenceRule= null,
        Guid?    createdBy     = null)
    {
        return new CashFlowEntry
        {
            OrganizationId  = organizationId,
            Type            = type,
            Description     = description,
            Amount          = amount,
            Currency        = currency,
            ExchangeRate    = exchangeRate,
            AmountBase      = amountBase,
            TransactionDate = transactionDate,
            CategoryId      = categoryId,
            ProjectId       = projectId,
            IsRecurring     = isRecurring,
            RecurrenceRule  = recurrenceRule,
            CreatedBy       = createdBy
        };
    }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}
