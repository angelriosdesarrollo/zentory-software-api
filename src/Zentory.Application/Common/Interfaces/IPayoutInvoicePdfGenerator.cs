namespace Zentory.Application.Common.Interfaces;

public interface IPayoutInvoicePdfGenerator
{
    byte[] Generate(PayoutInvoicePdfModel model);
}

public record PayoutInvoicePdfModel(
    string    CompanyName,
    string    CollaboratorName,
    string?   CollaboratorIdNumber,
    string    Period,
    string    Concept,
    decimal   Amount,
    string    Currency,
    DateTime  IssuedAt);
