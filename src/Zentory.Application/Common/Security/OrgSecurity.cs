namespace Zentory.Application.Common.Security;

// Constantes de seguridad de la organización decididas por el humano (PLAN-0018 architecture.md §2.4): no son
// configuración.
public static class OrgSecurity
{
    // Vigencia del nonce de reautenticación y antigüedad máxima del id_token de Google (RF-81 c, RF-84 a).
    public static readonly TimeSpan ReauthenticationWindow = TimeSpan.FromMinutes(5);

    // Tolerancia de reloj hacia el futuro para el iat del id_token (SR-0018.30 b).
    public static readonly TimeSpan ReauthenticationFutureSkew = TimeSpan.FromSeconds(30);

    // Vigencia de una propuesta de transferencia de la propiedad (RF-64 c; D-33): vencida, se trata como inexistente.
    public static readonly TimeSpan OwnershipTransferLifetime = TimeSpan.FromDays(7);
}
