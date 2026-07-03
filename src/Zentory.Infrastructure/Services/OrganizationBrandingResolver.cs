using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

public sealed class OrganizationBrandingResolver : IOrganizationBrandingResolver
{
    private static readonly TimeSpan LogoFetchTimeout = TimeSpan.FromSeconds(3);

    private readonly IZentoryDbContext _db;
    private readonly HttpClient        _http;

    public OrganizationBrandingResolver(IZentoryDbContext db, HttpClient http)
    {
        _db   = db;
        _http = http;
    }

    public async Task<OrganizationBranding> ResolveAsync(Guid organizationId, CancellationToken ct = default)
    {
        var settings = await _db.OrganizationSettings
            .Where(s => s.OrganizationId == organizationId)
            .ToListAsync(ct);
        var s = settings.ToDictionary(x => x.Key, x => x.Value);

        var logoUrl = s.GetValueOrDefault("profile.logo_url");
        var logoBytes = await TryFetchLogoAsync(logoUrl, ct);

        return new OrganizationBranding(
            logoBytes,
            s.GetValueOrDefault("profile.legal_name"),
            s.GetValueOrDefault("profile.nit"),
            s.GetValueOrDefault("profile.address"),
            s.GetValueOrDefault("profile.city"),
            s.GetValueOrDefault("profile.email"),
            s.GetValueOrDefault("profile.phone"));
    }

    // Nunca debe bloquear ni romper la generación del PDF — si el logo no está configurado,
    // la URL es inválida, o la descarga se demora, el documento sale igual sin logo.
    private async Task<byte[]?> TryFetchLogoAsync(string? logoUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(LogoFetchTimeout);
            return await _http.GetByteArrayAsync(logoUrl, cts.Token);
        }
        catch
        {
            return null;
        }
    }
}
