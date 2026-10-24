using Zentory.API.Extensions;
using Zentory.Application.Extensions;
using Zentory.Infrastructure.Extensions;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json: machine-level overrides, gitignored — loaded last so it wins.
// No se carga en Testing: ganaría sobre la configuración del host de pruebas (claves JWT, base).
if (!builder.Environment.IsEnvironment(AppEnvironments.Testing))
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Fail-fast antes de registrar la infraestructura: InMemory solo en Development o Testing.
builder.EnsureSafeDatabaseConfiguration();

// ── Layers ─────────────────────────────────────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── Auth ───────────────────────────────────────────────────────────────────
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAppAuthorization();

// ── Rate limiting — controllers públicos de escritura (sin auth) ────────────
builder.Services.AddPublicRateLimiting();

// ── Request-scoped services ──────────────────────────────────────────────────
builder.Services.AddRequestContext();

// ── API ─────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddSwaggerWithJwtAuth();
builder.Services.AddFrontendCors(builder.Configuration);

var app = builder.Build();

await app.SeedDevDataAsync();

app.UseAppMiddlewarePipeline();
app.ScheduleRecurringJobs();

app.Run();

// Punto de entrada visible para WebApplicationFactory<Program> en los tests de integración.
public partial class Program;
