using IntegrationBus.Core.Abstractions;

namespace IntegrationBus.Core.Subscriptions;

public interface IEventBusSubscriptionsManager
{
    bool IsEmpty { get; }
    
    event EventHandler<string> OnEventRemoved;
    
    void AddSubscription<T, TH>()
        where T : IIntegrationEvent
        where TH : IIntegrationEventHandler<T>;
    
    void RemoveSubscription<T, TH>()
        where T : IIntegrationEvent
        where TH : IIntegrationEventHandler<T>;
    
    bool HasSubscriptionsForEvent<T>() where T : IIntegrationEvent;
    bool HasSubscriptionsForEvent(string eventName);
    
    Type? GetEventTypeByName(string eventName);
    
    IEnumerable<SubscriptionInfo> GetHandlersForEvent<T>() where T : IIntegrationEvent;
    IEnumerable<SubscriptionInfo> GetHandlersForEvent(string eventName);
    
    string GetEventKey<T>() where T : IIntegrationEvent;
    
    void Clear();
}