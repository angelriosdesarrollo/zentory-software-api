using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Zentory.API.Authorization;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Extensions;
using Zentory.Application.PilaVerifications.Commands;
using Zentory.API.Middleware;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json: machine-level overrides, gitignored — loaded last so it wins
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ── Layers ─────────────────────────────────────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── Auth — JWT ─────────────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is required");
var collaboratorJwtKey = builder.Configuration["Jwt:CollaboratorKey"]
    ?? throw new InvalidOperationException("Jwt:CollaboratorKey is required");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    })
    // Esquema separado para la sesión del portal de colaboradores — signing key propia
    // (Jwt:CollaboratorKey) para que un JWT de un mundo nunca valide en el otro por
    // accidente, aunque el claim token_type/la policy ya lo evitarían igual.
    .AddJwtBearer("CollaboratorScheme", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(collaboratorJwtKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("EmpresaOnly", policy =>
        policy.RequireClaim("legal_type", LegalType.Empresa));

    options.AddPolicy("RequiresPro", policy =>
        policy.Requirements.Add(new MinimumPlanRequirement(Plan.Pro)));

    options.AddPolicy("RequiresStudio", policy =>
        policy.Requirements.Add(new MinimumPlanRequirement(Plan.Studio)));

    options.AddPolicy("EmpresaPro", policy =>
    {
        policy.RequireClaim("legal_type", LegalType.Empresa);
        policy.Requirements.Add(new MinimumPlanRequirement(Plan.Pro));
    });

    options.AddPolicy("EmpresaStudio", policy =>
    {
        policy.RequireClaim("legal_type", LegalType.Empresa);
        policy.Requirements.Add(new MinimumPlanRequirement(Plan.Studio));
    });

    options.AddPolicy("CollaboratorAuth", policy =>
    {
        policy.AuthenticationSchemes.Add("CollaboratorScheme");
        policy.RequireClaim("token_type", "collaborator_session");
    });
});

builder.Services.AddSingleton<IAuthorizationHandler, MinimumPlanHandler>();

// ── Rate limiting — controllers públicos de escritura (sin auth) ────────────
// A diferencia de Proposal/ProjectShare (solo lectura), PublicPilaController y
// PublicPayoutInvoicesController aceptan escritura (upload) sin sesión — el
// token es la única barrera, así que se limita por IP para frenar fuerza bruta.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("PublicUploadPolicy", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit         = 30,
                Window               = TimeSpan.FromMinutes(10),
                SegmentsPerWindow    = 5,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            }));

    // request-link dispara un correo y exchange acepta un token guessable-by-bruteforce —
    // más estricto que PublicUploadPolicy y sin cola (evita spam de bandeja de entrada).
    options.AddPolicy("CollaboratorPortalAuthPolicy", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit         = 10,
                Window               = TimeSpan.FromMinutes(10),
                SegmentsPerWindow    = 5,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            }));
});

// IMemoryCache — used by PlanLimitService to cache plan limits (10-min TTL)
builder.Services.AddMemoryCache();

// ── Tenant context — scoped per request ────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ICollaboratorPortalContext, CollaboratorPortalContext>();

// ── API ─────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Zentory API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Name         = "Authorization",
        Type         = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    c.AddSecurityRequirement(new()
    {
        [new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }]
            = Array.Empty<string>()
    });
});

// ── CORS — allow Next.js dev & prod origins ────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                builder.Configuration["Cors:AllowedOrigins"]?.Split(',')
                ?? ["http://localhost:3000"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// ── Dev seeder ─────────────────────────────────────────────────────────────
var useInMemory = app.Configuration.GetValue<bool>("Database:UseInMemory");
if (app.Environment.IsDevelopment() || useInMemory)
{
    using var scope  = app.Services.CreateScope();
    var seeder       = scope.ServiceProvider.GetRequiredService<DevDataSeeder>();
    await seeder.SeedAsync();
}

// ── Middleware pipeline ─────────────────────────────────────────────────────
app.UseMiddleware<GlobalExceptionHandlingMiddleware>(); // must be first

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHangfireDashboard("/hangfire"); // solo dev — sin auth propia todavía
}

app.UseCors("Frontend");
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Job mensual: día 25 a las 06:00 UTC, crea solicitudes PILA del período
// siguiente para las orgs empresa+studio que aún no las tienen (Fase E).
RecurringJob.AddOrUpdate<IMediator>(
    "monthly-pila-auto-request",
    mediator => mediator.Send(new RunMonthlyPilaAutoRequestCommand(), CancellationToken.None),
    "0 6 25 * *");

app.Run();
