namespace Zentory.Tests.Support;

// Raíz de contenido temporal con solo los appsettings versionados del API. Un host que la usa no ve
// appsettings.Local.json ni appsettings.Development.json de la máquina (no se leen ni se copian), sea cual sea su
// ambiente. Tampoco escribe nada bajo src/: lo que el host guarde en su raíz de contenido (App_Data) se borra con ella.
internal sealed class VersionedContentRoot : IDisposable
{
    public const string DefaultPrefix = "zentory-host-";

    private static readonly string[] VersionedSettingsFiles = ["appsettings.json", "appsettings.Production.json"];

    public string Path { get; }

    public VersionedContentRoot()
        : this(System.IO.Path.Combine(RepoRoot.Path, "src", "Zentory.API"), DefaultPrefix)
    {
    }

    // Origen y prefijo configurables solo para probar la limpieza ante una copia fallida (PLAN-0018 CA-25 d).
    internal VersionedContentRoot(string apiFolder, string prefix)
    {
        Path = Directory.CreateTempSubdirectory(prefix).FullName;
        try
        {
            foreach (var file in VersionedSettingsFiles)
                File.Copy(System.IO.Path.Combine(apiFolder, file), System.IO.Path.Combine(Path, file));
        }
        catch
        {
            // Sin esto, cada constructor fallido dejaba una carpeta huérfana en %TEMP%.
            Delete();
            throw;
        }
    }

    public void Dispose() => Delete();

    private void Delete()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { /* carpeta temporal: si queda bloqueada, la limpia el sistema */ }
        catch (UnauthorizedAccessException) { }
    }
}
