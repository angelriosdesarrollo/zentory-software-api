namespace Zentory.Application.Common;

// Organiza el bucket con la organización como carpeta raíz (en vez de anidarla bajo el tipo
// de documento) para que sea navegable manualmente por org en R2, y arma nombres de archivo
// legibles (slug + sufijo corto) en vez de un GUID plano — el sufijo evita colisiones cuando
// se resube el mismo período sin que el nombre deje de ser reconocible a simple vista.
public static class StorageKeyBuilder
{
    public static string Build(Guid organizationId, string category, Guid subjectId, string slug, string? contentType)
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        return $"{organizationId}/{category}/{subjectId}/{slug}-{uniqueSuffix}{ExtensionFor(contentType)}";
    }

    private static string ExtensionFor(string? contentType) => contentType switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg"       => ".jpg",
        "image/png"        => ".png",
        "image/webp"       => ".webp",
        _                  => string.Empty,
    };
}
