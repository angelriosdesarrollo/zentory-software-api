using System.Net;
using Microsoft.Extensions.Configuration;
using Zentory.Domain.Common;

namespace Zentory.Infrastructure.Services;

// Qué identifica a quien intenta (PLAN-0018 RF-29, D-13). El email se normaliza al construir la clave.
public enum AuthRateLimitPartition
{
    User,
    Ip,
    Email,
    IpAndEmail,
    // Organización activa del JWT (active_org_id): el techo de invitaciones por organización (SR-0018.22).
    Organization,
}

// Una regla de una política: cuántos intentos caben en la ventana para una partición. ConfigSection es la sección de
// configuración que la sobrescribe ({ConfigSection}:PermitLimit y {ConfigSection}:Window, este como TimeSpan).
public sealed record AuthRateLimitRule(
    string                 ConfigSection,
    AuthRateLimitPartition Partition,
    int                    PermitLimit,
    TimeSpan               Window);

public sealed record AuthRateLimitPolicy(string Name, IReadOnlyList<AuthRateLimitRule> Rules);

// Quien intenta: el filtro la arma desde HttpContext (sub, active_org_id, RemoteIpAddress) y los argumentos ya enlazados
// (email).
public sealed record AuthAttemptSubject(string? UserId, string? Ip, string? Email, string? OrganizationId = null);

public readonly record struct AuthAttemptDecision(bool IsAllowed, TimeSpan RetryAfter)
{
    public static AuthAttemptDecision Allowed { get; } = new(true, TimeSpan.Zero);

    public static AuthAttemptDecision Rejected(TimeSpan retryAfter) => new(false, retryAfter);
}

// Políticas de PLAN-0018 con sus valores por defecto (D-13). Los nombres son los que usa [AuthRateLimit] y, desde T10b,
// el verificador de reautenticación. Las de los endpoints de AuthController (RF-29) llegan en T20; invitations/accept, en
// T29b.
public static class AuthRateLimitPolicies
{
    public const string Reauth               = "reauth";
    public const string OrganizationDelete   = "organization-delete";
    public const string OrganizationRestore  = "organization-restore";
    public const string OrganizationTransfer = "organization-transfer";
    public const string OrganizationInvite      = "organization-invite";
    public const string OrganizationInviteEmail = "organization-invite-email";

    // RF-29 (a)-(f): una política por endpoint público de auth (y resend-verification, con JWT).
    public const string Login              = "login";
    public const string ForgotPassword     = "forgot-password";
    public const string ResendVerification = "resend-verification";
    public const string Register           = "register";
    public const string Google             = "google";
    public const string ResetPassword      = "reset-password";
    public const string VerifyEmail        = "verify-email";
    public const string AcceptInvitation   = "invitations-accept";

    public const string AuthSection         = "RateLimiting:Auth";
    public const string OrganizationSection = "RateLimiting:Organization";

    private static readonly TimeSpan FifteenMinutes = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OneHour        = TimeSpan.FromHours(1);
    private static readonly TimeSpan OneDay         = TimeSpan.FromDays(1);

    // Las políticas de los endpoints de AuthController (RF-29), en el orden de la spec.
    public static IReadOnlyList<string> AuthEndpointPolicies { get; } =
        [Login, ForgotPassword, ResendVerification, Register, Google, ResetPassword, VerifyEmail, AcceptInvitation];

    // reauth:{sub}: 5 intentos con credencial cada 15 min, compartida por eliminar, proponer y aceptar la transferencia
    // (RF-81 f). Eliminar, restaurar y proponer la transferencia: 5 por hora por sub (SR-0018.29).
    // Auth (RF-29; SR-0018.12, SR-0018.16): login 5/15 min por IP + email y techo de 20/15 min por IP; forgot-password
    // 3/h por email y 10/h por IP; resend-verification 3/h por sub y 10/h por IP; register 3/h por IP; google 10/15 min
    // por IP; reset-password, verify-email e invitations/accept 20/15 min por IP. Se cuenta todo intento, exista o no el
    // email.
    public static IReadOnlyList<AuthRateLimitPolicy> Defaults { get; } =
    [
        new(Reauth,
            [new($"{AuthSection}:Reauth", AuthRateLimitPartition.User, 5, FifteenMinutes)]),
        new(OrganizationDelete,
            [new($"{OrganizationSection}:Delete", AuthRateLimitPartition.User, 5, OneHour)]),
        new(OrganizationRestore,
            [new($"{OrganizationSection}:Restore", AuthRateLimitPartition.User, 5, OneHour)]),
        new(OrganizationTransfer,
            [new($"{OrganizationSection}:Transfer", AuthRateLimitPartition.User, 5, OneHour)]),
        // Invitaciones (RF-49 d; SR-0018.22). Por organización: 20 intentos por día, contados por [AuthRateLimit] en todo
        // intento que pasa la policy ManageMembers. Por email destino: 3 por día en todas las organizaciones (protege el
        // buzón del invitado), consumidos por InviteMemberCommand solo cuando la invitación pasó todas las validaciones y
        // se va a encolar el correo: un intento rechazado (403, 409, 422) no gasta el cupo del buzón de la víctima.
        new(OrganizationInvite,
            [new($"{OrganizationSection}:Invite:Organization", AuthRateLimitPartition.Organization, 20, OneDay)]),
        new(OrganizationInviteEmail,
            [new($"{OrganizationSection}:Invite:Email", AuthRateLimitPartition.Email, 3, OneDay)]),
        new(Login,
        [
            new($"{AuthSection}:Login:IpAndEmail", AuthRateLimitPartition.IpAndEmail, 5, FifteenMinutes),
            new($"{AuthSection}:Login:Ip", AuthRateLimitPartition.Ip, 20, FifteenMinutes),
        ]),
        new(ForgotPassword,
        [
            new($"{AuthSection}:ForgotPassword:Email", AuthRateLimitPartition.Email, 3, OneHour),
            new($"{AuthSection}:ForgotPassword:Ip", AuthRateLimitPartition.Ip, 10, OneHour),
        ]),
        new(ResendVerification,
        [
            new($"{AuthSection}:ResendVerification:User", AuthRateLimitPartition.User, 3, OneHour),
            new($"{AuthSection}:ResendVerification:Ip", AuthRateLimitPartition.Ip, 10, OneHour),
        ]),
        new(Register,
            [new($"{AuthSection}:Register:Ip", AuthRateLimitPartition.Ip, 3, OneHour)]),
        new(Google,
            [new($"{AuthSection}:Google:Ip", AuthRateLimitPartition.Ip, 10, FifteenMinutes)]),
        new(ResetPassword,
            [new($"{AuthSection}:ResetPassword:Ip", AuthRateLimitPartition.Ip, 20, FifteenMinutes)]),
        new(VerifyEmail,
            [new($"{AuthSection}:VerifyEmail:Ip", AuthRateLimitPartition.Ip, 20, FifteenMinutes)]),
        new(AcceptInvitation,
            [new($"{AuthSection}:AcceptInvitation:Ip", AuthRateLimitPartition.Ip, 20, FifteenMinutes)]),
    ];

    // Aplica la configuración sobre los valores por defecto. Un valor que no se puede leer o no es positivo detiene el
    // arranque con la clave en el mensaje: un límite mal escrito no debe quedar en "sin límite".
    public static IReadOnlyList<AuthRateLimitPolicy> FromConfiguration(IConfiguration configuration)
        => Defaults
            .Select(policy => policy with { Rules = policy.Rules.Select(rule => Configure(rule, configuration)).ToList() })
            .ToList();

    private static AuthRateLimitRule Configure(AuthRateLimitRule rule, IConfiguration configuration)
    {
        var limitKey  = $"{rule.ConfigSection}:PermitLimit";
        var windowKey = $"{rule.ConfigSection}:Window";

        var permitLimit = rule.PermitLimit;
        var rawLimit    = configuration[limitKey];
        if (!string.IsNullOrWhiteSpace(rawLimit)
            && (!int.TryParse(rawLimit, out permitLimit) || permitLimit <= 0))
            throw new InvalidOperationException($"{limitKey} debe ser un entero positivo.");

        var window    = rule.Window;
        var rawWindow = configuration[windowKey];
        if (!string.IsNullOrWhiteSpace(rawWindow)
            && (!TimeSpan.TryParse(rawWindow, out window) || window <= TimeSpan.Zero))
            throw new InvalidOperationException($"{windowKey} debe ser un intervalo positivo (hh:mm:ss).");

        return rule with { PermitLimit = permitLimit, Window = window };
    }
}

// Limitador de intentos de autenticación (PLAN-0018 D-13, RF-29, RF-81 f y g; SR-0018.29). Ventana deslizante exacta
// (registro de instantes) en memoria del proceso (RNF-6): se pierde al reiniciar y cada instancia lleva su propia
// cuenta, aceptado para el MVP de una sola instancia. El reloj es TimeProvider (D-35) para que los tests muevan el
// tiempo sin esperar.
//
// Va aparte del rate limiter de ASP.NET (RateLimitingExtensions) porque su particionador corre antes del model binding
// y no puede leer el email del cuerpo; las políticas de IP pura podrían vivir allí, pero se dejan aquí para que todas
// las de auth compartan un mismo contrato de respuesta (429 RATE_LIMITED + Retry-After).
public sealed class AuthAttemptLimiter
{
    // Barrido de cubetas vencidas cada tantas operaciones: acota la memoria sin un temporizador.
    private const int SweepEvery = 1024;

    private readonly TimeProvider                                     _time;
    private readonly IReadOnlyDictionary<string, AuthRateLimitPolicy> _policies;
    private readonly Dictionary<string, Bucket>                       _buckets = new(StringComparer.Ordinal);
    private readonly object                                           _gate    = new();
    private int                                                       _operations;

    public AuthAttemptLimiter(TimeProvider time, IEnumerable<AuthRateLimitPolicy> policies)
    {
        _time     = time;
        _policies = policies.ToDictionary(p => p.Name, StringComparer.Ordinal);
    }

    public AuthRateLimitPolicy GetPolicy(string policyName)
        => _policies.TryGetValue(policyName, out var policy)
            ? policy
            : throw new InvalidOperationException($"Política de rate limit desconocida: {policyName}.");

    // Cuenta el intento si todas las reglas de la política tienen cupo; si alguna está llena, lo rechaza sin contarlo
    // y devuelve cuánto falta para que se libere el cupo más lejano.
    public AuthAttemptDecision TryAcquire(string policyName, AuthAttemptSubject subject)
    {
        var policy = GetPolicy(policyName);
        var now    = _time.GetUtcNow();

        lock (_gate)
        {
            MaybeSweep(now);

            var buckets    = new List<Bucket>(policy.Rules.Count);
            var retryAfter = TimeSpan.Zero;

            foreach (var rule in policy.Rules)
            {
                var bucket = GetBucket(AttemptKey(policy.Name, rule.Partition, subject), rule.Window);
                bucket.Prune(now);
                if (bucket.Count >= rule.PermitLimit)
                {
                    var wait = bucket.Oldest + rule.Window - now;
                    if (wait > retryAfter) retryAfter = wait;
                }
                buckets.Add(bucket);
            }

            if (retryAfter > TimeSpan.Zero)
                return AuthAttemptDecision.Rejected(retryAfter);

            foreach (var bucket in buckets)
                bucket.Add(now);
            return AuthAttemptDecision.Allowed;
        }
    }

    // Fallos (credencial incorrecta) dentro de la ventana de la primera regla de la política (RF-81 g: el verificador
    // avisa por correo al tercero). Devuelve el conteo después de registrar este fallo.
    public int RecordFailure(string policyName, AuthAttemptSubject subject)
    {
        var (key, window) = FailureKey(policyName, subject);
        var now = _time.GetUtcNow();

        lock (_gate)
        {
            MaybeSweep(now);
            var bucket = GetBucket(key, window);
            bucket.Prune(now);
            bucket.Add(now);
            return bucket.Count;
        }
    }

    public int CountFailures(string policyName, AuthAttemptSubject subject)
    {
        var (key, _) = FailureKey(policyName, subject);
        var now = _time.GetUtcNow();

        lock (_gate)
        {
            if (!_buckets.TryGetValue(key, out var bucket)) return 0;
            bucket.Prune(now);
            return bucket.Count;
        }
    }

    // La misma forma canónica que guarda y busca el email (RF-27, SR-0018.12): " ANA@x " y "ana@x" comparten cubeta.
    public static string NormalizeEmail(string? email) => EmailAddress.Normalize(email ?? string.Empty);

    // La IP de la clave es la de la conexión (HttpContext.Connection.RemoteIpAddress, RF-29), nunca una cabecera: la API
    // no configura ForwardedHeaders, así que X-Forwarded-For no puede cambiarla (la IP real detrás del proxy es de
    // PLAN-0002, G-5). Una IPv4 mapeada a IPv6 ("::ffff:1.2.3.4") cuenta como su IPv4, para que las dos formas de la
    // misma dirección no tengan cupos separados. Sin IP (p. ej. TestServer, que no la asigna) la clave es "-", compartida
    // por todas las peticiones sin IP: más estricto, nunca sin límite.
    public static string? NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        if (!IPAddress.TryParse(ip.Trim(), out var address)) return ip.Trim();
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    private (string Key, TimeSpan Window) FailureKey(string policyName, AuthAttemptSubject subject)
    {
        var rule = GetPolicy(policyName).Rules[0];
        return ($"failures:{AttemptKey(policyName, rule.Partition, subject)}", rule.Window);
    }

    // Sin valor (p. ej. sin sub) la partición cae en una clave compartida "-": más estricto, nunca sin límite.
    private static string AttemptKey(string policy, AuthRateLimitPartition partition, AuthAttemptSubject subject)
        => partition switch
        {
            AuthRateLimitPartition.User       => $"{policy}:user:{Value(subject.UserId)}",
            AuthRateLimitPartition.Ip         => $"{policy}:ip:{Value(NormalizeIp(subject.Ip))}",
            AuthRateLimitPartition.Email      => $"{policy}:email:{Value(NormalizeEmail(subject.Email))}",
            AuthRateLimitPartition.IpAndEmail =>
                $"{policy}:ip-email:{Value(NormalizeIp(subject.Ip))}|{Value(NormalizeEmail(subject.Email))}",
            AuthRateLimitPartition.Organization => $"{policy}:org:{Value(subject.OrganizationId)}",
            _ => throw new ArgumentOutOfRangeException(nameof(partition), partition, null),
        };

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private Bucket GetBucket(string key, TimeSpan window)
    {
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            bucket = new Bucket(window);
            _buckets[key] = bucket;
        }
        return bucket;
    }

    private void MaybeSweep(DateTimeOffset now)
    {
        if (++_operations % SweepEvery != 0) return;

        foreach (var key in _buckets.Where(pair => pair.Value.IsExpired(now)).Select(pair => pair.Key).ToList())
            _buckets.Remove(key);
    }

    private sealed class Bucket(TimeSpan window)
    {
        private readonly Queue<DateTimeOffset> _hits = new();

        public int Count => _hits.Count;

        public DateTimeOffset Oldest => _hits.Peek();

        public void Add(DateTimeOffset now) => _hits.Enqueue(now);

        public void Prune(DateTimeOffset now)
        {
            while (_hits.Count > 0 && _hits.Peek() + window <= now)
                _hits.Dequeue();
        }

        public bool IsExpired(DateTimeOffset now)
        {
            Prune(now);
            return _hits.Count == 0;
        }
    }
}
