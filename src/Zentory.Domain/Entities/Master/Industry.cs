namespace Zentory.Domain.Entities.Master;

public class Industry
{
    public short  Id   { get; set; }   // SMALLINT PK GENERATED
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
}
