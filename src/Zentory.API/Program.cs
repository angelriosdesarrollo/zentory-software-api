using Zentory.API.Extensions;
using Zentory.Application.Extensions;
using Zentory.Infrastructure.Extensions;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json: machine-level overrides, gitignored — loaded last so it wins
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

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
