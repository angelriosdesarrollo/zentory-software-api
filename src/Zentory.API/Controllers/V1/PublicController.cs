using MediatR;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Master.Queries;
using Zentory.Application.Plans.Queries;

namespace Zentory.API.Controllers.V1;

/// <summary>
/// Endpoints públicos sin autenticación: planes de pricing, tasas de seguridad social,
/// categorías de gastos. Los datos provienen de las tablas master del sistema.
/// </summary>
[ApiController]
[Route("api/v1/public")]
public sealed class PublicController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/public/plans — planes de precios con features, límites y marketing copy</summary>
    [HttpGet("plans")]
    [ResponseCache(Duration = 300)] // 5 min — los precios no cambian con frecuencia
    public async Task<IActionResult> GetPlans(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPlansQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/public/ss-rules — tasas de seguridad social por país y año</summary>
    [HttpGet("ss-rules")]
    [ResponseCache(Duration = 86400)] // 24 h — las tasas cambian máximo una vez al año
    public async Task<IActionResult> GetSsRules(
        [FromQuery] string countryCode = "CO",
        [FromQuery] short? year        = null,
        CancellationToken  ct          = default)
    {
        var result = await _mediator.Send(new GetSsRulesQuery(countryCode, year), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/public/expense-categories — categorías de gastos e ingresos</summary>
    [HttpGet("expense-categories")]
    [ResponseCache(Duration = 3600)] // 1 h
    public async Task<IActionResult> GetExpenseCategories(
        [FromQuery] string? type = null,
        CancellationToken   ct   = default)
    {
        var result = await _mediator.Send(new GetExpenseCategoriesQuery(type), ct);
        return Ok(result);
    }

    // ── Master data — static reference lists ──────────────────────────────────

    /// <summary>GET /api/v1/public/master/currencies</summary>
    [HttpGet("master/currencies")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetCurrencies() => Ok(new[]
    {
        new { value = "COP", label = "COP — Peso colombiano" },
        new { value = "USD", label = "USD — Dólar estadounidense" },
        new { value = "EUR", label = "EUR — Euro" },
        new { value = "MXN", label = "MXN — Peso mexicano" },
    });

    /// <summary>GET /api/v1/public/master/account-currencies</summary>
    [HttpGet("master/account-currencies")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetAccountCurrencies() => Ok(new[]
    {
        new { value = "COP", label = "COP — Peso colombiano" },
        new { value = "USD", label = "USD — Dólar estadounidense" },
        new { value = "EUR", label = "EUR — Euro" },
    });

    /// <summary>GET /api/v1/public/master/payment-methods</summary>
    [HttpGet("master/payment-methods")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetPaymentMethods([FromQuery] string countryCode = "CO") => Ok(new[]
    {
        new { value = "Transferencia bancaria", label = "Transferencia bancaria" },
        new { value = "PSE",                    label = "PSE" },
        new { value = "Cheque",                 label = "Cheque" },
        new { value = "Efectivo",               label = "Efectivo" },
    });

    /// <summary>GET /api/v1/public/master/proposal-validity-options</summary>
    [HttpGet("master/proposal-validity-options")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetProposalValidityOptions() => Ok(new[]
    {
        new { value = "15 días", label = "15 días" },
        new { value = "30 días", label = "30 días" },
        new { value = "45 días", label = "45 días" },
        new { value = "60 días", label = "60 días" },
    });

    /// <summary>GET /api/v1/public/master/ip-options</summary>
    [HttpGet("master/ip-options")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetIpOptions() => Ok(new[]
    {
        new { value = "Transferida al cliente al 100% tras pago total",    label = "Transferida al cliente al 100% tras pago total" },
        new { value = "Licencia perpetua no exclusiva al cliente",         label = "Licencia perpetua no exclusiva al cliente" },
        new { value = "Compartida — código base propiedad del contratista", label = "Compartida — código base propiedad del contratista" },
        new { value = "Negociar por proyecto",                             label = "Negociar por proyecto" },
    });

    /// <summary>GET /api/v1/public/master/banks</summary>
    [HttpGet("master/banks")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetBanks([FromQuery] string countryCode = "CO") => Ok(new[]
    {
        new { value = "Bancolombia",     label = "Bancolombia" },
        new { value = "Davivienda",      label = "Davivienda" },
        new { value = "Banco de Bogotá", label = "Banco de Bogotá" },
        new { value = "BBVA Colombia",   label = "BBVA Colombia" },
        new { value = "Banco Popular",   label = "Banco Popular" },
        new { value = "Nequi",           label = "Nequi" },
        new { value = "Daviplata",       label = "Daviplata" },
        new { value = "Otro",            label = "Otro" },
    });

    /// <summary>GET /api/v1/public/master/countries</summary>
    [HttpGet("master/countries")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetCountries() => Ok(new[]
    {
        new { value = "Colombia",  label = "Colombia" },
        new { value = "México",    label = "México" },
        new { value = "Argentina", label = "Argentina" },
        new { value = "Chile",     label = "Chile" },
        new { value = "Ecuador",   label = "Ecuador" },
        new { value = "Perú",      label = "Perú" },
    });

    /// <summary>GET /api/v1/public/master/company-types</summary>
    [HttpGet("master/company-types")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetCompanyTypes([FromQuery] string countryCode = "CO") => Ok(new[]
    {
        new { value = "SAS",  label = "SAS" },
        new { value = "LTDA", label = "LTDA" },
        new { value = "S.A.", label = "S.A." },
        new { value = "E.U.", label = "E.U." },
        new { value = "Otro", label = "Otro" },
    });

    /// <summary>GET /api/v1/public/master/tax-regimes</summary>
    [HttpGet("master/tax-regimes")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetTaxRegimes(
        [FromQuery] string countryCode  = "CO",
        [FromQuery] string? accountType = null)
    {
        var isFreelance = accountType?.Equals("freelance", StringComparison.OrdinalIgnoreCase) == true;
        if (isFreelance)
            return Ok(new[]
            {
                new { value = "No responsable de IVA", label = "No responsable de IVA" },
                new { value = "Responsable de IVA",    label = "Responsable de IVA" },
            });

        return Ok(new[]
        {
            new { value = "Responsable de IVA",    label = "Responsable de IVA" },
            new { value = "No responsable de IVA", label = "No responsable de IVA" },
            new { value = "Gran contribuyente",     label = "Gran contribuyente" },
        });
    }

    /// <summary>GET /api/v1/public/master/usd-platforms</summary>
    [HttpGet("master/usd-platforms")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetUsdPlatforms() => Ok(new[]
    {
        new { value = "",                     label = "Sin cuenta USD" },
        new { value = "Wise (transferwise)",  label = "Wise (transferwise)" },
        new { value = "PayPal",               label = "PayPal" },
        new { value = "Payoneer",             label = "Payoneer" },
        new { value = "Otro banco",           label = "Otro banco" },
    });

    /// <summary>GET /api/v1/public/master/timezones</summary>
    [HttpGet("master/timezones")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetTimezones() => Ok(new[]
    {
        new { value = "America/Bogota",       label = "América/Bogotá (UTC−5)" },
        new { value = "America/Mexico_City",  label = "América/Ciudad de México (UTC−6)" },
        new { value = "America/Buenos_Aires", label = "América/Buenos Aires (UTC−3)" },
        new { value = "America/Santiago",     label = "América/Santiago (UTC−4/−3)" },
        new { value = "America/Lima",         label = "América/Lima (UTC−5)" },
        new { value = "America/Caracas",      label = "América/Caracas (UTC−4)" },
    });

    /// <summary>GET /api/v1/public/master/date-formats</summary>
    [HttpGet("master/date-formats")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetDateFormats() => Ok(new[]
    {
        new { value = "DD/MM/YYYY", label = "DD/MM/YYYY" },
        new { value = "MM/DD/YYYY", label = "MM/DD/YYYY" },
        new { value = "YYYY-MM-DD", label = "YYYY-MM-DD" },
    });

    /// <summary>GET /api/v1/public/master/number-formats</summary>
    [HttpGet("master/number-formats")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetNumberFormats() => Ok(new[]
    {
        new { value = "latam", label = "1.234.567,89 (punto miles, coma decimal)" },
        new { value = "en",    label = "1,234,567.89 (coma miles, punto decimal)" },
    });

    /// <summary>GET /api/v1/public/master/languages</summary>
    [HttpGet("master/languages")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetLanguages() => Ok(new[]
    {
        new { value = "es", label = "Español" },
        new { value = "en", label = "English" },
    });

    /// <summary>GET /api/v1/public/master/week-first-days</summary>
    [HttpGet("master/week-first-days")]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetWeekFirstDays() => Ok(new[]
    {
        new { value = "monday", label = "Lunes" },
        new { value = "sunday", label = "Domingo" },
    });
}
