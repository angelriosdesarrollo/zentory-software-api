using Microsoft.AspNetCore.Mvc;
using Zentory.Infrastructure.Services;
using Zentory.API.Authorization;

namespace Zentory.API.Controllers.V1;

// Sirve/recibe los archivos que LocalDiskStorageService guarda en disco cuando no hay
// credenciales R2 reales configuradas — emula lo que en producción hace un PUT/GET
// directo contra R2 vía URL pre-firmada. Nunca se registra como IStorageService real,
// así que fuera de Development esto simplemente no se usa (pero igual devuelve 404
// fuera de Development, por si acaso).
[ApiController]
[SkipMembershipValidation] // lista cerrada de PLAN-0018 RF-14 (SkipMembershipCatalogTests)
[Route("api/v1/public/dev/local-storage")]
public sealed class DevLocalStorageController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public DevLocalStorageController(IWebHostEnvironment env) => _env = env;

    [HttpPut("{**key}")]
    public async Task<IActionResult> Put(string key, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var rootPath = LocalDevStoragePaths.RootPath(_env);
        var path     = LocalDevStoragePaths.ResolveSafePath(rootPath, key);
        if (IsLegalArchive(rootPath, path)) return LegalArchiveNotFound();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = System.IO.File.Create(path);
        await Request.Body.CopyToAsync(file, ct);

        return Ok();
    }

    [HttpGet("{**key}")]
    public IActionResult Get(string key)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var rootPath = LocalDevStoragePaths.RootPath(_env);
        var path     = LocalDevStoragePaths.ResolveSafePath(rootPath, key);
        if (IsLegalArchive(rootPath, path)) return LegalArchiveNotFound();
        if (!System.IO.File.Exists(path)) return NotFound();

        var contentType = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/octet-stream";
        return PhysicalFile(path, contentType);
    }

    // Carpeta de los PDF del archivo legal (claves legal-archive/{Guid}.pdf). El literal es a propósito: ningún
    // controlador referencia el tipo del archivo legal (LegalArchivePolicyTests).
    private const string LegalArchiveFolder = "legal-archive";

    // El archivo legal no se sirve ni se recibe por aquí (PLAN-0018 RF-79 c, SR-0018.28): solo lo lee el operador. Se
    // compara la ruta ya resuelta, así "x/../legal-archive/...", barras invertidas o mayúsculas tampoco pasan.
    // 404 sin cuerpo: ni el archivo ni un ProblemDetails que confirme qué se pidió.
    private IActionResult LegalArchiveNotFound()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }

    private static bool IsLegalArchive(string rootPath, string resolvedPath)
    {
        var archiveRoot = Path.GetFullPath(Path.Combine(rootPath, LegalArchiveFolder));
        return string.Equals(resolvedPath, archiveRoot, StringComparison.OrdinalIgnoreCase)
            || resolvedPath.StartsWith(archiveRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
