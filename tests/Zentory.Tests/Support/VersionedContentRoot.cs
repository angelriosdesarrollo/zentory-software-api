namespace Zentory.Tests.Support;

// Raíz de contenido temporal con solo los appsettings versionados del API. Un host que la usa no ve
// appsettings.Local.json ni appsettings.Development.json de la máquina (no se leen ni se copian), sea cual sea su
// ambiente.
internal sealed class VersionedContentRoot : IDisposable
{
    private static readonly string[] VersionedSettingsFiles = ["appsettings.json", "appsettings.Production.json"];

    public string Path { get; } = Directory.CreateTempSubdirectory("zentory-host-").FullName;

    public VersionedContentRoot()
    {
        var apiFolder = System.IO.Path.Combine(RepoRoot.Path, "src", "Zentory.API");
        foreach (var file in VersionedSettingsFiles)
            File.Copy(System.IO.Path.Combine(apiFolder, file), System.IO.Path.Combine(Path, file));
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* carpeta temporal: si queda bloqueada, la limpia el sistema */ }
        catch (UnauthorizedAccessException) { }
    }
}
