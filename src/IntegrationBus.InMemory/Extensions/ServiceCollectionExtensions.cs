using IntegrationBus.Core.Abstractions;
using IntegrationBus.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationBus.InMemory.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryEventBus(this IServiceCollection services)
    {
        services.AddIntegrationBusCore();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        return services;
    }
}