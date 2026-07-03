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
    DateTime  IssuedAt,
    // Branding — todos opcionales, se leen del perfil de organización (OrganizationSettings
    // profile.*) ya existente; si no están diligenciados, esa sección del PDF se omite.
    byte[]?   LogoBytes    = null,
    string?   LegalName    = null,
    string?   Nit          = null,
    string?   Address      = null,
    string?   City         = null,
    string?   Email        = null,
    string?   Phone        = null,
    // Firma electrónica — presentes solo cuando el colaborador ya firmó en el portal.
    string?   SignedByName = null,
    DateTime? SignedAt     = null);
