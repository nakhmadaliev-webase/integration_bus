using IntegrationBus.Core.Abstractions;
using System.Text.Json.Serialization;

namespace IntegrationBus.Core.Events;

public abstract record IntegrationEvent : IIntegrationEvent
{
    [JsonInclude]
    public Guid Id { get; init; } = Guid.NewGuid();
    
    [JsonInclude]
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    
    [JsonInclude]
    public string EventType { get; init; }

    protected IntegrationEvent()
    {
        EventType = GetType().Name;
    }

    protected IntegrationEvent(Guid id, DateTime occurredOn)
    {
        Id = id;
        OccurredOn = occurredOn;
        EventType = GetType().Name;
    }
}