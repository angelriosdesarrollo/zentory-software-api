using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Common;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.API.Controllers.V1;

// Separado de DevToolsController a propósito: este es el único seed de desarrollo que
// necesita generar un PDF real y subirlo a storage, así que es el único que depende de
// IStorageService. Con R2:AccountId vacío cae a LocalDiskStorageService (ver
// ServiceCollectionExtensions), así que funciona sin credenciales reales — pero si algún
// día ese fallback se retira, este sería el único endpoint que rompe.
[ApiController]
[Route("api/v1/public/dev")]
public sealed class DevToolsPayoutGenerationController : ControllerBase
{
    private static readonly Guid DevOrgId = new("a1000000-0000-0000-0000-000000000001");

    private readonly IWebHostEnvironment            _env;
    private readonly ICollaboratorRepository        _collaborators;
    private readonly IOrganizationRepository        _organizations;
    private readonly ICollaboratorPayoutInvoiceRepository _payoutInvoices;
    private readonly IStorageService                _storage;
    private readonly IPayoutInvoicePdfGenerator      _pdf;
    private readonly IOrganizationBrandingResolver   _branding;
    private readonly IUnitOfWork                    _uow;

    public DevToolsPayoutGenerationController(
        IWebHostEnvironment            env,
        ICollaboratorRepository        collaborators,
        IOrganizationRepository        organizations,
        ICollaboratorPayoutInvoiceRepository payoutInvoices,
        IStorageService                storage,
        IPayoutInvoicePdfGenerator     pdf,
        IOrganizationBrandingResolver  branding,
        IUnitOfWork                    uow)
    {
        _env            = env;
        _collaborators  = collaborators;
        _organizations  = organizations;
        _payoutInvoices = payoutInvoices;
        _storage        = storage;
        _pdf            = pdf;
        _branding       = branding;
        _uow            = uow;
    }

    /// <summary>
    /// POST /api/v1/public/dev/seed-payout-generated-token — a diferencia de seed-payout-token
    /// (source=manual_upload, emula "Solicitar al colaborador"), esta genera un PDF real y sube
    /// a R2 (source=generated, emula "Generar y enviar"). Requiere R2:AccountId/AccessKey/SecretKey
    /// reales en appsettings.Local.json — sin eso, este endpoint (y solo este) responde 500.
    /// </summary>
    [HttpPost("seed-payout-generated-token")]
    public async Task<IActionResult> SeedPayoutGeneratedToken(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var collaborators = await _collaborators.ListAsync(DevOrgId, status: "active", ct: ct);
        var collaborator  = collaborators.FirstOrDefault(c => c.Type != "employee");
        if (collaborator is null)
            return NotFound(new { error = "No hay colaboradores de prueba sembrados por DevDataSeeder." });

        var organization = await _organizations.GetByIdAsync(DevOrgId, ct);
        var branding      = await _branding.ResolveAsync(DevOrgId, ct);
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
            DateTime.UtcNow,
            LogoBytes: branding.LogoBytes,
            LegalName: branding.LegalName,
            Nit: branding.Nit,
            Address: branding.Address,
            City: branding.City,
            Email: branding.Email,
            Phone: branding.Phone));

        var key      = StorageKeyBuilder.Build(
            DevOrgId, "payout-invoices", collaborator.Id, $"cuenta-cobro-{period}", "application/pdf");
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
}
