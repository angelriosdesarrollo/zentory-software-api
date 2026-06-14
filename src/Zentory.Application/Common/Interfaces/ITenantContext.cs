namespace Zentory.Application.Common.Interfaces;

public interface ITenantContext
{
    bool   IsAuthenticated { get; }
    Guid   OrganizationId  { get; }
    Guid   UserId          { get; }
    string UserInitials    { get; }
    string Plan            { get; }
    string AccountType     { get; }
}
