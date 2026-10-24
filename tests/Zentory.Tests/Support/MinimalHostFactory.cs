using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Zentory.Tests.Support;

// Host mínimo sobre Program.cs para los tests que necesitan un ambiente distinto de Testing (Development, Staging,
// Production), donde ZentoryApiFactory no aplica. La raíz de contenido es una VersionedContentRoot, y toda la
// configuración restante llega por UseSetting, que el hosting mínimo ve como línea de comandos antes de
// AddInfrastructure.
internal sealed class MinimalHostFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly VersionedContentRoot _contentRoot = new();

    public MinimalHostFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        _environment = environment;
        _settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseContentRoot(_contentRoot.Path);
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _contentRoot.Dispose();
    }
}
