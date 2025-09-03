namespace IntegrationBus.External.Abstractions;

public interface IExternalSystemClient
{
    string SystemId { get; }
    
    Task<TResponse> SendAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;
        
    Task SendAsync<TRequest>(string endpoint, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class;
        
    Task<TResponse> GetAsync<TResponse>(string endpoint, CancellationToken cancellationToken = default)
        where TResponse : class;
        
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}