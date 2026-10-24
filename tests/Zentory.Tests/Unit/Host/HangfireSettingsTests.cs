using Microsoft.Extensions.Configuration;
using Zentory.Infrastructure.Extensions;

namespace Zentory.Tests.Unit.Host;

// PLAN-0001 (RF-14, D-4): Hangfire:ServerEnabled se lee con una sola función y por defecto deja el servidor encendido,
// para que dev y prod sigan ejecutando monthly-pila-auto-request sin configurar nada.
public sealed class HangfireSettingsTests
{
    [Fact]
    public void PLAN0001_CAT2_1_IsServerEnabled_WithoutKey_ReturnsTrue()
    {
        Assert.True(HangfireSettings.IsServerEnabled(Configuration(new())));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    public void PLAN0001_CAT2_1_IsServerEnabled_WithFalse_ReturnsFalse(string value)
    {
        var configuration = Configuration(new() { [HangfireSettings.ServerEnabledKey] = value });

        Assert.False(HangfireSettings.IsServerEnabled(configuration));
    }

    [Fact]
    public void PLAN0001_CAT2_1_IsServerEnabled_WithTrue_ReturnsTrue()
    {
        var configuration = Configuration(new() { [HangfireSettings.ServerEnabledKey] = "true" });

        Assert.True(HangfireSettings.IsServerEnabled(configuration));
    }

    [Fact]
    public void PLAN0001_CAT2_1_IsServerEnabled_WithInvalidValue_ThrowsWithoutEchoingIt()
    {
        var configuration = Configuration(new() { [HangfireSettings.ServerEnabledKey] = "nope" });

        var exception = Assert.Throws<InvalidOperationException>(() => HangfireSettings.IsServerEnabled(configuration));
        Assert.Contains(HangfireSettings.ServerEnabledKey, exception.Message);
        Assert.DoesNotContain("nope", exception.Message);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
