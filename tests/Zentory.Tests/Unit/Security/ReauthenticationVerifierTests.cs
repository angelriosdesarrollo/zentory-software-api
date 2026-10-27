using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resend;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Security;

// PLAN-0018 CA-T10B-2 (parte de CA-110), CA-T10B-3 (parte de CA-123) y la parte de servicio de CA-117 (RF-81, RF-84;
// SR-0018.30, SR-0018.31, SR-0018.42; D-40). ReauthenticationVerifier con reloj simulado, el doble del validador de
// Google que lanza la misma excepción que el real, el limitador y una cola de correo que entrega en el acto a
// FakeEmailService. La aplicación a DELETE /organization y a la transferencia llega con T11, T30b y T30c.
public sealed class ReauthenticationVerifierTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private const string Password      = "correct-horse-battery-staple";
    private const string WrongPassword = "wrong-horse-battery-staple";
    private const string GoogleSubject = "google-sub-owner-g";

    private readonly FakeTimeProvider         _time     = new(Start);
    private readonly FakeGoogleTokenValidator _google   = new();
    private readonly FakeEmailService         _email    = new();
    private readonly CapturingLoggerProvider  _logs     = new();
    private readonly ZentoryDbContext         _db       = TestDbContextFactory.Create();
    private readonly ILoggerFactory           _loggerFactory;
    private readonly ReauthNonce              _nonces;
    private readonly InMemoryReauthNonceStore _store;
    private readonly AuthAttemptLimiter       _limiter;
    private readonly ReauthenticationVerifier _verifier;

    public ReauthenticationVerifierTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        _nonces        = new ReauthNonce(TestJwtSettings.Key, _time);
        _store         = new InMemoryReauthNonceStore(new MemoryCache(new MemoryCacheOptions()), _time);
        _limiter       = new AuthAttemptLimiter(_time, AuthRateLimitPolicies.Defaults);
        _verifier      = new ReauthenticationVerifier(_google, _nonces, _store, _limiter, new ImmediateEmailQueue(_email),
            _db, _time, _loggerFactory.CreateLogger<ReauthenticationVerifier>());
    }

    public void Dispose()
    {
        _db.Dispose();
        _loggerFactory.Dispose();
    }

    // ── CA-110: Google con iat, sub, email_verified y la excepción real del validador ──────────────────────────

    [Theory]
    [InlineData(-240)] // hace 4 min
    [InlineData(-300)] // hace exactamente 5 min (extremo incluido)
    [InlineData(29)]   // 29 s en el futuro
    [InlineData(30)]   // 30 s en el futuro (extremo incluido)
    public async Task PLAN0018_CA110_GoogleConIatDentroDeLaVentana_SeAceptaSinConsumirElNonce(int iatOffsetSeconds)
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        var token = GoogleToken(owner, iat: Start.AddSeconds(iatOffsetSeconds), nonce: nonce.Nonce);

        var verification = await _verifier.VerifyAsync(owner, Google(token), ReauthAction.OrganizationDelete);

        Assert.Equal(ReauthMethod.Google, verification.Method);
        Assert.Equal(nonce.Nonce, verification.Nonce);
        Assert.Equal(nonce.ExpiresAt, verification.NonceExpiresAt);
        // Verificación pendiente: el nonce lo consume el comando justo antes de escribir (D-40).
        Assert.False(_store.IsConsumed(nonce.Nonce));
        Assert.Empty(await _db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(-301)] // hace 5 min y 1 s
    [InlineData(31)]   // 31 s en el futuro
    public async Task PLAN0018_CA110_GoogleConIatFueraDeLaVentana_Falla403(int iatOffsetSeconds)
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        var token = GoogleToken(owner, iat: Start.AddSeconds(iatOffsetSeconds), nonce: nonce.Nonce);

        await AssertFailsAsync(owner, Google(token));
    }

    [Fact]
    public async Task PLAN0018_CA110_GoogleSinIat_Falla403()
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        var token = _google.Register(new GoogleUserInfo(GoogleSubject, owner.Email, owner.FirstName, owner.LastName,
            EmailVerified: true, IssuedAt: null, Nonce: nonce.Nonce));

        await AssertFailsAsync(owner, Google(token));
    }

    [Fact]
    public async Task PLAN0018_CA110_GoogleConSubDeOtraCuenta_Falla403()
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        var token = GoogleToken(owner, subject: "google-sub-another-account", nonce: nonce.Nonce);

        await AssertFailsAsync(owner, Google(token));
    }

    [Fact]
    public async Task PLAN0018_CA110_GoogleConEmailVerifiedFalse_Falla403()
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        var token = GoogleToken(owner, emailVerified: false, nonce: nonce.Nonce);

        await AssertFailsAsync(owner, Google(token));
    }

    [Fact]
    public async Task PLAN0018_CA110_TokenQueElValidadorRechazaConSuExcepcionReal_Falla403YNo422()
    {
        var owner = GoogleOnlyOwner();

        // El doble lanza lo mismo que GoogleTokenValidator ante un token rechazado: ValidationException (un 422).
        await Assert.ThrowsAsync<ValidationException>(() => _google.ValidateAsync(FakeGoogleTokenValidator.RejectedToken()));

        var error = await Record.ExceptionAsync(() =>
            _verifier.VerifyAsync(owner, Google(FakeGoogleTokenValidator.RejectedToken()), ReauthAction.OrganizationDelete));

        Assert.IsType<ReauthenticationFailedException>(error);
    }

    [Fact]
    public async Task PLAN0018_CA110_DobleDelValidador_LanzaLaMismaExcepcionQueElValidadorReal()
    {
        // El real rechaza un token mal formado antes de pedir certificados a Google: no hay red en este test.
        var real = new GoogleTokenValidator(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [GoogleTokenValidator.ClientIdSetting] = TestJwtSettings.GoogleClientId })
            .Build());

        var realError = await Assert.ThrowsAsync<ValidationException>(() => real.ValidateAsync("not-a-jwt"));
        var fakeError = await Assert.ThrowsAsync<ValidationException>(
            () => _google.ValidateAsync(FakeGoogleTokenValidator.RejectedToken()));

        Assert.Equal(realError.GetType(), fakeError.GetType());
        Assert.Equal(realError.Errors, fakeError.Errors);
    }

    [Fact]
    public async Task PLAN0018_CA110_CualquierExcepcionDelValidador_EsFalloUniforme()
    {
        var owner    = GoogleOnlyOwner();
        var verifier = new ReauthenticationVerifier(new ThrowingGoogleValidator(), _nonces, _store, _limiter,
            new ImmediateEmailQueue(_email), _db, _time, _loggerFactory.CreateLogger<ReauthenticationVerifier>());

        var error = await Record.ExceptionAsync(() =>
            verifier.VerifyAsync(owner, Google("any-token"), ReauthAction.OrganizationDelete));

        Assert.IsType<ReauthenticationFailedException>(error);
    }

    [Fact]
    public async Task PLAN0018_CA110_ContrasenaParaCuentaSoloGoogle_Falla403()
    {
        var owner = GoogleOnlyOwner();

        await AssertFailsAsync(owner, new ReauthCredential(Password, null));
    }

    [Fact]
    public async Task PLAN0018_CA110_GoogleParaCuentaSinGoogleId_Falla403()
    {
        var owner = PasswordOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        var token = _google.Register(new GoogleUserInfo(GoogleSubject, owner.Email, owner.FirstName, owner.LastName,
            EmailVerified: true, IssuedAt: Start, Nonce: nonce.Nonce));

        await AssertFailsAsync(owner, Google(token));
    }

    [Fact]
    public async Task PLAN0018_CA110_OwnerConContrasenaYGoogle_AceptaCualquieraDeLasDos()
    {
        var owner = User.Create("both@example.test", "Bea", "Both", OrgRole.Owner,
            passwordHash: BCrypt.Net.BCrypt.HashPassword(Password, 4), googleId: GoogleSubject);
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationTransfer);

        var byPassword = await _verifier.VerifyAsync(owner, new ReauthCredential(Password, null), ReauthAction.OrganizationTransfer);
        var byGoogle   = await _verifier.VerifyAsync(owner, Google(GoogleToken(owner, nonce: nonce.Nonce)),
            ReauthAction.OrganizationTransfer);

        Assert.Equal(ReauthMethod.Password, byPassword.Method);
        Assert.True(byPassword.TryConsume(_store));
        Assert.Equal(ReauthMethod.Google, byGoogle.Method);
    }

    [Fact]
    public async Task PLAN0018_CA110_ContrasenaIncorrecta_Falla403()
    {
        await AssertFailsAsync(PasswordOnlyOwner(), new ReauthCredential(WrongPassword, null));
    }

    [Fact]
    public async Task PLAN0018_CA110_LosFallos_TienenElMismoTipoYMensaje()
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        string[] tokens =
        [
            GoogleToken(owner, iat: Start.AddMinutes(-6), nonce: nonce.Nonce),
            GoogleToken(owner, subject: "google-sub-other", nonce: nonce.Nonce),
            GoogleToken(owner, emailVerified: false, nonce: nonce.Nonce),
            GoogleToken(owner, nonce: null),
            FakeGoogleTokenValidator.RejectedToken(),
        ];

        var errors = new List<Exception?>();
        foreach (var token in tokens)
            errors.Add(await Record.ExceptionAsync(() => _verifier.VerifyAsync(owner, Google(token), ReauthAction.OrganizationDelete)));
        errors.Add(await Record.ExceptionAsync(() =>
            _verifier.VerifyAsync(owner, new ReauthCredential(Password, null), ReauthAction.OrganizationDelete)));

        Assert.All(errors, error => Assert.IsType<ReauthenticationFailedException>(error));
        Assert.Single(errors.Select(error => error!.Message).Distinct());
    }

    // ── Contrato: exactamente una credencial ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(Password, "some-google-id-token")]
    public async Task PLAN0018_CA110_NingunaOLasDosCredenciales_Es422SinEcoNiConteo(string? password, string? googleIdToken)
    {
        var owner = PasswordOnlyOwner();

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            _verifier.VerifyAsync(owner, new ReauthCredential(password, googleIdToken), ReauthAction.OrganizationDelete));

        var detail = Assert.Single(error.Errors);
        Assert.Equal(ReauthCredential.FieldName, detail.Field);
        Assert.DoesNotContain(Password, detail.Message);
        Assert.DoesNotContain("some-google-id-token", detail.Message);
        Assert.Empty(await _db.AuditLogs.ToListAsync());
        Assert.Equal(0, _limiter.CountFailures(AuthRateLimitPolicies.Reauth, Subject(owner)));
    }

    [Fact]
    public void PLAN0018_CA110_ReauthCredential_ToStringNoIncluyeLosValores()
    {
        var text = new ReauthCredential(Password, "google-token-value").ToString();

        Assert.DoesNotContain(Password, text);
        Assert.DoesNotContain("google-token-value", text);
    }

    // ── CA-117 (servicio): nonce ausente, de otra acción, de otro sub, vencido o ya consumido ───────────────────

    [Fact]
    public async Task PLAN0018_CA117_IdTokenValidoYRecienteSinNonce_Falla403()
    {
        var owner = GoogleOnlyOwner();

        await AssertFailsAsync(owner, Google(GoogleToken(owner, nonce: null)));
    }

    [Fact]
    public async Task PLAN0018_CA117_NonceDeOtraAccion_Falla403()
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationTransfer);

        await AssertFailsAsync(owner, Google(GoogleToken(owner, nonce: nonce.Nonce)));
    }

    [Fact]
    public async Task PLAN0018_CA117_NonceDeOtroSub_Falla403()
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(Guid.NewGuid(), ReauthAction.OrganizationDelete);

        await AssertFailsAsync(owner, Google(GoogleToken(owner, nonce: nonce.Nonce)));
    }

    [Fact]
    public async Task PLAN0018_CA117_NonceVencido_Falla403()
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        _time.SetUtcNow(nonce.ExpiresAt + TimeSpan.FromSeconds(1));
        var token = GoogleToken(owner, iat: _time.GetUtcNow().AddMinutes(-1), nonce: nonce.Nonce);

        await AssertFailsAsync(owner, Google(token));
    }

    [Fact]
    public async Task PLAN0018_CA117_NonceYaConsumido_FallaConElMismoYConUnIdTokenNuevo_YReleasePermiteReintentar()
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        var first = GoogleToken(owner, nonce: nonce.Nonce);

        var verification = await _verifier.VerifyAsync(owner, Google(first), ReauthAction.OrganizationDelete);
        Assert.True(verification.TryConsume(_store));
        Assert.False(verification.TryConsume(_store));

        await AssertFailsAsync(owner, Google(first));
        await AssertFailsAsync(owner, Google(GoogleToken(owner, nonce: nonce.Nonce)), expectedRows: 2);

        // La escritura falló después de consumir: el comando libera y el reintento correcto vuelve a pasar.
        verification.Release(_store);
        var retry = await _verifier.VerifyAsync(owner, Google(first), ReauthAction.OrganizationDelete);
        Assert.True(retry.TryConsume(_store));
    }

    [Fact]
    public async Task PLAN0018_CA117_DosVerificacionesConElMismoNonce_SoloUnaConsume()
    {
        var owner = GoogleOnlyOwner();
        var nonce = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);

        // Las dos pasan la verificación (nadie consumió aún); al escribir, solo una gana el consumo.
        var a = await _verifier.VerifyAsync(owner, Google(GoogleToken(owner, nonce: nonce.Nonce)), ReauthAction.OrganizationDelete);
        var b = await _verifier.VerifyAsync(owner, Google(GoogleToken(owner, nonce: nonce.Nonce)), ReauthAction.OrganizationDelete);

        var results = await Task.WhenAll(Task.Run(() => a.TryConsume(_store)), Task.Run(() => b.TryConsume(_store)));

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public async Task PLAN0018_CA117_LogsCapturados_NoContienenNonceNiIdTokenNiContrasena()
    {
        var owner  = User.Create("logs@example.test", "Lola", "Logs", OrgRole.Owner,
            passwordHash: BCrypt.Net.BCrypt.HashPassword(Password, 4), googleId: GoogleSubject);
        var nonce  = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        var other  = _nonces.Issue(owner.UserId, ReauthAction.OrganizationTransfer);
        var valid  = GoogleToken(owner, nonce: nonce.Nonce);
        var wrong  = GoogleToken(owner, nonce: other.Nonce);
        var reject = FakeGoogleTokenValidator.RejectedToken();

        await _verifier.VerifyAsync(owner, Google(valid), ReauthAction.OrganizationDelete);
        await Record.ExceptionAsync(() => _verifier.VerifyAsync(owner, Google(wrong), ReauthAction.OrganizationDelete));
        await Record.ExceptionAsync(() => _verifier.VerifyAsync(owner, Google(reject), ReauthAction.OrganizationDelete));
        await Record.ExceptionAsync(() =>
            _verifier.VerifyAsync(owner, new ReauthCredential(WrongPassword, null), ReauthAction.OrganizationDelete));

        Assert.NotEmpty(_logs.Entries);
        foreach (var secret in new[] { nonce.Nonce, other.Nonce, valid, wrong, reject, WrongPassword, Password })
            Assert.DoesNotContain(_logs.Entries, entry => entry.Message.Contains(secret, StringComparison.Ordinal));
    }

    // ── CA-123: AuditLog por fallo y aviso al tercero ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA123_TercerFalloEnLaVentana_EncolaUnCorreoDeAvisoYNingunoAntes()
    {
        var owner = PasswordOnlyOwner();

        await AssertFailsAsync(owner, new ReauthCredential(WrongPassword, null));
        _time.Advance(TimeSpan.FromMinutes(4));
        await AssertFailsAsync(owner, new ReauthCredential(WrongPassword, null), expectedRows: 2);
        Assert.Empty(_email.Sent);

        _time.Advance(TimeSpan.FromMinutes(4));
        await AssertFailsAsync(owner, new ReauthCredential(WrongPassword, null), expectedRows: 3);

        var warning = Assert.Single(_email.Sent);
        Assert.Equal(nameof(IEmailService.SendReauthenticationFailureWarningAsync), warning.Kind);
        Assert.Equal(owner.Email, warning.ToEmail);

        // El cuarto de la misma ventana no repite el aviso.
        await AssertFailsAsync(owner, new ReauthCredential(WrongPassword, null), expectedRows: 4);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task PLAN0018_CA123_TresFallosRepartidosEnMasDe15Min_NoAvisan()
    {
        var owner = PasswordOnlyOwner();

        await AssertFailsAsync(owner, new ReauthCredential(WrongPassword, null));
        _time.Advance(TimeSpan.FromMinutes(10));
        await AssertFailsAsync(owner, new ReauthCredential(WrongPassword, null), expectedRows: 2);
        _time.Advance(TimeSpan.FromMinutes(6)); // el primero ya salió de la ventana de 15 min
        await AssertFailsAsync(owner, new ReauthCredential(WrongPassword, null), expectedRows: 3);

        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task PLAN0018_CA123_FallosDeOtroUsuario_NoCuentanParaElAviso()
    {
        var owner = PasswordOnlyOwner();
        var other = PasswordOnlyOwner();

        await AssertFailsAsync(owner, new ReauthCredential(WrongPassword, null));
        await AssertFailsAsync(other, new ReauthCredential(WrongPassword, null), expectedRows: 2);
        await AssertFailsAsync(owner, new ReauthCredential(WrongPassword, null), expectedRows: 3);

        Assert.Empty(_email.Sent);
        Assert.Equal(2, _limiter.CountFailures(AuthRateLimitPolicies.Reauth, Subject(owner)));
    }

    [Fact]
    public async Task PLAN0018_CA123_CadaFallo_DejaAuditLogConMotivoGenericoSinCredenciales()
    {
        var owner = User.Create("audit@example.test", "Ada", "Audit", OrgRole.Owner,
            passwordHash: BCrypt.Net.BCrypt.HashPassword(Password, 4), googleId: GoogleSubject);
        var organizationId = Guid.NewGuid();
        var other          = _nonces.Issue(owner.UserId, ReauthAction.OrganizationDelete);
        var token          = GoogleToken(owner, nonce: other.Nonce);

        await Record.ExceptionAsync(() => _verifier.VerifyAsync(owner, new ReauthCredential(WrongPassword, null),
            ReauthAction.OrganizationTransfer, organizationId));
        await Record.ExceptionAsync(() => _verifier.VerifyAsync(owner, Google(token),
            ReauthAction.OrganizationTransfer, organizationId));

        var rows = await _db.AuditLogs.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(ReauthenticationVerifier.AuditTable, row.TableName);
            Assert.Equal(ReauthenticationVerifier.AuditAction, row.Action);
            Assert.Equal(owner.UserId, row.UserId);
            Assert.Equal(owner.UserId, row.RecordId);
            Assert.Equal(organizationId, row.OrganizationId);
            Assert.Null(row.OldValues);
            Assert.Equal(
                $"{{\"action\":\"{ReauthAction.OrganizationTransfer}\",\"reason\":\"{ReauthenticationVerifier.AuditReason}\"}}",
                row.NewValues);
            foreach (var secret in new[] { WrongPassword, Password, token, other.Nonce })
                Assert.DoesNotContain(secret, row.NewValues!);
        });
    }

    [Fact]
    public async Task PLAN0018_CA123_CorreoDeAviso_CodificaElNombreDelUsuario()
    {
        var resend  = CapturingResend.Create();
        var service = new ResendEmailService(resend, new ConfigurationBuilder().Build());

        await service.SendReauthenticationFailureWarningAsync("owner@example.test", "<script>alert(1)</script>", Start.UtcDateTime);

        var message = Assert.Single(((CapturingResend)(object)resend).Messages);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", message.HtmlBody);
        Assert.DoesNotContain("<script>", message.HtmlBody);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task AssertFailsAsync(User user, ReauthCredential credential, int expectedRows = 1)
    {
        var error = await Record.ExceptionAsync(() => _verifier.VerifyAsync(user, credential, ReauthAction.OrganizationDelete));

        Assert.IsType<ReauthenticationFailedException>(error);
        Assert.Equal(expectedRows, await _db.AuditLogs.CountAsync());
    }

    private static ReauthCredential Google(string idToken) => new(null, idToken);

    private static AuthAttemptSubject Subject(User user) => new(user.UserId.ToString(), null, null);

    private static User GoogleOnlyOwner()
        => User.Create($"g-{Guid.NewGuid():N}@example.test", "Gina", "Google", OrgRole.Owner, googleId: GoogleSubject);

    private static User PasswordOnlyOwner()
        => User.Create($"p-{Guid.NewGuid():N}@example.test", "Pablo", "Password", OrgRole.Owner,
            passwordHash: BCrypt.Net.BCrypt.HashPassword(Password, 4));

    // id_token válido por defecto: sub del owner, email verificado, iat = ahora.
    private string GoogleToken(User owner, DateTimeOffset? iat = null, string? subject = null, bool emailVerified = true,
        string? nonce = null)
        => _google.Register(new GoogleUserInfo(subject ?? owner.GoogleId ?? GoogleSubject, owner.Email, owner.FirstName,
            owner.LastName, EmailVerified: emailVerified, IssuedAt: iat ?? _time.GetUtcNow(), Nonce: nonce));

    // Cola que entrega en el acto: el desacople del request lo prueba AuthEmailQueueTests (CA-29).
    private sealed class ImmediateEmailQueue(IEmailService email) : IAuthEmailQueue
    {
        public bool TryEnqueue(string kind, Func<IEmailService, CancellationToken, Task> send)
        {
            send(email, CancellationToken.None).GetAwaiter().GetResult();
            return true;
        }
    }

    private sealed class ThrowingGoogleValidator : IGoogleTokenValidator
    {
        public Task<GoogleUserInfo> ValidateAsync(string idToken, CancellationToken ct = default)
            => throw new HttpRequestException("certificates unavailable");
    }
}
