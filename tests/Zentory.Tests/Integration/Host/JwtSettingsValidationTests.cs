using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Infrastructure.Extensions;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0018 CA-35 (SPEC-0001 CA-23.1; RF-31, G-9): el host real no arranca si Jwt:Key o Jwt:CollaboratorKey está vacía,
// mide menos de 32 bytes UTF-8 o las dos son iguales, ni si Jwt:AccessTokenExpiryMinutes está fuera de 1..15. El error
// nombra la clave que falla y nunca imprime un valor. Las claves de cada caso salen de TestJwtSettings.
public sealed class JwtSettingsValidationTests
{
    public static TheoryData<string, string, string, string> InvalidSettings => new()
    {
        // caso,                    Jwt:Key,                             Jwt:CollaboratorKey,                            clave que debe nombrar el error
        { "key-vacia",              "",                                  TestJwtSettings.CollaboratorKey,                JwtSettingsValidation.KeySetting },
        { "key-en-blanco",          new string(' ', 40),                 TestJwtSettings.CollaboratorKey,                JwtSettingsValidation.KeySetting },
        { "key-31-bytes",           TestJwtSettings.KeyWithBytes(31),    TestJwtSettings.CollaboratorKey,                JwtSettingsValidation.KeySetting },
        { "collaborator-vacia",     TestJwtSettings.Key,                 "",                                             JwtSettingsValidation.CollaboratorKeySetting },
        { "collaborator-31-bytes",  TestJwtSettings.Key,                 TestJwtSettings.CollaboratorKeyWithBytes(31),   JwtSettingsValidation.CollaboratorKeySetting },
        { "claves-iguales",         TestJwtSettings.Key,                 TestJwtSettings.Key,                            JwtSettingsValidation.CollaboratorKeySetting },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public async Task PLAN0018_CA35_Host_ConClaveJwtVaciaCortaOIgualALaOtra_NoArrancaYNombraLaClave(
        string scenario, string key, string collaboratorKey, string failingSetting)
    {
        await using var factory = HostWith(key, collaboratorKey, TestJwtSettings.AccessTokenExpiryMinutes.ToString(CultureInfo.InvariantCulture));

        var error = Record.Exception(() => factory.CreateClient());

        AssertJwtStartupFailure(error, failingSetting, scenario, key, collaboratorKey);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("16")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("quince")]
    public async Task PLAN0018_CA35_Host_ConExpiracionFueraDeUnoAQuince_NoArrancaYNombraLaClave(string minutes)
    {
        await using var factory = HostWith(TestJwtSettings.Key, TestJwtSettings.CollaboratorKey, minutes);

        var error = Record.Exception(() => factory.CreateClient());

        AssertJwtStartupFailure(error, JwtSettingsValidation.AccessTokenExpiryMinutesSetting, $"expiracion-{minutes}",
            TestJwtSettings.Key, TestJwtSettings.CollaboratorKey);
    }

    public static TheoryData<string, string, string, int> ValidSettings => new()
    {
        { "32-bytes-y-1-min",   TestJwtSettings.KeyWithBytes(32), TestJwtSettings.CollaboratorKeyWithBytes(32), 1 },
        { "32-bytes-y-15-min",  TestJwtSettings.KeyWithBytes(32), TestJwtSettings.CollaboratorKeyWithBytes(32), 15 },
        { "claves-de-prueba",   TestJwtSettings.Key,              TestJwtSettings.CollaboratorKey,              10 },
        // 16 caracteres de dos bytes: 32 bytes UTF-8 aunque solo sean 16 caracteres.
        { "32-bytes-multibyte", new string('ñ', 16),              TestJwtSettings.CollaboratorKeyWithBytes(32), 15 },
    };

    [Theory]
    [MemberData(nameof(ValidSettings))]
    public async Task PLAN0018_CA35_Host_ConClavesDistintasDe32BytesOMasYExpiracionDeUnoAQuince_ArrancaYEmiteConEsaExpiracion(
        string scenario, string key, string collaboratorKey, int minutes)
    {
        await using var factory = HostWith(key, collaboratorKey, minutes.ToString(CultureInfo.InvariantCulture));

        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        Assert.True(TimeSpan.FromMinutes(minutes) == scope.ServiceProvider.GetRequiredService<IJwtService>().AccessTokenLifetime,
            $"Caso {scenario}: la expiración emitida no es la configurada.");
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static MinimalHostFactory HostWith(string key, string collaboratorKey, string expiryMinutes)
        => new(AppEnvironments.Testing, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]        = "true",
            [DatabaseSettings.InMemoryNameKey]       = $"zentory-jwt-settings-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey]      = "false",
            [DevSeedSettings.EnabledKey]             = "false",
            [TestJwtSettings.KeySetting]             = key,
            [TestJwtSettings.CollaboratorKeySetting] = collaboratorKey,
            [TestJwtSettings.ExpirySetting]          = expiryMinutes,
        });

    // El arranque falla con el InvalidOperationException de la validación, que nombra la clave; ni ese mensaje ni la
    // cadena completa de excepciones contienen las claves configuradas (se compara sin imprimirlas).
    private static void AssertJwtStartupFailure(
        Exception? error, string failingSetting, string scenario, string key, string collaboratorKey)
    {
        Assert.True(error is not null, $"Caso {scenario}: el host arrancó con una configuración JWT inválida.");

        var validation = FindInChain(error!, e => e.Message.Contains(failingSetting, StringComparison.Ordinal));
        Assert.True(validation is not null, $"Caso {scenario}: el error de arranque no nombra {failingSetting}.");

        var chain = error!.ToString();
        foreach (var secret in new[] { key, collaboratorKey }.Where(s => s.Trim().Length > 0))
            Assert.False(chain.Contains(secret, StringComparison.Ordinal),
                $"Caso {scenario}: el error de arranque contiene una clave JWT configurada.");
    }

    private static InvalidOperationException? FindInChain(Exception exception, Func<InvalidOperationException, bool> predicate)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException match && predicate(match)) return match;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    var found = FindInChain(inner, predicate);
                    if (found is not null) return found;
                }
            }
        }

        return null;
    }
}
