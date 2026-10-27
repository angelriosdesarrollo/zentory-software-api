namespace Zentory.API.Authorization;

// Metadato de endpoint que excluye una acción de MembershipValidationMiddleware (PLAN-0018 RF-14, SR-0018.7, D-7). Se
// evalúa sobre context.GetEndpoint(), después del routing, nunca por prefijo de ruta. La lista de acciones que lo llevan
// es cerrada y la fija SkipMembershipCatalogTests: agregarlo a una acción nueva exige actualizar ese test.
//
// Una acción excluida no tiene IMembershipContext poblado: no puede autorizar por rol y debe validar por sí misma lo que
// necesite (por ejemplo, switch la membresía de destino).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SkipMembershipValidationAttribute : Attribute;
