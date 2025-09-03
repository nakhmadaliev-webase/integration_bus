using IntegrationBus.Core.Abstractions;

namespace IntegrationBus.External.Abstractions;

public interface IExternalIntegrationEvent : IIntegrationEvent
{
    string ExternalSystemId { get; }
    string ExternalEventId { get; }
    Dictionary<string, object> Metadata { get; }
}