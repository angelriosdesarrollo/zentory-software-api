namespace Zentory.Application.Common.Interfaces;

public interface ITenantContext
{
    bool   IsAuthenticated { get; }
    Guid   OrganizationId  { get; }  // active org (reads active_org_id claim)
    Guid   UserId          { get; }
    string UserInitials    { get; }
    string Plan            { get; }
    string LegalType     { get; }
    string ActiveOrgRole   { get; }  // "owner" | "admin" | "member"
    bool   IsOwner         { get; }
}
