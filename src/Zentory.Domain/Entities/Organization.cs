namespace Zentory.Domain.Entities;

public class Organization
{
    public Guid   OrganizationId { get; private set; } = Guid.NewGuid();
    public string Name           { get; private set; } = default!;
    public string Plan           { get; private set; } = Zentory.Domain.Constants.Plan.Free;
    public string AccountType    { get; private set; } = default!;
    public string Country        { get; private set; } = "CO";
    public bool   IsActive       { get; private set; } = true;

    private Organization() { }

    public static Organization Create(string name, string accountType, string country = "CO", Guid? id = null)
    {
        return new Organization
        {
            OrganizationId = id ?? Guid.NewGuid(),
            Name           = name,
            AccountType    = accountType,
            Country        = country
        };
    }

    public void SetPlan(string plan) { Plan = plan; }
}
