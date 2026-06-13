namespace Zentory.Domain.Entities.Master;

public class SsRule
{
    public Guid    Id              { get; set; } = Guid.NewGuid();
    public string  CountryCode     { get; set; } = default!;
    public short   EffectiveYear   { get; set; }
    public string  FundType        { get; set; } = default!;  // 'salud'|'pension'|'arl'|'caja_compensacion'
    public string  ContributorType { get; set; } = default!;  // 'independiente'|'empleado'|'empleador'
    public decimal EmployeePct     { get; set; }
    public decimal EmployerPct     { get; set; }
    public decimal TotalPct        { get; set; }
    public decimal MinBaseSmlv     { get; set; } = 1.00m;
    public decimal? MaxBaseSmlv    { get; set; }
    public decimal SmlvCop         { get; set; }
    public short?  ArlLevel        { get; set; }
    public bool    Active          { get; set; } = true;
    public string? Notes           { get; set; }
}
