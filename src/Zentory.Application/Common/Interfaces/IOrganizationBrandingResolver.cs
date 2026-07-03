namespace Zentory.Application.Common.Interfaces;

// Lee el perfil de organización ya existente (OrganizationSettings, claves profile.*, el
// mismo dato que ya usa GetOrganizationProfileQuery) para poder "brandear" documentos
// generados por el sistema (hoy: cuentas de cobro) sin necesitar una tabla ni pantalla de
// configuración nueva. Si un campo no está diligenciado, simplemente viene null — eso es
// el "template por defecto".
public interface IOrganizationBrandingResolver
{
    Task<OrganizationBranding> ResolveAsync(Guid organizationId, CancellationToken ct = default);
}

public record OrganizationBranding(
    byte[]?   LogoBytes,
    string?   LegalName,
    string?   Nit,
    string?   Address,
    string?   City,
    string?   Email,
    string?   Phone);
