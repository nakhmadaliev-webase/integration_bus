using IntegrationBus.Core.Abstractions;
using IntegrationBus.Examples.Events;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationBus.Examples.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IEventBus eventBus, ILogger<EventsController> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    [HttpPost("order-created")]
    public async Task<IActionResult> PublishOrderCreated([FromBody] OrderCreatedRequest request)
    {
        var orderCreatedEvent = new OrderCreatedEvent
        {
            OrderId = request.OrderId,
            CustomerName = request.CustomerName,
            TotalAmount = request.TotalAmount,
            OrderDate = DateTime.UtcNow
        };

        _logger.LogInformation("Publishing OrderCreatedEvent for order {OrderId}", request.OrderId);
        
        await _eventBus.PublishAsync(orderCreatedEvent);

        return Ok(new { Message = "Order created event published", EventId = orderCreatedEvent.Id });
    }

    [HttpPost("payment-processed")]
    public async Task<IActionResult> PublishPaymentProcessed([FromBody] PaymentProcessedRequest request)
    {
        var paymentProcessedEvent = new PaymentProcessedEvent
        {
            OrderId = request.OrderId,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            IsSuccessful = request.IsSuccessful
        };

        _logger.LogInformation("Publishing PaymentProcessedEvent for order {OrderId}", request.OrderId);
        
        await _eventBus.PublishAsync(paymentProcessedEvent);

        return Ok(new { Message = "Payment processed event published", EventId = paymentProcessedEvent.Id });
    }

    [HttpPost("inventory-updated")]
    public async Task<IActionResult> PublishInventoryUpdated([FromBody] InventoryUpdatedRequest request)
    {
        var inventoryUpdatedEvent = new InventoryUpdatedEvent
        {
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            Operation = request.Operation
        };

        _logger.LogInformation("Publishing InventoryUpdatedEvent for product {ProductId}", request.ProductId);
        
        await _eventBus.PublishAsync(inventoryUpdatedEvent);

        return Ok(new { Message = "Inventory updated event published", EventId = inventoryUpdatedEvent.Id });
    }
}

public record OrderCreatedRequest(int OrderId, string CustomerName, decimal TotalAmount);
public record PaymentProcessedRequest(int OrderId, decimal Amount, string PaymentMethod, bool IsSuccessful);
public record InventoryUpdatedRequest(int ProductId, int Quantity, string Operation);