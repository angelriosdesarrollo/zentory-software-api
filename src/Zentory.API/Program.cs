using Zentory.API.Cli;
using Zentory.API.Extensions;
using Zentory.Application.Extensions;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Services;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Verbos de CLI (PLAN-0018 RF-35, SR-0018.1): bifurcan antes de crear el builder, en cualquier ambiente. Sin
// WebApplication no hay Kestrel, puerto HTTP, servidor de Hangfire ni migración o seed de arranque.
if (CliEntryPoint.IsCliInvocation(args))
    return await CliEntryPoint.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

// Validación de scopes en todo ambiente, no solo en Development (PLAN-0018 SR-0018.40): un singleton que capture un
// servicio scoped (IMembershipContext, ITenantContext) falla al resolverse en lugar de compartir la membresía entre
// peticiones.
builder.Host.UseDefaultServiceProvider(options => options.ValidateScopes = true);

// appsettings.Local.json: machine-level overrides, gitignored — loaded last so it wins.
// No se carga en Testing: ganaría sobre la configuración del host de pruebas (claves JWT, base).
if (!builder.Environment.IsEnvironment(AppEnvironments.Testing))
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Fail-fast antes de registrar la infraestructura: InMemory solo en Development o Testing, y con PostgreSQL la
// cadena de conexión es obligatoria en cualquier ambiente.
builder.EnsureSafeDatabaseConfiguration();

// Fail-fast de Google:ClientId vacío o en blanco en todo ambiente (PLAN-0018 RF-81 h, SR-0018.30 c): sin audiencia
// real, cualquier id_token de Google pasaría la validación.
GoogleTokenValidator.RequireClientId(builder.Configuration);

// ── Layers ─────────────────────────────────────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// ── Auth ───────────────────────────────────────────────────────────────────
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAppAuthorization();

// ── Rate limiting — controllers públicos de escritura (sin auth) ────────────
builder.Services.AddPublicRateLimiting();
builder.Services.AddAuthRateLimiting();

// ── Request-scoped services ──────────────────────────────────────────────────
builder.Services.AddRequestContext();

// ── API ─────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddSwaggerWithJwtAuth();
builder.Services.AddFrontendCors(builder.Configuration, builder.Environment);

var app = builder.Build();

await app.InitializeDatabaseAsync(args);

app.UseAppMiddlewarePipeline();
app.ScheduleRecurringJobs();

app.Run();
return CliExitCodes.Success;

// Punto de entrada visible para WebApplicationFactory<Program> en los tests de integración.
public partial class Program;
