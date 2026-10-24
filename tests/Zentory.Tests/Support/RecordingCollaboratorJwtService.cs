using Zentory.Application.Common.Interfaces;

namespace Zentory.Tests.Support;

// Doble de ICollaboratorJwtService que cuenta las emisiones de sesión, para probar que un
// switch rechazado no llega a generar un JWT.
public sealed class RecordingCollaboratorJwtService : ICollaboratorJwtService
{
    public int GenerateSessionTokenCalls { get; private set; }

    public string GenerateSessionToken(
        string              email,
        IReadOnlyList<Guid> collaboratorIds,
        Guid                activeCollaboratorId,
        Guid                activeOrganizationId,
        string              activeOrganizationName,
        string              authMethod)
    {
        GenerateSessionTokenCalls++;
        return $"recorded-token-{GenerateSessionTokenCalls}";
    }
}
