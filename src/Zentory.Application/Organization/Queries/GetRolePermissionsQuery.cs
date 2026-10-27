using MediatR;
using Zentory.Application.Organization.DTOs;

namespace Zentory.Application.Organization.Queries;

// GET /organization/roles/permissions (PLAN-0018 RF-45 b; D-18): matriz de solo lectura con las capacidades que aplica
// el backend. Describe las policies (RequireOrgAdmin, RequireFinancialAccess, ManageMembers, RequireOrgOwner y
// [AnyOrgMember]); no las reemplaza: cambiar una fila aquí no cambia ningún permiso.
public record GetRolePermissionsQuery : IRequest<IReadOnlyList<RolePermissionDto>>;

public sealed class GetRolePermissionsQueryHandler
    : IRequestHandler<GetRolePermissionsQuery, IReadOnlyList<RolePermissionDto>>
{
    private static readonly RoleGrantsDto OwnerAndAdmin = new(Owner: true, Admin: true,  Member: false);
    private static readonly RoleGrantsDto OwnerOnly     = new(Owner: true, Admin: false, Member: false);
    private static readonly RoleGrantsDto Everyone      = new(Owner: true, Admin: true,  Member: true);

    private static readonly IReadOnlyList<RolePermissionDto> Matrix =
    [
        new(RolePermissionKeys.SettingsManage,             "Cambiar la configuración",               OwnerAndAdmin),
        new(RolePermissionKeys.OrganizationFiscalProfile,  "Editar el perfil fiscal",                OwnerAndAdmin),
        new(RolePermissionKeys.MembersManage,              "Gestionar miembros y roles",             OwnerAndAdmin),
        new(RolePermissionKeys.FinancesView,               "Ver finanzas y reportes",                OwnerAndAdmin),
        new(RolePermissionKeys.BillingPlan,                "Cambiar el plan y la suscripción",       OwnerOnly),
        new(RolePermissionKeys.OrganizationTransfer,       "Transferir la propiedad",                OwnerOnly),
        new(RolePermissionKeys.OrganizationDelete,         "Eliminar la organización",               OwnerOnly),
        new(RolePermissionKeys.OperationsAccess,           "Operar proyectos, clientes y facturas",  Everyone),
    ];

    public Task<IReadOnlyList<RolePermissionDto>> Handle(GetRolePermissionsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(Matrix);
}

// Claves de la matriz de RF-45 b. Son el contrato con el frontend (lib/api/roles.ts).
public static class RolePermissionKeys
{
    public const string SettingsManage            = "settings.manage";
    public const string OrganizationFiscalProfile = "organization.fiscal_profile";
    public const string MembersManage             = "members.manage";
    public const string FinancesView              = "finances.view";
    public const string BillingPlan               = "billing.plan";
    public const string OrganizationTransfer      = "organization.transfer";
    public const string OrganizationDelete        = "organization.delete";
    public const string OperationsAccess          = "operations.access";
}
