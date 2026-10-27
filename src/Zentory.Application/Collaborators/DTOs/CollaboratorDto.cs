using System.Text.Json.Serialization;

namespace Zentory.Application.Collaborators.DTOs;

// Tarifas con la convención de D-29 (PLAN-0018 RF-86 d; D-41): para member los handlers de lectura las dejan en null y
// [JsonIgnore(WhenWritingNull)] las saca del JSON (se omiten, no se envían en cero ni en null). Currency no es un monto y
// se conserva. Para owner y admin una tarifa sin valor también se omite; el frontend ya las trata como opcionales.

public record CollaboratorSummaryDto(
    Guid     Id,
    string   Name,
    string   Type,
    string   Status,
    string?  Role,
    string?  Email,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? HourlyRate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? MonthlyRate,
    string   Currency,
    string   PilaStatus);

public record CollaboratorDto(
    Guid     Id,
    string   Name,
    string   Type,
    string   Status,
    string?  Role,
    string?  Email,
    string?  Phone,
    string?  IdNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? HourlyRate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? MonthlyRate,
    string   Currency,
    string   PilaStatus,
    string?  PilaLastVerifiedPeriod,
    short?   ArlRiskLevel,
    Guid?    UserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
