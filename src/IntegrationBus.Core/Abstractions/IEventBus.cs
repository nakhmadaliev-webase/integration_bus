namespace IntegrationBus.Core.Abstractions;

public interface IEventBus
{
    Task PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        where TIntegrationEvent : IIntegrationEvent;

    void Subscribe<TIntegrationEvent, TIntegrationEventHandler>()
        where TIntegrationEvent : IIntegrationEvent
        where TIntegrationEventHandler : class, IIntegrationEventHandler<TIntegrationEvent>;

    void Unsubscribe<TIntegrationEvent, TIntegrationEventHandler>()
        where TIntegrationEvent : IIntegrationEvent
        where TIntegrationEventHandler : class, IIntegrationEventHandler<TIntegrationEvent>;
}