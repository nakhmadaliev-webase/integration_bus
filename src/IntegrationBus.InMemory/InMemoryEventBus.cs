using IntegrationBus.Core.Abstractions;
using IntegrationBus.Core.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IntegrationBus.InMemory;

public class InMemoryEventBus : IEventBus, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBusSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<InMemoryEventBus> _logger;

    public InMemoryEventBus(
        IServiceProvider serviceProvider,
        IEventBusSubscriptionsManager subscriptionsManager,
        ILogger<InMemoryEventBus> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _subscriptionsManager = subscriptionsManager ?? new InMemoryEventBusSubscriptionsManager();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        where TIntegrationEvent : IIntegrationEvent
    {
        var eventName = _subscriptionsManager.GetEventKey<TIntegrationEvent>();
        
        _logger.LogTrace("Publishing event {EventName} with ID {EventId}", eventName, integrationEvent.Id);

        if (!_subscriptionsManager.HasSubscriptionsForEvent<TIntegrationEvent>())
        {
            _logger.LogTrace("No subscriptions found for event {EventName}", eventName);
            return;
        }

        await ProcessEvent(eventName, integrationEvent, cancellationToken);
    }

    public void Subscribe<TIntegrationEvent, TIntegrationEventHandler>()
        where TIntegrationEvent : IIntegrationEvent
        where TIntegrationEventHandler : class, IIntegrationEventHandler<TIntegrationEvent>
    {
        var eventName = _subscriptionsManager.GetEventKey<TIntegrationEvent>();
        
        _logger.LogInformation("Subscribing to event {EventName} with {EventHandler}", eventName, typeof(TIntegrationEventHandler).GetGenericTypeName());
        
        _subscriptionsManager.AddSubscription<TIntegrationEvent, TIntegrationEventHandler>();
    }

    public void Unsubscribe<TIntegrationEvent, TIntegrationEventHandler>()
        where TIntegrationEvent : IIntegrationEvent
        where TIntegrationEventHandler : class, IIntegrationEventHandler<TIntegrationEvent>
    {
        var eventName = _subscriptionsManager.GetEventKey<TIntegrationEvent>();
        
        _logger.LogInformation("Unsubscribing from event {EventName} with {EventHandler}", eventName, typeof(TIntegrationEventHandler).GetGenericTypeName());
        
        _subscriptionsManager.RemoveSubscription<TIntegrationEvent, TIntegrationEventHandler>();
    }

    private async Task ProcessEvent<TIntegrationEvent>(string eventName, TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEvent
    {
        _logger.LogTrace("Processing event {EventName}", eventName);

        var subscriptions = _subscriptionsManager.GetHandlersForEvent<TIntegrationEvent>();
        
        foreach (var subscription in subscriptions)
        {
            await ProcessEventHandler(subscription, integrationEvent, cancellationToken);
        }
    }

    private async Task ProcessEventHandler<TIntegrationEvent>(SubscriptionInfo subscription, TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEvent
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetService(subscription.HandlerType);
            
            if (handler == null)
            {
                _logger.LogWarning("No handler found for {EventHandler}", subscription.HandlerType.GetGenericTypeName());
                return;
            }

            var eventType = integrationEvent.GetType();
            var handlerMethod = subscription.HandlerType.GetMethod(nameof(IIntegrationEventHandler<TIntegrationEvent>.HandleAsync));
            
            if (handlerMethod == null)
            {
                _logger.LogWarning("HandleAsync method not found on {EventHandler}", subscription.HandlerType.GetGenericTypeName());
                return;
            }

            await (Task)handlerMethod.Invoke(handler, new object[] { integrationEvent, cancellationToken })!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event {EventName} with handler {EventHandler}", 
                integrationEvent.EventType, subscription.HandlerType.GetGenericTypeName());
            throw;
        }
    }

    public void Dispose()
    {
        _subscriptionsManager?.Clear();
    }
}

public static class GenericTypeExtensions
{
    public static string GetGenericTypeName(this Type type)
    {
        if (type.IsGenericType)
        {
            var genericTypes = string.Join(",", type.GetGenericArguments().Select(t => t.Name).ToArray());
            return $"{type.Name.Remove(type.Name.IndexOf('`'))}<{genericTypes}>";
        }

        return type.Name;
    }

    public static string GetGenericTypeName(this object @object)
    {
        return @object.GetType().GetGenericTypeName();
    }
}