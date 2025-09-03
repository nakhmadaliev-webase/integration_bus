using IntegrationBus.Core.Events;

namespace IntegrationBus.Examples.Events;

public record OrderCreatedEvent : IntegrationEvent
{
    public int OrderId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime OrderDate { get; init; }
}

public record PaymentProcessedEvent : IntegrationEvent
{
    public int OrderId { get; init; }
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public bool IsSuccessful { get; init; }
}

public record InventoryUpdatedEvent : IntegrationEvent
{
    public int ProductId { get; init; }
    public int Quantity { get; init; }
    public string Operation { get; init; } = string.Empty; // "increase" or "decrease"
}