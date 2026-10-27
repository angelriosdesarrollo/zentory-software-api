using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Common.Security;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Services;

namespace Zentory.Tests.Support;

// Espía del verificador de reautenticación (PLAN-0018 CA-109, CA-117, RF-65 b2) para un host derivado: delega en el
// ReauthenticationVerifier real y cuenta las llamadas (un admin o member no debe llegar a verificar). AfterVerify corre
// después de una verificación válida y antes de que el comando siga: sirve para provocar una carrera (cambio concurrente
// de la organización, dos peticiones con el mismo nonce) en un punto exacto.
public sealed class ReauthVerifierSpy
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Func<Task>? AfterVerify { get; set; }

    public void Install(IServiceCollection services)
    {
        services.RemoveAll<IReauthenticationVerifier>();
        services.AddScoped<ReauthenticationVerifier>();
        services.AddSingleton(this);
        services.AddScoped<IReauthenticationVerifier>(sp =>
            new Spying(sp.GetRequiredService<ReauthenticationVerifier>(), sp.GetRequiredService<ReauthVerifierSpy>()));
    }

    private sealed class Spying(IReauthenticationVerifier inner, ReauthVerifierSpy spy) : IReauthenticationVerifier
    {
        public async Task<ReauthVerification> VerifyAsync(
            User user, ReauthCredential credential, string action, Guid? organizationId = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref spy._calls);
            var verification = await inner.VerifyAsync(user, credential, action, organizationId, ct);
            if (spy.AfterVerify is { } hook)
                await hook();
            return verification;
        }
    }
}
