namespace Zentory.Application.Common.Interfaces;

// Análogo a ITenantContext, pero para la sesión del portal de autoservicio de
// colaboradores (JWT del esquema "CollaboratorScheme", claims propias — no comparte
// nada con el JWT de empresa).
public interface ICollaboratorPortalContext
{
    bool                 IsAuthenticated       { get; }
    string               Email                 { get; }
    IReadOnlyList<Guid>  CollaboratorIds       { get; }  // uno por organización donde el email tiene un Collaborator activo
    Guid                 ActiveCollaboratorId  { get; }
    Guid                 ActiveOrganizationId  { get; }
    string               ActiveOrganizationName { get; }
}
