using IntegrationBus.Core.Events;
using IntegrationBus.External.Abstractions;
using System.Text.Json.Serialization;

namespace IntegrationBus.External.Events;

public abstract record ExternalIntegrationEvent : IntegrationEvent, IExternalIntegrationEvent
{
    [JsonInclude]
    public string ExternalSystemId { get; init; } = string.Empty;
    
    [JsonInclude]
    public string ExternalEventId { get; init; } = string.Empty;
    
    [JsonInclude]
    public Dictionary<string, object> Metadata { get; init; } = new();

    protected ExternalIntegrationEvent()
    {
    }

    protected ExternalIntegrationEvent(string externalSystemId, string externalEventId)
    {
        ExternalSystemId = externalSystemId;
        ExternalEventId = externalEventId;
    }

    protected ExternalIntegrationEvent(string externalSystemId, string externalEventId, Dictionary<string, object> metadata)
        : this(externalSystemId, externalEventId)
    {
        Metadata = metadata;
    }
}