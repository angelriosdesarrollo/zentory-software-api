using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 CA-110 (RF-81 c, d; RF-84) y CA-T11-2 (parte de CA-117; RF-84 d) sobre el host real: DELETE /organization con
// googleIdToken. El validador de Google es el doble que lanza la excepción real ante un token rechazado, el reloj es
// simulado y el nonce sale de GET /auth/reauth-nonce. También RF-65 b2: un cambio concurrente de la organización entre la
// verificación y la escritura responde 409 sin efecto y libera el nonce.
public sealed class DeleteOrganizationGoogleReauthTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword = "Owner-Correcta-2026!";

    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(10);

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeTimeProvider               _time;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly ReauthVerifierSpy              _spy    = new();
    private readonly WebApplicationFactory<Program> _host;

    public DeleteOrganizationGoogleReauthTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        // Segundo exacto y cerca del reloj real: el JWT de la petición se emite con la hora del sistema.
        _time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_time);
            services.RemoveAll<IGoogleTokenValidator>();
            services.AddSingleton<IGoogleTokenValidator>(_google);
            _spy.Install(services);
        }));
    }

    public void Dispose() => _host.Dispose();

    // ── CA-110 ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA110_DeleteOrganization_GoogleConIatDeHace4MinSubVerificadoYNonce_Responde204()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        using var http = Client(tenants, tenants.A);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);

        using var response = await DeleteWithGoogleAsync(http, tenants, Token(owner, nonce, iatOffset: TimeSpan.FromMinutes(-4)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull((await OrganizationOf(tenants.A)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA110_DeleteOrganization_IatDeHace5Min1s_Responde403SinEfecto()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        using var http = Client(tenants, tenants.A);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);

        using var response = await DeleteWithGoogleAsync(http, tenants,
            Token(owner, nonce, iatOffset: -(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1))));

        await AssertReauthenticationFailedAsync(response);
        Assert.Null((await OrganizationOf(tenants.A)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA110_DeleteOrganization_Iat31sEnElFuturo403_Y29sEnElFuturo204()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        using var http = Client(tenants, tenants.A);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);

        using (var future31 = await DeleteWithGoogleAsync(http, tenants, Token(owner, nonce, iatOffset: TimeSpan.FromSeconds(31))))
        {
            await AssertReauthenticationFailedAsync(future31);
            Assert.Null((await OrganizationOf(tenants.A)).DeletedAt);
        }

        // El intento rechazado no consumió el nonce (RF-84 d): el mismo nonce con iat válido funciona.
        using var future29 = await DeleteWithGoogleAsync(http, tenants, Token(owner, nonce, iatOffset: TimeSpan.FromSeconds(29)));

        Assert.Equal(HttpStatusCode.NoContent, future29.StatusCode);
    }

    [Theory]
    [InlineData("other-sub")]
    [InlineData("email-not-verified")]
    [InlineData("rejected-by-validator")]
    public async Task PLAN0018_CA110_DeleteOrganization_SubAjenoEmailSinVerificarOTokenRechazado_Responde403YNo422(string variant)
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        using var http = Client(tenants, tenants.A);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);
        var idToken = variant switch
        {
            "other-sub"          => Token(owner, nonce, subject: $"google-other-{Guid.NewGuid():N}"),
            "email-not-verified" => Token(owner, nonce, emailVerified: false),
            _                    => FakeGoogleTokenValidator.RejectedToken(),
        };

        using var response = await DeleteWithGoogleAsync(http, tenants, idToken);

        await AssertReauthenticationFailedAsync(response);
        Assert.Null((await OrganizationOf(tenants.A)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA110_DeleteOrganization_OwnerSoloGoogleConCurrentPassword_Responde403SinEfecto()
    {
        var (tenants, _) = await SeedGoogleOwnerAsync();
        using var http = Client(tenants, tenants.A);

        using var response = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, OwnerPassword);

        await AssertReauthenticationFailedAsync(response);
        Assert.Null((await OrganizationOf(tenants.A)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA110_DeleteOrganization_Los403DeReautenticacion_TienenElMismoCuerpo()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        using var http = Client(tenants, tenants.A);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);
        var bodies = new List<string>();

        foreach (var idToken in new[]
                 {
                     Token(owner, nonce, iatOffset: -(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1))),
                     Token(owner, nonce, iatOffset: TimeSpan.FromSeconds(31)),
                     Token(owner, nonce, subject: $"google-other-{Guid.NewGuid():N}"),
                     Token(owner, nonce, emailVerified: false),
                     Token(owner, nonce: null),
                     FakeGoogleTokenValidator.RejectedToken(),
                 })
        {
            using var response = await DeleteWithGoogleAsync(http, tenants, idToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }
        using (var password = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, OwnerPassword))
            bodies.Add(await password.Content.ReadAsStringAsync());

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.Null((await OrganizationOf(tenants.A)).DeletedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PLAN0018_CA110_DeleteOrganization_OwnerConContrasenaYGoogle_Responde204ConCualquieraDeLasDos(bool usePassword)
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync(withPassword: true);
        using var http = Client(tenants, tenants.A);

        HttpResponseMessage response;
        if (usePassword)
            response = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, OwnerPassword);
        else
            response = await DeleteWithGoogleAsync(http, tenants,
                Token(owner, await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete)));

        using (response)
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull((await OrganizationOf(tenants.A)).DeletedAt);
    }

    // ── CA-117 (parte, CA-T11-2) ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA117_DeleteOrganization_IdTokenSinNonceOConElDeTransfer_Responde403YConElCorrecto204()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        using var http = Client(tenants, tenants.A);
        var transferNonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationTransfer);

        using (var withoutNonce = await DeleteWithGoogleAsync(http, tenants, Token(owner, nonce: null)))
            await AssertReauthenticationFailedAsync(withoutNonce);
        using (var otherAction = await DeleteWithGoogleAsync(http, tenants, Token(owner, transferNonce)))
            await AssertReauthenticationFailedAsync(otherAction);
        Assert.Null((await OrganizationOf(tenants.A)).DeletedAt);

        var deleteNonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);
        using var response = await DeleteWithGoogleAsync(http, tenants, Token(owner, deleteNonce));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA117_DeleteOrganization_SegundoUsoDelMismoNonce_Responde403SinEfecto()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        var second = await SeedOwnedOrganizationAsync(owner);
        using var http = Client(tenants, tenants.A);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);

        using (var first = await DeleteWithGoogleAsync(http, tenants, Token(owner, nonce)))
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Dentro de sus 5 minutos, con otro id_token que lleva el mismo nonce y sobre otra organización propia.
        _time.Advance(TimeSpan.FromMinutes(1));
        using var http2 = ClientFor(owner, second);
        using var reuse = await OrganizationDeletionApi.DeleteAsync(http2, second.Name, googleIdToken: Token(owner, nonce));

        await AssertReauthenticationFailedAsync(reuse);
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(_host.Services, second.OrganizationId)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA117_DeleteOrganization_DosPeticionesConcurrentesConElMismoNonce_UnExitoYUn403()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        var second = await SeedOwnedOrganizationAsync(owner);
        using var httpA = Client(tenants, tenants.A);
        using var httpC = ClientFor(owner, second);
        var nonce = await OrganizationDeletionApi.NonceAsync(httpA, ReauthAction.OrganizationDelete);

        // Las dos verifican el nonce antes de que cualquiera lo consuma: la carrera queda en TryConsume.
        var arrived = 0;
        var bothVerified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _spy.AfterVerify = async () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
                bothVerified.TrySetResult();
            await bothVerified.Task.WaitAsync(BarrierTimeout);
        };
        try
        {
            var responses = await Task.WhenAll(
                DeleteWithGoogleAsync(httpA, tenants, Token(owner, nonce)),
                OrganizationDeletionApi.DeleteAsync(httpC, second.Name, googleIdToken: Token(owner, nonce)));

            var statuses = responses.Select(r => r.StatusCode).Order().ToList();
            Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Forbidden], statuses);
            foreach (var response in responses)
                response.Dispose();
        }
        finally
        {
            _spy.AfterVerify = null;
        }

        var deleted = new[]
        {
            (await OrganizationOf(tenants.A)).DeletedAt,
            (await OrganizationDeletionApi.OrganizationAsync(_host.Services, second.OrganizationId)).DeletedAt,
        };
        Assert.Single(deleted, value => value is not null);
    }

    [Fact]
    public async Task PLAN0018_CA117_DeleteOrganization_NombreIncorrectoNoConsumeElNonce_ElReintentoCorrectoResponde204()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        using var http = Client(tenants, tenants.A);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);
        var idToken = Token(owner, nonce);

        using (var wrongName = await OrganizationDeletionApi.DeleteAsync(http, "Otro nombre", googleIdToken: idToken))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongName.StatusCode);
        Assert.False(_host.Services.GetRequiredService<IReauthNonceStore>().IsConsumed(nonce));

        using var retry = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, googleIdToken: idToken);

        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.True(_host.Services.GetRequiredService<IReauthNonceStore>().IsConsumed(nonce));
    }

    // ── RF-65 b2: cambio concurrente ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA102_DeleteOrganization_OwnerCambiadoEntreVerificarYEscribir_Responde409SinEfectoYLiberaElNonce()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        var newOwner = tenants.B.Owner.UserId;
        using var http = Client(tenants, tenants.A);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);

        // Otra operación (p. ej. una transferencia aceptada) cambia el owner después de que el comando leyó la fila.
        _spy.AfterVerify = async () =>
        {
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var organization = await db.Organizations.SingleAsync(o => o.OrganizationId == tenants.A.OrganizationId);
            organization.SetOwner(newOwner);
            await db.SaveChangesAsync();
        };
        HttpResponseMessage response;
        try
        {
            response = await DeleteWithGoogleAsync(http, tenants, Token(owner, nonce));
        }
        finally
        {
            _spy.AfterVerify = null;
        }

        using (response)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(DeleteOrganizationCommandHandler.ChangedCode, await response.Content.ReadAsStringAsync());
        }
        var organization = await OrganizationOf(tenants.A);
        Assert.Null(organization.DeletedAt);
        Assert.Equal(newOwner, organization.OwnerId);
        Assert.False(_host.Services.GetRequiredService<IReauthNonceStore>().IsConsumed(nonce));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<(TenantPair Tenants, User Owner)> SeedGoogleOwnerAsync(bool withPassword = false)
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a =>
            {
                a.WithOwnerGoogleId($"google-sub-{Guid.NewGuid():N}");
                if (withPassword)
                    a.WithOwnerPassword(OwnerPassword);
            })
            .BuildAsync();
        return (tenants, tenants.A.Owner);
    }

    // Otra organización del mismo owner, con su membresía de owner: el nonce no está ligado a la organización.
    private async Task<OrganizationEntity> SeedOwnedOrganizationAsync(User owner)
    {
        var organization = OrganizationEntity.Create($"Segunda {Guid.NewGuid():N}", LegalType.Empresa);
        organization.SetOwner(owner.UserId);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.Organizations.Add(organization);
        db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, owner.UserId, OrgRole.Owner));
        await db.SaveChangesAsync();
        return organization;
    }

    private HttpClient Client(TenantPair tenants, SeededTenant tenant) => OrganizationDeletionApi.ClientFor(_host, tenants, tenant);

    private HttpClient ClientFor(User owner, OrganizationEntity organization)
    {
        var http = _host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", _factory.Tokens.ForUser(owner, organization, OrgRole.Owner, Plan.Studio));
        return http;
    }

    private Task<HttpResponseMessage> DeleteWithGoogleAsync(HttpClient http, TenantPair tenants, string idToken)
        => OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, googleIdToken: idToken);

    // id_token válido por defecto: sub del owner, email verificado, iat = ahora del reloj simulado.
    private string Token(User owner, string? nonce, TimeSpan? iatOffset = null, string? subject = null, bool emailVerified = true)
        => _google.Register(new GoogleUserInfo(subject ?? owner.GoogleId!, owner.Email, owner.FirstName, owner.LastName,
            EmailVerified: emailVerified, IssuedAt: _time.GetUtcNow() + (iatOffset ?? TimeSpan.Zero), Nonce: nonce));

    private Task<OrganizationEntity> OrganizationOf(SeededTenant tenant)
        => OrganizationDeletionApi.OrganizationAsync(_host.Services, tenant.OrganizationId);

    private static async Task AssertReauthenticationFailedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(ReauthenticationFailedException.Code, text, StringComparison.Ordinal);
        Assert.Contains("\"error\":\"forbidden\"", text, StringComparison.Ordinal);
    }
}
