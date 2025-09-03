using IntegrationBus.Core.Abstractions;

namespace IntegrationBus.External.Abstractions;

public interface IExternalEventBus
{
    Task PublishToExternalSystemAsync<TEvent>(string systemId, TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
        
    Task PublishToExternalSystemAsync<TEvent>(string systemId, string endpoint, TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
    
    Task<TResponse> RequestFromExternalSystemAsync<TRequest, TResponse>(string systemId, string endpoint, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;
        
    void RegisterExternalSystem(string systemId, IExternalSystemClient client);
    
    Task<bool> IsExternalSystemHealthyAsync(string systemId, CancellationToken cancellationToken = default);
}