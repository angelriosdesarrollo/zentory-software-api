using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.API.Controllers.V1;

// Endpoints exclusivos de desarrollo para generar tokens públicos de prueba
// (usados por el mock de correos en zentory-app: app/(public)/dev/email-preview).
// Devuelven 404 fuera de Development. Van directo a los repositorios en vez de
// pasar por MediatR/ITenantContext a propósito: no requieren sesión ni
// organización activa, y no representan un caso de uso real del producto.
[ApiController]
[Route("api/v1/public/dev")]
public sealed class DevToolsController : ControllerBase
{
    private static readonly Guid DevOrgId = new("a1000000-0000-0000-0000-000000000001");

    private readonly IWebHostEnvironment            _env;
    private readonly ICollaboratorRepository        _collaborators;
    private readonly IOrganizationRepository        _organizations;
    private readonly IPilaVerificationRepository    _pilaVerifications;
    private readonly ICollaboratorPayoutInvoiceRepository _payoutInvoices;
    private readonly IStorageService                _storage;
    private readonly IPayoutInvoicePdfGenerator      _pdf;
    private readonly IZentoryDbContext              _db;
    private readonly IUnitOfWork                    _uow;

    public DevToolsController(
        IWebHostEnvironment            env,
        ICollaboratorRepository        collaborators,
        IOrganizationRepository        organizations,
        IPilaVerificationRepository    pilaVerifications,
        ICollaboratorPayoutInvoiceRepository payoutInvoices,
        IStorageService                storage,
        IPayoutInvoicePdfGenerator     pdf,
        IZentoryDbContext              db,
        IUnitOfWork                    uow)
    {
        _env               = env;
        _collaborators     = collaborators;
        _organizations     = organizations;
        _pilaVerifications = pilaVerifications;
        _payoutInvoices    = payoutInvoices;
        _storage           = storage;
        _pdf               = pdf;
        _db                = db;
        _uow               = uow;
    }

    private async Task<Collaborator?> GetSeedCollaboratorAsync(CancellationToken ct)
    {
        var collaborators = await _collaborators.ListAsync(DevOrgId, status: "active", ct: ct);
        return collaborators.FirstOrDefault(c => c.Type != "employee");
    }

    /// <summary>POST /api/v1/public/dev/seed-pila-token</summary>
    [HttpPost("seed-pila-token")]
    public async Task<IActionResult> SeedPilaToken(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var collaborator = await GetSeedCollaboratorAsync(ct);
        if (collaborator is null)
            return NotFound(new { error = "No hay colaboradores de prueba sembrados por DevDataSeeder." });

        var period = DateTime.UtcNow.ToString("yyyy-MM");
        var verification = await _pilaVerifications.GetByCollaboratorAndPeriodAsync(collaborator.Id, period, ct);
        if (verification is null)
        {
            verification = PilaVerification.Create(DevOrgId, collaborator.Id, period);
            await _pilaVerifications.AddAsync(verification, ct);
        }
        else
        {
            verification.RegenerateToken();
            await _pilaVerifications.UpdateAsync(verification, ct);
        }
        await _uow.SaveChangesAsync(ct);

        return Ok(new { token = verification.Token, collaboratorName = collaborator.Name });
    }

    /// <summary>POST /api/v1/public/dev/seed-payout-token</summary>
    [HttpPost("seed-payout-token")]
    public async Task<IActionResult> SeedPayoutToken(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var collaborator = await GetSeedCollaboratorAsync(ct);
        if (collaborator is null)
            return NotFound(new { error = "No hay colaboradores de prueba sembrados por DevDataSeeder." });

        var period = DateTime.UtcNow.ToString("yyyy-MM");
        var invoice = CollaboratorPayoutInvoice.Create(
            DevOrgId,
            collaborator.Id,
            period,
            concept: "Cuenta de cobro (prueba)",
            amount: collaborator.MonthlyRate ?? collaborator.HourlyRate ?? 0,
            currency: collaborator.Currency,
            source: "manual_upload");

        await _payoutInvoices.AddAsync(invoice, ct);
        await _uow.SaveChangesAsync(ct);

        return Ok(new { token = invoice.PublicToken, collaboratorName = collaborator.Name });
    }

    /// <summary>
    /// POST /api/v1/public/dev/seed-payout-generated-token — a diferencia de seed-payout-token
    /// (source=manual_upload, emula "Solicitar al colaborador"), esta genera un PDF real y sube
    /// a R2 (source=generated, emula "Generar y enviar"). Devuelve un token — igual que los otros
    /// seeds — porque el flujo unificado también pasa por el portal (descargar borrador → firmar
    /// → subir), aunque el correo real además incluya un link de descarga directo.
    /// </summary>
    [HttpPost("seed-payout-generated-token")]
    public async Task<IActionResult> SeedPayoutGeneratedToken(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var collaborator = await GetSeedCollaboratorAsync(ct);
        if (collaborator is null)
            return NotFound(new { error = "No hay colaboradores de prueba sembrados por DevDataSeeder." });

        var organization = await _organizations.GetByIdAsync(DevOrgId, ct);
        var period        = DateTime.UtcNow.ToString("yyyy-MM");
        var amount        = collaborator.MonthlyRate ?? collaborator.HourlyRate ?? 0;

        var invoice = CollaboratorPayoutInvoice.Create(
            DevOrgId,
            collaborator.Id,
            period,
            concept: "Cuenta de cobro (prueba)",
            amount: amount,
            currency: collaborator.Currency,
            source: "generated");

        var pdfBytes = _pdf.Generate(new PayoutInvoicePdfModel(
            organization?.Name ?? string.Empty,
            collaborator.Name,
            collaborator.IdNumber,
            period,
            "Cuenta de cobro (prueba)",
            amount,
            collaborator.Currency,
            DateTime.UtcNow));

        var key      = $"payout-invoices/{DevOrgId}/{collaborator.Id}/{invoice.Id}.pdf";
        var fileName = $"cuenta-cobro-{period}.pdf";
        using (var stream = new MemoryStream(pdfBytes))
        {
            await _storage.UploadAsync(key, stream, "application/pdf", ct);
        }

        invoice.MarkGenerated(key, fileName, pdfBytes.LongLength, "application/pdf");

        await _payoutInvoices.AddAsync(invoice, ct);
        await _uow.SaveChangesAsync(ct);

        return Ok(new { token = invoice.PublicToken, collaboratorName = collaborator.Name });
    }

    /// <summary>
    /// POST /api/v1/public/dev/seed-magic-link-token — Camino B (autoservicio): crea un
    /// CollaboratorAccessToken de prueba y devuelve el token EN CLARO (nunca se hace en
    /// producción — ahí solo se envía por correo) para poder probar /portal/entrar de punta
    /// a punta desde el preview de correos.
    /// </summary>
    [HttpPost("seed-magic-link-token")]
    public async Task<IActionResult> SeedMagicLinkToken(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var collaborator = await GetSeedCollaboratorAsync(ct);
        if (collaborator is null || string.IsNullOrWhiteSpace(collaborator.Email))
            return NotFound(new { error = "No hay colaboradores de prueba con correo sembrados por DevDataSeeder." });

        var rawToken  = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var expiresAt = DateTime.UtcNow.AddMinutes(30);

        var accessToken = CollaboratorAccessToken.Create(collaborator.Email.ToLowerInvariant(), tokenHash, expiresAt);
        await _db.CollaboratorAccessTokens.AddAsync(accessToken, ct);
        await _uow.SaveChangesAsync(ct);

        return Ok(new { token = rawToken, collaboratorName = collaborator.Name });
    }
}
