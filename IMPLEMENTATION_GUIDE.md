# Integration Bus Implementation Guide

This guide provides step-by-step instructions for implementing the integration bus in your ASP.NET Core microservices.

## Step 1: Project Setup

### 1.1 Add Package References

For each microservice that will use the integration bus, add the appropriate package references:

```xml
<!-- Core package (always required) -->
<PackageReference Include="IntegrationBus.Core" Version="1.0.0" />

<!-- Choose one implementation -->
<PackageReference Include="IntegrationBus.InMemory" Version="1.0.0" />
<!-- OR -->
<PackageReference Include="IntegrationBus.RabbitMQ" Version="1.0.0" />
```

### 1.2 Project Structure Recommendation

```
YourMicroservice/
├── Events/
│   ├── IntegrationEvents/     # Events this service publishes
│   └── ExternalEvents/        # Events from other services this service subscribes to
├── Handlers/
│   └── IntegrationEventHandlers/
├── Services/
└── Controllers/
```

## Step 2: Define Integration Events

### 2.1 Create Integration Events

Events should represent business facts that have occurred:

```csharp
// Events/IntegrationEvents/OrderCreatedEvent.cs
using IntegrationBus.Core.Events;

namespace OrderService.Events.IntegrationEvents;

public record OrderCreatedEvent : IntegrationEvent
{
    public int OrderId { get; init; }
    public string CustomerEmail { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime OrderDate { get; init; }
    public List<OrderItem> Items { get; init; } = new();
}

public record OrderItem
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Price { get; init; }
}
```

### 2.2 External Events (from other services)

```csharp
// Events/ExternalEvents/PaymentProcessedEvent.cs
using IntegrationBus.Core.Events;

namespace OrderService.Events.ExternalEvents;

// This event comes from Payment Service
public record PaymentProcessedEvent : IntegrationEvent
{
    public int OrderId { get; init; }
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public bool IsSuccessful { get; init; }
    public string? FailureReason { get; init; }
}
```

## Step 3: Implement Event Handlers

### 3.1 Create Event Handlers

```csharp
// Handlers/IntegrationEventHandlers/PaymentProcessedEventHandler.cs
using IntegrationBus.Core.Abstractions;
using OrderService.Events.ExternalEvents;

namespace OrderService.Handlers.IntegrationEventHandlers;

public class PaymentProcessedEventHandler : IIntegrationEventHandler<PaymentProcessedEvent>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<PaymentProcessedEventHandler> _logger;
    private readonly IEventBus _eventBus;

    public PaymentProcessedEventHandler(
        IOrderRepository orderRepository,
        ILogger<PaymentProcessedEventHandler> logger,
        IEventBus eventBus)
    {
        _orderRepository = orderRepository;
        _logger = logger;
        _eventBus = eventBus;
    }

    public async Task HandleAsync(PaymentProcessedEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing payment result for order {OrderId}", integrationEvent.OrderId);

        var order = await _orderRepository.GetByIdAsync(integrationEvent.OrderId);
        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found", integrationEvent.OrderId);
            return;
        }

        if (integrationEvent.IsSuccessful)
        {
            order.MarkAsConfirmed();
            await _orderRepository.UpdateAsync(order);

            // Publish order confirmed event
            var orderConfirmedEvent = new OrderConfirmedEvent
            {
                OrderId = order.Id,
                CustomerEmail = order.CustomerEmail,
                ConfirmedAt = DateTime.UtcNow
            };

            await _eventBus.PublishAsync(orderConfirmedEvent, cancellationToken);
            
            _logger.LogInformation("Order {OrderId} confirmed successfully", order.Id);
        }
        else
        {
            order.MarkAsFailed(integrationEvent.FailureReason);
            await _orderRepository.UpdateAsync(order);
            
            _logger.LogWarning("Order {OrderId} payment failed: {Reason}", order.Id, integrationEvent.FailureReason);
        }
    }
}
```

## Step 4: Service Registration

### 4.1 Register Services in Program.cs

```csharp
using IntegrationBus.Core.Extensions;
using IntegrationBus.RabbitMQ.Extensions;
using OrderService.Events.ExternalEvents;
using OrderService.Events.IntegrationEvents;
using OrderService.Handlers.IntegrationEventHandlers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Integration Bus
builder.Services.AddRabbitMQEventBus(builder.Configuration);

// Register repositories and services
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();

// Register event handlers
builder.Services.AddIntegrationEventHandler<PaymentProcessedEventHandler, PaymentProcessedEvent>();
builder.Services.AddIntegrationEventHandler<InventoryReservedEventHandler, InventoryReservedEvent>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Subscribe to events
var eventBus = app.Services.GetRequiredService<IEventBus>();
eventBus.Subscribe<PaymentProcessedEvent, PaymentProcessedEventHandler>();
eventBus.Subscribe<InventoryReservedEvent, InventoryReservedEventHandler>();

app.Run();
```

### 4.2 Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=OrderServiceDb;Trusted_Connection=true;MultipleActiveResultSets=true"
  },
  "RabbitMQ": {
    "Connection": "localhost",
    "UserName": "guest",
    "Password": "guest",
    "QueueName": "order_service_queue",
    "RetryCount": 5
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "IntegrationBus": "Debug"
    }
  }
}
```

## Step 5: Publishing Events

### 5.1 In Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderService orderService,
        IEventBus eventBus,
        ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _eventBus = eventBus;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        try
        {
            // Create order
            var order = await _orderService.CreateOrderAsync(request);

            // Publish integration event
            var orderCreatedEvent = new OrderCreatedEvent
            {
                OrderId = order.Id,
                CustomerEmail = order.CustomerEmail,
                CustomerName = order.CustomerName,
                TotalAmount = order.TotalAmount,
                OrderDate = order.CreatedAt,
                Items = order.Items.Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    Quantity = i.Quantity,
                    Price = i.Price
                }).ToList()
            };

            await _eventBus.PublishAsync(orderCreatedEvent);

            _logger.LogInformation("Order {OrderId} created and event published", order.Id);

            return Ok(order.ToDto());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order");
            return StatusCode(500, "Internal server error");
        }
    }
}
```

### 5.2 In Domain Services

```csharp
public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IEventBus _eventBus;

    public async Task<Order> ProcessOrderPaymentAsync(int orderId, ProcessPaymentRequest request)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null) throw new OrderNotFoundException(orderId);

        order.ProcessPayment(request.PaymentMethod, request.Amount);
        await _orderRepository.UpdateAsync(order);

        // Publish domain event as integration event
        var paymentRequestedEvent = new PaymentRequestedEvent
        {
            OrderId = order.Id,
            Amount = order.TotalAmount,
            PaymentMethod = request.PaymentMethod,
            CustomerEmail = order.CustomerEmail
        };

        await _eventBus.PublishAsync(paymentRequestedEvent);

        return order;
    }
}
```

## Step 6: Error Handling and Resilience

### 6.1 Implement Retry Logic in Handlers

```csharp
public class ResilientPaymentProcessedEventHandler : IIntegrationEventHandler<PaymentProcessedEvent>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<ResilientPaymentProcessedEventHandler> _logger;
    private static readonly Random _random = new();

    public async Task HandleAsync(PaymentProcessedEvent integrationEvent, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        var attempt = 0;

        while (attempt < maxRetries)
        {
            try
            {
                await ProcessEventAsync(integrationEvent, cancellationToken);
                return; // Success, exit
            }
            catch (TransientException ex)
            {
                attempt++;
                if (attempt >= maxRetries)
                {
                    _logger.LogError(ex, "Failed to process payment event after {Attempts} attempts for order {OrderId}", 
                        maxRetries, integrationEvent.OrderId);
                    throw;
                }

                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 1000 + _random.Next(0, 1000));
                _logger.LogWarning("Attempt {Attempt} failed for order {OrderId}, retrying in {Delay}ms", 
                    attempt, integrationEvent.OrderId, delay.TotalMilliseconds);
                
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task ProcessEventAsync(PaymentProcessedEvent integrationEvent, CancellationToken cancellationToken)
    {
        // Original processing logic here
    }
}
```

### 6.2 Dead Letter Queue Pattern

```csharp
public class DeadLetterEventHandler<T> : IIntegrationEventHandler<T> where T : IIntegrationEvent
{
    private readonly IIntegrationEventHandler<T> _innerHandler;
    private readonly IDeadLetterService _deadLetterService;
    private readonly ILogger<DeadLetterEventHandler<T>> _logger;

    public async Task HandleAsync(T integrationEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _innerHandler.HandleAsync(integrationEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event {EventType} with ID {EventId} failed processing, sending to dead letter queue", 
                typeof(T).Name, integrationEvent.Id);
            
            await _deadLetterService.SendToDeadLetterAsync(integrationEvent, ex.Message);
            
            // Don't rethrow to prevent infinite retry
        }
    }
}
```

## Step 7: Testing

### 7.1 Unit Testing Event Handlers

```csharp
[TestClass]
public class PaymentProcessedEventHandlerTests
{
    private Mock<IOrderRepository> _orderRepositoryMock;
    private Mock<IEventBus> _eventBusMock;
    private Mock<ILogger<PaymentProcessedEventHandler>> _loggerMock;
    private PaymentProcessedEventHandler _handler;

    [TestInitialize]
    public void Setup()
    {
        _orderRepositoryMock = new Mock<IOrderRepository>();
        _eventBusMock = new Mock<IEventBus>();
        _loggerMock = new Mock<ILogger<PaymentProcessedEventHandler>>();
        
        _handler = new PaymentProcessedEventHandler(
            _orderRepositoryMock.Object,
            _loggerMock.Object,
            _eventBusMock.Object);
    }

    [TestMethod]
    public async Task HandleAsync_SuccessfulPayment_ShouldConfirmOrder()
    {
        // Arrange
        var orderId = 123;
        var order = new Order { Id = orderId, Status = OrderStatus.Pending };
        var paymentEvent = new PaymentProcessedEvent
        {
            OrderId = orderId,
            IsSuccessful = true,
            Amount = 100.00m
        };

        _orderRepositoryMock.Setup(r => r.GetByIdAsync(orderId))
            .ReturnsAsync(order);

        // Act
        await _handler.HandleAsync(paymentEvent);

        // Assert
        Assert.AreEqual(OrderStatus.Confirmed, order.Status);
        _orderRepositoryMock.Verify(r => r.UpdateAsync(order), Times.Once);
        _eventBusMock.Verify(e => e.PublishAsync(It.IsAny<OrderConfirmedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

### 7.2 Integration Testing

```csharp
[TestClass]
public class IntegrationBusIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public IntegrationBusIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [TestMethod]
    public async Task CreateOrder_ShouldPublishIntegrationEvent()
    {
        // Arrange
        var request = new CreateOrderRequest
        {
            CustomerName = "John Doe",
            CustomerEmail = "john@example.com",
            Items = new[]
            {
                new { ProductId = 1, Quantity = 2, Price = 25.00m }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        response.EnsureSuccessStatusCode();
        
        // Verify event was published (this would require a test event bus or message capture)
        // Implementation depends on your testing strategy
    }
}
```

## Step 8: Monitoring and Observability

### 8.1 Add Metrics

```csharp
public class MetricsEventHandler<T> : IIntegrationEventHandler<T> where T : IIntegrationEvent
{
    private readonly IIntegrationEventHandler<T> _innerHandler;
    private readonly IMetrics _metrics;

    public async Task HandleAsync(T integrationEvent, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var eventType = typeof(T).Name;

        try
        {
            await _innerHandler.HandleAsync(integrationEvent, cancellationToken);
            
            _metrics.Counter("integration_events_processed_total")
                .WithTag("event_type", eventType)
                .WithTag("status", "success")
                .Increment();
        }
        catch (Exception)
        {
            _metrics.Counter("integration_events_processed_total")
                .WithTag("event_type", eventType)
                .WithTag("status", "error")
                .Increment();
            throw;
        }
        finally
        {
            _metrics.Histogram("integration_event_processing_duration_milliseconds")
                .WithTag("event_type", eventType)
                .Record(stopwatch.ElapsedMilliseconds);
        }
    }
}
```

### 8.2 Add Distributed Tracing

```csharp
public class TracingEventHandler<T> : IIntegrationEventHandler<T> where T : IIntegrationEvent
{
    private readonly IIntegrationEventHandler<T> _innerHandler;
    private static readonly ActivitySource ActivitySource = new("IntegrationBus");

    public async Task HandleAsync(T integrationEvent, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity($"IntegrationEvent.Handle.{typeof(T).Name}");
        
        activity?.SetTag("event.id", integrationEvent.Id.ToString());
        activity?.SetTag("event.type", integrationEvent.EventType);
        activity?.SetTag("event.occurred_on", integrationEvent.OccurredOn.ToString("O"));

        try
        {
            await _innerHandler.HandleAsync(integrationEvent, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

## Common Patterns and Best Practices

### 1. Event Sourcing Integration
```csharp
public class EventSourcingOrderService : IOrderService
{
    private readonly IEventStore _eventStore;
    private readonly IEventBus _integrationEventBus;

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        var order = Order.Create(request);
        
        // Save domain events to event store
        await _eventStore.SaveEventsAsync(order.Id, order.GetUncommittedEvents());
        
        // Publish integration events
        foreach (var domainEvent in order.GetUncommittedEvents())
        {
            var integrationEvent = MapToIntegrationEvent(domainEvent);
            await _integrationEventBus.PublishAsync(integrationEvent);
        }
        
        order.MarkEventsAsCommitted();
        return order;
    }
}
```

### 2. Inbox Pattern for Idempotency
```csharp
public class IdempotentEventHandler<T> : IIntegrationEventHandler<T> where T : IIntegrationEvent
{
    private readonly IIntegrationEventHandler<T> _innerHandler;
    private readonly IEventInboxRepository _inboxRepository;

    public async Task HandleAsync(T integrationEvent, CancellationToken cancellationToken)
    {
        // Check if event was already processed
        if (await _inboxRepository.ExistsAsync(integrationEvent.Id))
        {
            return; // Already processed, skip
        }

        // Mark as processing
        await _inboxRepository.AddAsync(integrationEvent.Id, InboxStatus.Processing);

        try
        {
            await _innerHandler.HandleAsync(integrationEvent, cancellationToken);
            
            // Mark as completed
            await _inboxRepository.UpdateStatusAsync(integrationEvent.Id, InboxStatus.Completed);
        }
        catch (Exception)
        {
            // Mark as failed
            await _inboxRepository.UpdateStatusAsync(integrationEvent.Id, InboxStatus.Failed);
            throw;
        }
    }
}
```

This implementation guide provides a comprehensive approach to integrating the event bus into your microservices architecture. Start with the basic implementation and gradually add the advanced patterns as your system grows in complexity.