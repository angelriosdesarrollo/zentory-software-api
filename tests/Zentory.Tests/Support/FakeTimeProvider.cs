namespace Zentory.Tests.Support;

// Reloj fijo para los tests (PLAN-0018 D-35): escrito a mano para no sumar Microsoft.Extensions.TimeProvider.Testing.
// Solo fija la hora; los temporizadores siguen siendo los del TimeProvider base.
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
