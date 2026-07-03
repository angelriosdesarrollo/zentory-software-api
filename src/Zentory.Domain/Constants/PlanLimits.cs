namespace Zentory.Domain.Constants;

public static class PlanLimits
{
    /// <summary>
    /// Feature key strings used as identifiers in the PlanLimits table.
    /// Must match the values seeded in <c>DevDataSeeder.SeedBillingPlansAsync</c>.
    /// </summary>
    public static class FeatureKeys
    {
        public const string MaxClients        = "max_clients";
        public const string MaxInvoicesMonth  = "max_invoices_month";
        public const string MaxOrgMembers     = "max_org_members";
        public const string MaxProjects       = "max_projects";
        public const string MaxCollaborators  = "max_collaborators";
        public const string MaxOwnedOrgs      = "max_owned_orgs";
    }
}
