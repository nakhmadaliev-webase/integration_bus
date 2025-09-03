using IntegrationBus.Core.Abstractions;
using IntegrationBus.External.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace IntegrationBus.External.Services;

public class ExternalEventBus : IExternalEventBus
{
    private readonly ConcurrentDictionary<string, IExternalSystemClient> _clients = new();
    private readonly ILogger<ExternalEventBus> _logger;
    private readonly IEventBus _internalEventBus;

    public ExternalEventBus(ILogger<ExternalEventBus> logger, IEventBus internalEventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _internalEventBus = internalEventBus ?? throw new ArgumentNullException(nameof(internalEventBus));
    }

    public async Task PublishToExternalSystemAsync<TEvent>(string systemId, TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        var defaultEndpoint = "/events";
        await PublishToExternalSystemAsync(systemId, defaultEndpoint, @event, cancellationToken);
    }

    public async Task PublishToExternalSystemAsync<TEvent>(string systemId, string endpoint, TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        if (!_clients.TryGetValue(systemId, out var client))
        {
            throw new InvalidOperationException($"External system client '{systemId}' is not registered");
        }

        try
        {
            _logger.LogInformation("Publishing event {EventType} to external system {SystemId} at endpoint {Endpoint}",
                @event.EventType, systemId, endpoint);

            await client.SendAsync<IIntegrationEvent>(endpoint, @event, cancellationToken);

            _logger.LogInformation("Successfully published event {EventId} to external system {SystemId}",
                @event.Id, systemId);

            // Publish internal event about external system communication
            await _internalEventBus.PublishAsync(new ExternalEventPublishedEvent
            {
                ExternalSystemId = systemId,
                EventId = @event.Id,
                EventType = @event.EventType,
                Endpoint = endpoint,
                PublishedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventId} to external system {SystemId} at endpoint {Endpoint}",
                @event.Id, systemId, endpoint);

            // Publish internal event about failure
            await _internalEventBus.PublishAsync(new ExternalEventPublishFailedEvent
            {
                ExternalSystemId = systemId,
                EventId = @event.Id,
                EventType = @event.EventType,
                Endpoint = endpoint,
                ErrorMessage = ex.Message,
                FailedAt = DateTime.UtcNow
            }, cancellationToken);

            throw;
        }
    }

    public async Task<TResponse> RequestFromExternalSystemAsync<TRequest, TResponse>(string systemId, string endpoint, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        if (!_clients.TryGetValue(systemId, out var client))
        {
            throw new InvalidOperationException($"External system client '{systemId}' is not registered");
        }

        try
        {
            _logger.LogInformation("Sending request to external system {SystemId} at endpoint {Endpoint}",
                systemId, endpoint);

            var response = await client.SendAsync<TRequest, TResponse>(endpoint, request, cancellationToken);

            _logger.LogInformation("Successfully received response from external system {SystemId}",
                systemId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get response from external system {SystemId} at endpoint {Endpoint}",
                systemId, endpoint);
            throw;
        }
    }

    public void RegisterExternalSystem(string systemId, IExternalSystemClient client)
    {
        if (string.IsNullOrWhiteSpace(systemId))
        {
            throw new ArgumentException("System ID cannot be null or empty", nameof(systemId));
        }

        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        _clients.AddOrUpdate(systemId, client, (key, existingClient) =>
        {
            _logger.LogWarning("Replacing existing client for external system {SystemId}", systemId);
            if (existingClient is IDisposable disposable)
            {
                disposable.Dispose();
            }
            return client;
        });

        _logger.LogInformation("Registered external system client for {SystemId}", systemId);
    }

    public async Task<bool> IsExternalSystemHealthyAsync(string systemId, CancellationToken cancellationToken = default)
    {
        if (!_clients.TryGetValue(systemId, out var client))
        {
            _logger.LogWarning("External system client '{SystemId}' is not registered", systemId);
            return false;
        }

        try
        {
            return await client.HealthCheckAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for external system {SystemId}", systemId);
            return false;
        }
    }
}

// Internal events for tracking external system communications
public record ExternalEventPublishedEvent : IntegrationBus.Core.Events.IntegrationEvent
{
    public string ExternalSystemId { get; init; } = string.Empty;
    public Guid EventId { get; init; }
    public new string EventType { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public DateTime PublishedAt { get; init; }
}

public record ExternalEventPublishFailedEvent : IntegrationBus.Core.Events.IntegrationEvent
{
    public string ExternalSystemId { get; init; } = string.Empty;
    public Guid EventId { get; init; }
    public new string EventType { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public DateTime FailedAt { get; init; }
}