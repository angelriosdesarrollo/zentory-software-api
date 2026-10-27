namespace Zentory.Application.Common.Options;

// Organizations:* (PLAN-0018 RF-65 f, RF-72, RF-78). PurgeAfterDays es el plazo de restauración: la fecha límite que
// recibe el owner al eliminar (DeletedAt + PurgeAfterDays), el listado de eliminadas y la purga física usan el mismo
// valor. AddInfrastructure lo enlaza y lo valida al arrancar: un plazo menor que 1 día purgaría en la siguiente corrida.
public sealed class OrganizationsOptions
{
    public const string SectionName           = "Organizations";
    public const string PurgeAfterDaysKey     = SectionName + ":" + nameof(PurgeAfterDays);
    public const int    DefaultPurgeAfterDays = 30;
    public const int    MinimumPurgeAfterDays = 1;

    public const string InvalidPurgeAfterDaysMessage = PurgeAfterDaysKey + " debe ser un entero mayor o igual que 1.";

    public int PurgeAfterDays { get; set; } = DefaultPurgeAfterDays;

    public bool IsValid => PurgeAfterDays >= MinimumPurgeAfterDays;

    // Fecha límite para restaurar una organización eliminada en deletedAtUtc.
    public DateTime RestoreDeadline(DateTime deletedAtUtc) => deletedAtUtc.AddDays(PurgeAfterDays);
}
