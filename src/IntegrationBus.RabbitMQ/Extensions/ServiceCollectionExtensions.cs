using IntegrationBus.Core.Abstractions;
using IntegrationBus.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationBus.RabbitMQ.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMQEventBus(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIntegrationBusCore();
        services.Configure<RabbitMQEventBusOptions>(configuration.GetSection(RabbitMQEventBusOptions.SectionName));
        services.AddSingleton<IEventBus, RabbitMQEventBus>();
        return services;
    }

    public static IServiceCollection AddRabbitMQEventBus(this IServiceCollection services, Action<RabbitMQEventBusOptions> configureOptions)
    {
        services.AddIntegrationBusCore();
        services.Configure(configureOptions);
        services.AddSingleton<IEventBus, RabbitMQEventBus>();
        return services;
    }
}