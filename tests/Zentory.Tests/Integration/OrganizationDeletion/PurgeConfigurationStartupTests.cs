using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zentory.API.Extensions;
using Zentory.Application.Common.Options;
using Zentory.Infrastructure.Extensions;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 CA-T12-2 (parte de CA-107; RF-78, RF-79; ADR purga §7): el plazo de purga y la retención del archivo legal
// se validan al arrancar. Un valor menor que 1 en Organizations:PurgeAfterDays o en Retention:LegalDocumentsYears impide
// que el host arranque, y el error nombra la clave (sin imprimir ningún otro valor de configuración).
public sealed class PurgeConfigurationStartupTests
{
    public static TheoryData<string, string> InvalidSettings => new()
    {
        { OrganizationsOptions.PurgeAfterDaysKey,    "0" },
        { OrganizationsOptions.PurgeAfterDaysKey,    "-1" },
        { RetentionOptions.LegalDocumentsYearsKey,   "0" },
        { RetentionOptions.LegalDocumentsYearsKey,   "-5" },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public async Task PLAN0018_CA107_Host_ConPlazoDePurgaORetencionLegalMenorQueUno_NoArranca(string key, string value)
    {
        await using var factory = HostWith(key, value);

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains(key, error!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PLAN0018_CA107_Host_ConRetencionLegalSinConfigurar_UsaCincoAniosYElConfiguradoGana()
    {
        await using (var defaults = HostWith(null, null))
        {
            defaults.CreateClient().Dispose();
            Assert.Equal(RetentionOptions.DefaultLegalDocumentsYears,
                defaults.Services.GetRequiredService<IOptions<RetentionOptions>>().Value.LegalDocumentsYears);
        }
        Assert.Equal(5, RetentionOptions.DefaultLegalDocumentsYears);

        await using var configured = HostWith(RetentionOptions.LegalDocumentsYearsKey, "7");
        configured.CreateClient().Dispose();
        Assert.Equal(7, configured.Services.GetRequiredService<IOptions<RetentionOptions>>().Value.LegalDocumentsYears);
    }

    private static MinimalHostFactory HostWith(string? key, string? value)
    {
        var settings = new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "true",
            [DatabaseSettings.InMemoryNameKey]  = $"zentory-purge-config-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey] = "false",
            [DevSeedSettings.EnabledKey]        = "false",
        };
        if (key is not null)
            settings[key] = value;
        return new MinimalHostFactory(AppEnvironments.Testing, settings);
    }
}
