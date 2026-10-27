namespace Zentory.Application.Organization.DTOs;

// Rol del catálogo fijo (PLAN-0018 RF-45 a). Los colores son opcionales: el frontend pone los suyos si vienen nulos.
public record OrgRoleDto(
    string  Id,
    string  Name,
    bool    IsDefault,
    string? BadgeBg,
    string? BadgeColor);

// Fila de la matriz de solo lectura de RF-45 b: la capacidad y qué rol la tiene.
public record RolePermissionDto(
    string         Key,
    string         Label,
    RoleGrantsDto  Grants);

public record RoleGrantsDto(
    bool Owner,
    bool Admin,
    bool Member);
