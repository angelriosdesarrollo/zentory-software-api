namespace Zentory.Application.Common.Interfaces;

// JWT de sesión del portal de autoservicio de colaboradores — separado de IJwtService
// (que emite tokens para User/Organization de empresa) porque las claims no tienen nada
// en común: un colaborador no tiene plan, ni rol de organización, ni es dueño de nada.
public interface ICollaboratorJwtService
{
    string GenerateSessionToken(
        string              email,
        IReadOnlyList<Guid> collaboratorIds,
        Guid                activeCollaboratorId,
        Guid                activeOrganizationId,
        string              activeOrganizationName);
}
