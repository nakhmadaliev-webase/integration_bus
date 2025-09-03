namespace IntegrationBus.Core.Abstractions;

public interface IIntegrationEvent
{
    Guid Id { get; }
    DateTime OccurredOn { get; }
    string EventType { get; }
}