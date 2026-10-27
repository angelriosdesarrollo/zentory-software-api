using System.Text.Json;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;

namespace Zentory.Application.Organization.Commands;

// Auditoría de la configuración de la organización (PLAN-0018 RF-89, SR-0018.45, D-46; REV-0005 H-7): los comandos que
// escriben OrganizationSettings (PATCH /organization/settings y PATCH /organization/profile) comparten esta regla, así
// que una clave auditada deja el mismo ActivityLog sea cual sea la ruta que la cambia.
internal static class OrganizationSettingsAudit
{
    // EntityType de las filas de ActivityLog; EntityId es la organización.
    public const string EntityType = "OrganizationSettings";

    // Identidad fiscal y datos de pago (OrgSettingKey.Audited) cuyo valor cambia. Vacío y ausente cuentan igual (null).
    public static bool IsAuditedChange(string key, string? previous, string? current)
        => OrgSettingKey.Audited.Contains(key) && !string.Equals(previous, current, StringComparison.Ordinal);

    // Queda el valor anterior y el nuevo. Va en la misma unidad de trabajo que el cambio, así que no hay cambio sin rastro
    // ni rastro sin cambio. El valor solo viaja en metadata hacia la base: nunca a un log de aplicación.
    public static Task LogChangeAsync(
        IActivityLogService activityLog, Guid organizationId, string key, string? previous, string? current, CancellationToken ct)
        => activityLog.LogAsync(
            entityType: EntityType,
            entityId:   organizationId,
            action:     $"Cambió la configuración [{key}]",
            metadata:   JsonSerializer.Serialize(new { key, previous, current }),
            ct:         ct);
}
