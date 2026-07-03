namespace Zentory.Application.Common;

// UGPP exige conservar evidencia de cumplimiento (comprobantes PILA, cuentas de
// cobro) por un mínimo de 5 años. Esto es solo informativo — expone la fecha
// límite para que la empresa la tenga presente, pero no implementa borrado
// automático: eliminar evidencia legal vía un job sin revisión humana es un
// riesgo mayor que el de conservarla de más.
public static class DocumentRetentionRules
{
    public const int RetentionYears = 5;

    public static DateTime? RetentionUntil(DateTime? documentDate)
        => documentDate?.AddYears(RetentionYears);
}
