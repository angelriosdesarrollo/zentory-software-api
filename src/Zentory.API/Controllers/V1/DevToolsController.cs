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
//
// A propósito NO tiene IStorageService/IPayoutInvoicePdfGenerator en el constructor:
// ASP.NET Core resuelve TODAS las dependencias del constructor al enrutar cualquier
// acción de este controller, así que si CloudflareR2StorageService no puede construirse
// (sin credenciales R2 reales configuradas localmente), CUALQUIER endpoint de acá
// fallaría — incluidos los que no tocan storage para nada. El seed que sí necesita
// generar un PDF real y subirlo a R2 vive aparte, en DevToolsPayoutGenerationController.
[ApiController]
[Route("api/v1/public/dev")]
public sealed class DevToolsController : ControllerBase
{
    private static readonly Guid DevOrgId = new("a1000000-0000-0000-0000-000000000001");

    private readonly IWebHostEnvironment            _env;
    private readonly ICollaboratorRepository        _collaborators;
    private readonly IPilaVerificationRepository    _pilaVerifications;
    private readonly ICollaboratorPayoutInvoiceRepository _payoutInvoices;
    private readonly IZentoryDbContext              _db;
    private readonly IUnitOfWork                    _uow;

    public DevToolsController(
        IWebHostEnvironment            env,
        ICollaboratorRepository        collaborators,
        IPilaVerificationRepository    pilaVerifications,
        ICollaboratorPayoutInvoiceRepository payoutInvoices,
        IZentoryDbContext              db,
        IUnitOfWork                    uow)
    {
        _env               = env;
        _collaborators     = collaborators;
        _pilaVerifications = pilaVerifications;
        _payoutInvoices    = payoutInvoices;
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
