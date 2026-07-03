using Zentory.Domain.Entities;

namespace Zentory.Application.PilaVerifications;

// Decreto 1703/2002: un independiente solo está obligado a PILA si sus ingresos
// mensuales superan 1 SMLV. Para 'fixed_contractor' el monto mensual es conocido
// de antemano (MonthlyRate), así que se puede filtrar con certeza. Para
// 'hourly_contractor' el ingreso real depende de las horas efectivamente
// trabajadas en el período (TimeEntry), que no se puede determinar con certeza
// por adelantado — se trata siempre como elegible para no arriesgar un falso
// negativo de cumplimiento; la empresa puede omitir manualmente si en la
// práctica no llegó al umbral.
//
// Compartida entre GetPilaComplianceQueryHandler (vista manual) y
// RunMonthlyPilaAutoRequestCommandHandler (job automático) para que ambos
// caminos apliquen exactamente el mismo criterio de elegibilidad.
public static class PilaEligibilityRules
{
    public static bool IsEligible(Collaborator collaborator, decimal? smlv)
    {
        if (collaborator.Type != "fixed_contractor") return true;
        if (smlv is null || smlv <= 0) return true;
        return (collaborator.MonthlyRate ?? 0) >= smlv;
    }
}
