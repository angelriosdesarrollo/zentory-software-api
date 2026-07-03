using Microsoft.AspNetCore.Mvc;
using Zentory.Infrastructure.Services;

namespace Zentory.API.Controllers.V1;

// Sirve/recibe los archivos que LocalDiskStorageService guarda en disco cuando no hay
// credenciales R2 reales configuradas — emula lo que en producción hace un PUT/GET
// directo contra R2 vía URL pre-firmada. Nunca se registra como IStorageService real,
// así que fuera de Development esto simplemente no se usa (pero igual devuelve 404
// fuera de Development, por si acaso).
[ApiController]
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
        if (!System.IO.File.Exists(path)) return NotFound();

        var contentType = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/octet-stream";
        return PhysicalFile(path, contentType);
    }
}
