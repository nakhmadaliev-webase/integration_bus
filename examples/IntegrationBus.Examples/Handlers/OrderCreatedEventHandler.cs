using IntegrationBus.Core.Abstractions;
using IntegrationBus.Examples.Events;

namespace IntegrationBus.Examples.Handlers;

public class OrderCreatedEventHandler : IIntegrationEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing order created event for order {OrderId}, customer: {CustomerName}, amount: {TotalAmount}",
            integrationEvent.OrderId, integrationEvent.CustomerName, integrationEvent.TotalAmount);

        await Task.Delay(1000, cancellationToken);

        _logger.LogInformation("Order {OrderId} processing completed", integrationEvent.OrderId);
    }
}

public class PaymentProcessedEventHandler : IIntegrationEventHandler<PaymentProcessedEvent>
{
    private readonly ILogger<PaymentProcessedEventHandler> _logger;

    public PaymentProcessedEventHandler(ILogger<PaymentProcessedEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(PaymentProcessedEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing payment event for order {OrderId}, amount: {Amount}, method: {PaymentMethod}, successful: {IsSuccessful}",
            integrationEvent.OrderId, integrationEvent.Amount, integrationEvent.PaymentMethod, integrationEvent.IsSuccessful);

        await Task.Delay(500, cancellationToken);

        if (integrationEvent.IsSuccessful)
        {
            _logger.LogInformation("Payment for order {OrderId} was successful", integrationEvent.OrderId);
        }
        else
        {
            _logger.LogWarning("Payment for order {OrderId} failed", integrationEvent.OrderId);
        }
    }
}

public class InventoryUpdatedEventHandler : IIntegrationEventHandler<InventoryUpdatedEvent>
{
    private readonly ILogger<InventoryUpdatedEventHandler> _logger;

    public InventoryUpdatedEventHandler(ILogger<InventoryUpdatedEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(InventoryUpdatedEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing inventory update for product {ProductId}, quantity: {Quantity}, operation: {Operation}",
            integrationEvent.ProductId, integrationEvent.Quantity, integrationEvent.Operation);

        await Task.Delay(200, cancellationToken);

        _logger.LogInformation("Inventory update for product {ProductId} completed", integrationEvent.ProductId);
    }
}