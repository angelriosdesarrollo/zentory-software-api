using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Zentory.API.Authorization;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Extensions;
using Zentory.API.Middleware;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// appsettings.Local.json: machine-level overrides, gitignored — loaded last so it wins
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ── Layers ─────────────────────────────────────────────────────────────────
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ── Auth — JWT ─────────────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is required");

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
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("EmpresaOnly", policy =>
        policy.RequireClaim("account_type", AccountType.Empresa));

    options.AddPolicy("RequiresPro", policy =>
        policy.Requirements.Add(new MinimumPlanRequirement(Plan.Pro)));

    options.AddPolicy("RequiresStudio", policy =>
        policy.Requirements.Add(new MinimumPlanRequirement(Plan.Studio)));

    options.AddPolicy("EmpresaPro", policy =>
    {
        policy.RequireClaim("account_type", AccountType.Empresa);
        policy.Requirements.Add(new MinimumPlanRequirement(Plan.Pro));
    });

    options.AddPolicy("EmpresaStudio", policy =>
    {
        policy.RequireClaim("account_type", AccountType.Empresa);
        policy.Requirements.Add(new MinimumPlanRequirement(Plan.Studio));
    });
});

builder.Services.AddSingleton<IAuthorizationHandler, MinimumPlanHandler>();

// IMemoryCache — used by PlanLimitService to cache plan limits (10-min TTL)
builder.Services.AddMemoryCache();

// ── Tenant context — scoped per request ────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

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
}

app.UseCors("Frontend");
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
