namespace Zentory.Domain.Constants;

// Roles de una membresía de organización (PLAN-0018 RF-45; D-18). Única fuente de los valores: src/ y los tests no los
// escriben como literal (DomainLiteralPolicyTests). El catálogo es fijo: no hay roles personalizados.
public static class OrgRole
{
    public const string Owner  = "owner";
    public const string Admin  = "admin";
    public const string Member = "member";

    // Los tres roles, en el orden en que los devuelve GET /organization/roles.
    public static readonly IReadOnlyList<string> All = [Owner, Admin, Member];

    // Roles que se pueden asignar al invitar o cambiar un rol. Owner solo se obtiene creando la organización o por
    // transferencia de propiedad.
    public static readonly IReadOnlyList<string> Assignable = [Admin, Member];
}
