using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;

namespace Zentory.Tests.Support;

// Doble del usuario autenticado del ERP: OrganizationId es la organización activa del token.
public sealed class FakeTenantContext : ITenantContext
{
    public bool   IsAuthenticated { get; init; } = true;
    public Guid   OrganizationId  { get; init; }
    public Guid   UserId          { get; init; } = Guid.NewGuid();
    public string UserInitials    { get; init; } = "TT";
    public string Plan            { get; init; } = Zentory.Domain.Constants.Plan.Studio;
    public string LegalType       { get; init; } = Zentory.Domain.Constants.LegalType.Empresa;
    public string ActiveOrgRole   { get; init; } = "owner";
    public bool   IsOwner         { get; init; } = true;

    public static FakeTenantContext For(Guid organizationId) => new() { OrganizationId = organizationId };
}
