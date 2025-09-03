# Integration Bus for ASP.NET Core Microservices

A lightweight, extensible integration bus implementation for ASP.NET Core microservices that supports both in-memory and RabbitMQ message brokers.

## Architecture Overview

The integration bus follows a clean architecture pattern with the following components:

### Core Components

- **IntegrationBus.Core**: Contains abstractions and base implementations
- **IntegrationBus.InMemory**: In-memory implementation for development/testing
- **IntegrationBus.RabbitMQ**: RabbitMQ implementation for production

### Key Abstractions

- `IIntegrationEvent`: Base contract for all integration events
- `IIntegrationEventHandler<T>`: Handler contract for processing events
- `IEventBus`: Main event bus interface for publishing and subscribing
- `IEventBusSubscriptionsManager`: Manages event subscriptions

## Implementation Flow

### 1. Event Definition
```csharp
public record OrderCreatedEvent : IntegrationEvent
{
    public int OrderId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime OrderDate { get; init; }
}
```

### 2. Event Handler Implementation
```csharp
public class OrderCreatedEventHandler : IIntegrationEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing order {OrderId} for customer {CustomerName}",
            integrationEvent.OrderId, integrationEvent.CustomerName);
        
        // Process the event
        await ProcessOrderAsync(integrationEvent, cancellationToken);
    }
}
```

### 3. Service Registration (Program.cs)

#### Option A: In-Memory (Development/Testing)
```csharp
// Add Integration Bus services
builder.Services.AddInMemoryEventBus();

// Register event handlers
builder.Services.AddIntegrationEventHandler<OrderCreatedEventHandler, OrderCreatedEvent>();

// Subscribe to events after building the app
var app = builder.Build();
var eventBus = app.Services.GetRequiredService<IEventBus>();
eventBus.Subscribe<OrderCreatedEvent, OrderCreatedEventHandler>();
```

#### Option B: RabbitMQ (Production)
```csharp
// Add Integration Bus services with RabbitMQ
builder.Services.AddRabbitMQEventBus(builder.Configuration);

// Register event handlers
builder.Services.AddIntegrationEventHandler<OrderCreatedEventHandler, OrderCreatedEvent>();

// Subscribe to events
var app = builder.Build();
var eventBus = app.Services.GetRequiredService<IEventBus>();
eventBus.Subscribe<OrderCreatedEvent, OrderCreatedEventHandler>();
```

### 4. Configuration (appsettings.json for RabbitMQ)
```json
{
  "RabbitMQ": {
    "Connection": "localhost",
    "UserName": "guest",
    "Password": "guest",
    "QueueName": "your_service_queue",
    "RetryCount": 5
  }
}
```

### 5. Publishing Events
```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IEventBus _eventBus;

    public OrdersController(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        // Create and save order
        var order = await CreateOrderAsync(request);

        // Publish integration event
        var orderCreatedEvent = new OrderCreatedEvent
        {
            OrderId = order.Id,
            CustomerName = order.CustomerName,
            TotalAmount = order.TotalAmount,
            OrderDate = order.CreatedAt
        };

        await _eventBus.PublishAsync(orderCreatedEvent);

        return Ok(order);
    }
}
```

## Microservice Integration Patterns

### 1. Event-Driven Communication
```
[Order Service] --OrderCreatedEvent--> [Payment Service]
                                   --> [Inventory Service]
                                   --> [Notification Service]
```

### 2. Saga Pattern Implementation
```csharp
public class OrderSagaHandler : 
    IIntegrationEventHandler<OrderCreatedEvent>,
    IIntegrationEventHandler<PaymentProcessedEvent>,
    IIntegrationEventHandler<InventoryReservedEvent>
{
    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // Step 1: Process payment
        await _eventBus.PublishAsync(new ProcessPaymentCommand { OrderId = @event.OrderId });
    }

    public async Task HandleAsync(PaymentProcessedEvent @event, CancellationToken cancellationToken)
    {
        if (@event.IsSuccessful)
        {
            // Step 2: Reserve inventory
            await _eventBus.PublishAsync(new ReserveInventoryCommand { OrderId = @event.OrderId });
        }
        else
        {
            // Compensate: Cancel order
            await _eventBus.PublishAsync(new CancelOrderCommand { OrderId = @event.OrderId });
        }
    }

    public async Task HandleAsync(InventoryReservedEvent @event, CancellationToken cancellationToken)
    {
        // Step 3: Confirm order
        await _eventBus.PublishAsync(new ConfirmOrderCommand { OrderId = @event.OrderId });
    }
}
```

### 3. Cross-Service Data Consistency
```csharp
public class UserProfileUpdatedEventHandler : IIntegrationEventHandler<UserProfileUpdatedEvent>
{
    private readonly IOrderRepository _orderRepository;

    public async Task HandleAsync(UserProfileUpdatedEvent @event, CancellationToken cancellationToken)
    {
        // Update cached user data in order service
        await _orderRepository.UpdateCustomerInfoAsync(@event.UserId, @event.CustomerName, @event.Email);
    }
}
```

## Best Practices

### 1. Event Design
- Events should be immutable (use records)
- Include all necessary data to avoid service coupling
- Use versioning for event evolution
- Keep events focused and specific

### 2. Error Handling
```csharp
public class ResilientEventHandler : IIntegrationEventHandler<MyEvent>
{
    public async Task HandleAsync(MyEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessEventAsync(@event);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process event {EventId}", @event.Id);
            
            // Implement retry logic or dead letter queue
            await HandleEventFailureAsync(@event, ex);
            throw;
        }
    }
}
```

### 3. Testing
```csharp
[Test]
public async Task Should_Handle_Order_Created_Event()
{
    // Arrange
    var serviceProvider = new ServiceCollection()
        .AddLogging()
        .AddInMemoryEventBus()
        .AddIntegrationEventHandler<OrderCreatedEventHandler, OrderCreatedEvent>()
        .BuildServiceProvider();

    var eventBus = serviceProvider.GetRequiredService<IEventBus>();
    eventBus.Subscribe<OrderCreatedEvent, OrderCreatedEventHandler>();

    var orderEvent = new OrderCreatedEvent
    {
        OrderId = 123,
        CustomerName = "John Doe",
        TotalAmount = 100.00m
    };

    // Act
    await eventBus.PublishAsync(orderEvent);

    // Assert - verify side effects
}
```

### 4. Monitoring and Observability
```csharp
public class ObservableEventHandler<T> : IIntegrationEventHandler<T> where T : IIntegrationEvent
{
    private readonly IIntegrationEventHandler<T> _inner;
    private readonly IMetrics _metrics;

    public async Task HandleAsync(T integrationEvent, CancellationToken cancellationToken)
    {
        using var activity = Activity.StartActivity($"Handle-{typeof(T).Name}");
        activity?.SetTag("event.id", integrationEvent.Id.ToString());
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            await _inner.HandleAsync(integrationEvent, cancellationToken);
            _metrics.Counter("events.processed").Add(1, new("event_type", typeof(T).Name), new("status", "success"));
        }
        catch (Exception)
        {
            _metrics.Counter("events.processed").Add(1, new("event_type", typeof(T).Name), new("status", "error"));
            throw;
        }
        finally
        {
            _metrics.Histogram("events.processing_time").Record(stopwatch.ElapsedMilliseconds, new("event_type", typeof(T).Name));
        }
    }
}
```

## Project Structure

```
integration_bus/
├── src/
│   ├── IntegrationBus.Core/           # Core abstractions and base classes
│   │   ├── Abstractions/              # Interfaces
│   │   ├── Events/                    # Base event classes
│   │   ├── Subscriptions/             # Subscription management
│   │   └── Extensions/                # DI extensions
│   ├── IntegrationBus.InMemory/       # In-memory implementation
│   └── IntegrationBus.RabbitMQ/       # RabbitMQ implementation
├── examples/
│   └── IntegrationBus.Examples/       # Sample implementation
└── README.md
```

## Getting Started

1. **Clone the repository**
2. **Choose your message broker**:
   - For development: Use `AddInMemoryEventBus()`
   - For production: Use `AddRabbitMQEventBus(configuration)`
3. **Define your integration events** inheriting from `IntegrationEvent`
4. **Create event handlers** implementing `IIntegrationEventHandler<T>`
5. **Register services** in `Program.cs`
6. **Subscribe to events** after building the application

## Running the Example

```bash
cd examples/IntegrationBus.Examples
dotnet run
```

Navigate to `https://localhost:7xxx/swagger` to test the API endpoints.

The example includes three types of events:
- Order Created Events
- Payment Processed Events  
- Inventory Updated Events

Each demonstrates different aspects of the integration bus functionality.