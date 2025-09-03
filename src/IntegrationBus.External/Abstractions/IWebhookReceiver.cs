namespace IntegrationBus.External.Abstractions;

public interface IWebhookReceiver
{
    Task<bool> ValidateWebhookAsync(string systemId, string payload, string signature, CancellationToken cancellationToken = default);
    
    Task ProcessWebhookAsync(string systemId, string payload, CancellationToken cancellationToken = default);
}