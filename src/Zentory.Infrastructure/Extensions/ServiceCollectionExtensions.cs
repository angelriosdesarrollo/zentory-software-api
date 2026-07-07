using Anthropic;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resend;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Interceptors;
using Zentory.Infrastructure.Persistence.Repositories;
using Zentory.Infrastructure.Services;
using Zentory.Infrastructure.Documents;

namespace Zentory.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<AuditInterceptor>();

        var useInMemory = string.Equals(configuration["Database:UseInMemory"], "true", StringComparison.OrdinalIgnoreCase);

        if (useInMemory)
        {
            services.AddDbContext<ZentoryDbContext>((sp, options) =>
                options.UseInMemoryDatabase("ZentoryDev")
                       .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));
        }
        else
        {
            services.AddDbContext<ZentoryDbContext>((sp, options) =>
                options.UseNpgsql(
                    configuration.GetConnectionString("DefaultConnection"),
                    npgsql => npgsql.MigrationsAssembly(typeof(ZentoryDbContext).Assembly.FullName))
                       .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));
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
        services.AddScoped<ICollaboratorJwtService, CollaboratorJwtService>();
        services.AddScoped<IActivityLogService, ActivityLogService>();
        services.AddScoped<IPlanLimitService, PlanLimitService>();
        services.AddScoped<IPlanResolutionService, PlanResolutionService>();
        services.AddScoped<DevDataSeeder>();

        services.AddOptions();
        services.AddHttpClient<ResendClient>();
        services.Configure<ResendClientOptions>(o =>
        {
            o.ApiToken = configuration["Resend:ApiKey"] ?? string.Empty;
        });
        services.AddTransient<IResend, ResendClient>();
        services.AddScoped<IEmailService, ResendEmailService>();
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
        services.AddHttpClient<IOrganizationBrandingResolver, OrganizationBrandingResolver>();

        // Hangfire — job mensual de solicitud automática de PILA (Fase E).
        // Mismo toggle que la base de datos: en dev/in-memory el storage de jobs
        // también es en memoria (no persiste entre reinicios), en producción usa
        // la misma base Postgres.
        services.AddHangfire(config =>
        {
            config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                  .UseSimpleAssemblyNameTypeSerializer()
                  .UseRecommendedSerializerSettings();

            if (useInMemory)
                config.UseInMemoryStorage();
            else
                config.UsePostgreSqlStorage(o => o.UseNpgsqlConnection(configuration.GetConnectionString("DefaultConnection")));
        });
        services.AddHangfireServer();

        return services;
    }
}
