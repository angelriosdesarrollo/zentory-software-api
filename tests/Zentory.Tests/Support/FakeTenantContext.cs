using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;

namespace Zentory.Tests.Support;

// Doble del usuario autenticado del ERP: OrganizationId es la organización activa del token. HasActiveOrganization
// sigue a OrganizationId (un Guid no vacío) salvo que el test lo fije, como un principal sin active_org_id válido.
public sealed class FakeTenantContext : ITenantContext
{
    private readonly bool? _hasActiveOrganization;

    public bool   IsAuthenticated { get; init; } = true;
    public Guid   OrganizationId  { get; init; }
    public Guid   UserId          { get; init; } = Guid.NewGuid();
    public Guid   SessionId       { get; init; } = Guid.NewGuid();
    public string UserInitials    { get; init; } = "TT";
    public string Plan            { get; init; } = Zentory.Domain.Constants.Plan.Studio;
    public string LegalType       { get; init; } = Zentory.Domain.Constants.LegalType.Empresa;
    public string ActiveOrgRole   { get; init; } = OrgRole.Owner;
    public bool   IsOwner         { get; init; } = true;

    public bool HasActiveOrganization
    {
        get => _hasActiveOrganization ?? (IsAuthenticated && OrganizationId != Guid.Empty);
        init => _hasActiveOrganization = value;
    }

    public static FakeTenantContext For(Guid organizationId) => new() { OrganizationId = organizationId };
}
