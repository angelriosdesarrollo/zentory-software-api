namespace Zentory.Application.Common.Security;

// Acciones irreversibles que exigen reautenticación (PLAN-0018 RF-81; ADR reautenticacion-reciente §1). El nonce de
// Google se liga a una de ellas: el emitido para una acción no sirve para otra. Una acción irreversible nueva suma aquí
// su constante y llama a IReauthenticationVerifier en su comando.
public static class ReauthAction
{
    public const string OrganizationDelete         = "organization.delete";
    public const string OrganizationTransfer       = "organization.transfer";
    public const string OrganizationTransferAccept = "organization.transfer.accept";

    public static IReadOnlyList<string> All { get; } =
        [OrganizationDelete, OrganizationTransfer, OrganizationTransferAccept];

    // Comparación ordinal y exacta: "Organization.Delete" no es una acción.
    public static bool IsKnown(string? action) => action is not null && All.Contains(action, StringComparer.Ordinal);
}
