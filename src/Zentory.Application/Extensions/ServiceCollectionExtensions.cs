using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Common.Behaviors;

namespace Zentory.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly));

        services.AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // Provisional singleton stores — replace each with a real EF repository when the domain entity exists.
        services.AddSingleton<Projects.ProjectExpenseStore>();

        return services;
    }
}
