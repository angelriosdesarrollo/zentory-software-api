using Anthropic;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Resend;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Options;
using Zentory.Application.Common.Security;
using Zentory.Application.Organization.Commands;
using Zentory.Application.Organization.Services;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Interceptors;
using Zentory.Infrastructure.Persistence.Purge;
using Zentory.Infrastructure.Persistence.Repositories;
using Zentory.Infrastructure.Services;
using Zentory.Infrastructure.Documents;

namespace Zentory.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    // environment decide si Hangfire crea su esquema (RF-7): solo Development. Sin él se asume un ambiente desplegado.
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        // Reloj único (D-35): los escenarios demo, la purga y la reautenticación lo inyectan; los tests lo reemplazan.
        services.TryAddSingleton(TimeProvider.System);

        // Interceptores de EF desde DI (D-3): el DbContext usa todos los IInterceptor registrados, así un test suma el
        // suyo (p. ej. un contador de conexiones) sin reconstruir las opciones ni duplicar el de auditoría.
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<IInterceptor>(sp => sp.GetRequiredService<AuditInterceptor>());

        var useInMemory = DatabaseSettings.UseInMemory(configuration);

        if (useInMemory)
        {
            var inMemoryName = DatabaseSettings.InMemoryName(configuration);
            services.AddDbContext<ZentoryDbContext>((sp, options) =>
                options.UseInMemoryDatabase(inMemoryName)
                       .AddInterceptors(sp.GetServices<IInterceptor>()));
        }
        else
        {
            services.AddDbContext<ZentoryDbContext>((sp, options) =>
                options.UseNpgsql(
                    configuration.GetConnectionString("DefaultConnection"),
                    npgsql => npgsql.MigrationsAssembly(typeof(ZentoryDbContext).Assembly.FullName))
                       .AddInterceptors(sp.GetServices<IInterceptor>()));
        }

        services.AddScoped<IZentoryDbContext>(sp => sp.GetRequiredService<ZentoryDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Auth / Organization
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Integrations
        services.AddScoped<IOrganizationIntegrationRepository, OrganizationIntegrationRepository>();

        // Core domains
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ICollaboratorRepository, CollaboratorRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IProposalRepository, ProposalRepository>();
        services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
        services.AddScoped<ICashFlowEntryRepository, CashFlowEntryRepository>();
        services.AddScoped<IProjectTaskRepository, ProjectTaskRepository>();
        services.AddScoped<IProjectShareRepository, ProjectShareRepository>();
        services.AddScoped<IAiFeatureConfigRepository, AiFeatureConfigRepository>();
        services.AddScoped<IAiUsageLogRepository, AiUsageLogRepository>();
        services.AddScoped<IPilaVerificationRepository, PilaVerificationRepository>();
        services.AddScoped<ICollaboratorPayoutInvoiceRepository, CollaboratorPayoutInvoiceRepository>();

        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();

        // Reautenticación de las acciones irreversibles (PLAN-0018 RF-81, RF-84; D-40). El emisor del nonce es sin
        // estado (clave HKDF de Jwt:Key); el registro de consumidos es el IMemoryCache del proceso (una sola instancia).
        services.AddMemoryCache();
        services.AddSingleton(sp => new ReauthNonce(
            configuration[ReauthNonce.JwtKeySetting] ?? string.Empty, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IReauthNonceStore, InMemoryReauthNonceStore>();
        services.AddScoped<IReauthenticationVerifier, ReauthenticationVerifier>();
        services.AddScoped<ICollaboratorJwtService, CollaboratorJwtService>();
        services.AddScoped<IActivityLogService, ActivityLogService>();
        services.AddScoped<IPlanLimitService, PlanLimitService>();
        services.AddScoped<IPlanResolutionService, PlanResolutionService>();
        // Member sin finanzas (PLAN-0018 RF-66 e, D-29): lee IMembershipContext de la petición en curso.
        services.AddScoped<IFinancialVisibility, FinancialVisibility>();
        // Única lectura de membresías entre organizaciones del mismo usuario (PLAN-0018 RF-12, ADR-0008 §3).
        services.AddScoped<IUserMembershipReader, UserMembershipReader>();
        // Única lectura de las organizaciones eliminadas del propio owner (PLAN-0018 RF-72, lista blanca de ADR-0008 §3).
        services.AddScoped<IDeletedOrganizationReader, DeletedOrganizationReader>();
        services.AddScoped<DevDataSeeder>();

        services.AddOptions();

        // Plazo de restauración de las organizaciones eliminadas (PLAN-0018 RF-65 f, RF-78), validado al arrancar.
        services.AddOptions<OrganizationsOptions>()
            .Bind(configuration.GetSection(OrganizationsOptions.SectionName))
            .Validate(options => options.IsValid, OrganizationsOptions.InvalidPurgeAfterDaysMessage)
            .ValidateOnStart();

        // Retención del archivo legal (PLAN-0018 RF-79), validada al arrancar junto con el plazo de purga.
        services.AddOptions<RetentionOptions>()
            .Bind(configuration.GetSection(RetentionOptions.SectionName))
            .Validate(options => options.IsValid, RetentionOptions.InvalidLegalDocumentsYearsMessage)
            .ValidateOnStart();

        services.AddHttpClient<ResendClient>();
        services.Configure<ResendClientOptions>(o =>
        {
            o.ApiToken = configuration["Resend:ApiKey"] ?? string.Empty;
        });
        services.AddTransient<IResend, ResendClient>();
        services.AddScoped<IEmailService, ResendEmailService>();

        // Correos de identidad fuera del request y fuera de Hangfire (PLAN-0018 D-14): una sola instancia es la cola y
        // el BackgroundService que la vacía.
        services.AddSingleton<AuthEmailQueue>();
        services.AddSingleton<IAuthEmailQueue>(sp => sp.GetRequiredService<AuthEmailQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<AuthEmailQueue>());
        services.AddSingleton<IApplicationSettings, ApplicationSettings>();

        services.AddSingleton(_ => new AnthropicClient { ApiKey = configuration["Anthropic:ApiKey"] ?? string.Empty });
        services.AddScoped<IAiTextGenerationService, AnthropicTextGenerationService>();

        // Sin credenciales R2 reales configuradas localmente, cae a disco (mismo espíritu
        // que Database:UseInMemory) — así el flujo de subida/descarga completo se puede
        // probar en dev sin depender de una cuenta de Cloudflare. Nunca pasa en producción,
        // donde R2:AccountId siempre debe estar configurado.
        if (string.IsNullOrWhiteSpace(configuration["R2:AccountId"]))
            services.AddScoped<IStorageService, LocalDiskStorageService>();
        else
            services.AddScoped<IStorageService, CloudflareR2StorageService>();

        services.AddScoped<IPayoutInvoicePdfGenerator, PayoutInvoicePdfGenerator>();

        // Purga física de organizaciones eliminadas y archivo legal (PLAN-0018 RF-78, RF-79; ADR purga).
        services.AddScoped<LegalDocumentArchiver>();
        services.AddScoped<IOrganizationDataPurger, OrganizationDataPurger>();
        services.AddHttpClient<IOrganizationBrandingResolver, OrganizationBrandingResolver>();

        // Hangfire — job mensual de solicitud automática de PILA (Fase E).
        // Mismo toggle que la base de datos: en dev/in-memory el storage de jobs
        // también es en memoria (no persiste entre reinicios), en producción usa
        // la misma base Postgres.
        // Fuera de Development la API no crea ni altera el esquema hangfire (RF-7, SR-0018.4): lo hará el pipeline de
        // PLAN-0002. Las opciones se registran para que el storage y los tests usen la misma instancia.
        var storageOptions = new PostgreSqlStorageOptions
        {
            PrepareSchemaIfNecessary = environment?.IsDevelopment() ?? false,
        };
        services.AddSingleton(storageOptions);

        services.AddHangfire((sp, config) =>
        {
            config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                  .UseSimpleAssemblyNameTypeSerializer()
                  .UseRecommendedSerializerSettings();

            if (useInMemory)
                config.UseInMemoryStorage();
            else
                config.UsePostgreSqlStorage(
                    o => o.UseNpgsqlConnection(configuration.GetConnectionString("DefaultConnection")),
                    sp.GetRequiredService<PostgreSqlStorageOptions>());
        });

        // El storage se registra siempre (lo usan el dashboard y RecurringJob); el servidor que ejecuta los jobs
        // se apaga con Hangfire:ServerEnabled=false (host de tests, modo seed).
        if (HangfireSettings.IsServerEnabled(configuration))
            services.AddHangfireServer();

        return services;
    }
}
