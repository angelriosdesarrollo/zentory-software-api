namespace Zentory.Tests.Support;

// Raíz del repo (la carpeta de Zentory.slnx), para los tests que leen archivos versionados del repo.
internal static class RepoRoot
{
    public static readonly string Path = Find();

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Zentory.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException($"No se encontró Zentory.slnx subiendo desde {AppContext.BaseDirectory}.");
    }
}
