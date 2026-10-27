namespace Zentory.Application.Common.Security;

// Member sin finanzas (PLAN-0018 RF-66 e; D-29; R-E1): si la petición en curso puede ver montos. Sale de la membresía de
// la petición (IMembershipContext), nunca del claim: owner y admin ven montos; member no. Sin membresía resuelta (acción
// excluida, sesión de colaborador o fuera de una petición) no ve montos.
//
// Convención para los DTO con montos que también ve un member ("se omiten, no se envían en cero"):
//   - la propiedad monetaria es nullable y lleva [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)];
//   - el handler la deja en null para member (Amount(...) lo hace en una línea).
// No se cambia el DefaultIgnoreCondition global: hoy serializa los null y otros contratos dependen de ello.
public interface IFinancialVisibility
{
    bool CanSeeAmounts { get; }

    // El monto si la petición puede verlo; null si no, para que la propiedad desaparezca del JSON.
    decimal? Amount(decimal? value);
}
