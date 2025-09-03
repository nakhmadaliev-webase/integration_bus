namespace IntegrationBus.Core.Abstractions;

public interface IIntegrationEventHandler<in TIntegrationEvent> 
    where TIntegrationEvent : IIntegrationEvent
{
    Task HandleAsync(TIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}