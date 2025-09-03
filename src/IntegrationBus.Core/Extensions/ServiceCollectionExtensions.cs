using IntegrationBus.Core.Abstractions;
using IntegrationBus.Core.Subscriptions;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationBus.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIntegrationBusCore(this IServiceCollection services)
    {
        services.AddSingleton<IEventBusSubscriptionsManager, InMemoryEventBusSubscriptionsManager>();
        return services;
    }

    public static IServiceCollection AddIntegrationEventHandler<THandler, TEvent>(this IServiceCollection services)
        where THandler : class, IIntegrationEventHandler<TEvent>
        where TEvent : IIntegrationEvent
    {
        services.AddTransient<THandler>();
        return services;
    }
}