namespace Zentory.API.Authorization;

// Marcador de metadato: la acción está abierta a cualquier miembro vigente de la organización activa (PLAN-0018 RF-87,
// SR-0018.36, D-43). No implementa IAuthorizeData ni registra policy: la membresía vigente ya la garantiza
// MembershipValidationMiddleware (RF-14); el atributo solo hace explícita la decisión de no exigir rol. Se combina con la
// policy de plan de la clase (EmpresaOnly, EmpresaStudio, …).
//
// Solo en métodos, a propósito: una acción nueva no hereda "cualquier miembro" de su clase y tiene que declarar su
// autorización. AuthorizationCatalogTests exige que cada acción del esquema de usuario declare exactamente una de
// RequireOrgOwner, RequireOrgAdmin, RequireFinancialAccess, ManageMembers o [AnyOrgMember].
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AnyOrgMemberAttribute : Attribute;
