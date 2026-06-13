using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Repositories;
using Zentory.Infrastructure.Services;

namespace Zentory.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (string.Equals(configuration["Database:UseInMemory"], "true", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<ZentoryDbContext>(options =>
                options.UseInMemoryDatabase("ZentoryDev"));
        }
        else
        {
            services.AddDbContext<ZentoryDbContext>(options =>
                options.UseNpgsql(
                    configuration.GetConnectionString("DefaultConnection"),
                    npgsql => npgsql.MigrationsAssembly(typeof(ZentoryDbContext).Assembly.FullName)));
        }

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Auth / Organization
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Core domains
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ICollaboratorRepository, CollaboratorRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IProposalRepository, ProposalRepository>();
        services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
        services.AddScoped<ICashFlowEntryRepository, CashFlowEntryRepository>();
        services.AddScoped<IProjectTaskRepository, ProjectTaskRepository>();

        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<DevDataSeeder>();

        return services;
    }
}
