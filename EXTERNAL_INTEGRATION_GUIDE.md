# External Integration Bus Guide

The External Integration Bus extends your internal integration bus to communicate with external systems through HTTP APIs, webhooks, and various protocols.

## Overview

The External Integration Bus provides:
- **HTTP Client Integration**: Resilient HTTP communication with external APIs
- **Webhook Processing**: Receive and process webhooks from external systems  
- **Circuit Breakers & Retry Policies**: Resilience patterns for external communication
- **System Adapters**: Pre-built adapters for common external services
- **Event Mapping**: Convert between internal and external event formats

## Architecture Components

### Core Abstractions

```csharp
// External system client interface
public interface IExternalSystemClient
{
    string SystemId { get; }
    Task<TResponse> SendAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default);
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}

// External event bus interface
public interface IExternalEventBus
{
    Task PublishToExternalSystemAsync<TEvent>(string systemId, TEvent @event, CancellationToken cancellationToken = default);
    Task<TResponse> RequestFromExternalSystemAsync<TRequest, TResponse>(string systemId, string endpoint, TRequest request, CancellationToken cancellationToken = default);
    void RegisterExternalSystem(string systemId, IExternalSystemClient client);
}
```

### External Events

```csharp
public abstract record ExternalIntegrationEvent : IntegrationEvent, IExternalIntegrationEvent
{
    public string ExternalSystemId { get; init; } = string.Empty;
    public string ExternalEventId { get; init; } = string.Empty;
    public Dictionary<string, object> Metadata { get; init; } = new();
}
```

## Quick Start

### 1. Service Registration

```csharp
// Program.cs
builder.Services.AddExternalIntegrationBus(builder.Configuration);

// Add specific external systems
builder.Services.AddPaymentGatewayIntegration(builder.Configuration);
builder.Services.AddNotificationServiceIntegration(builder.Configuration);

// Or register custom external systems
builder.Services.AddExternalSystem<CustomAdapter>("custom-system", builder.Configuration);
```

### 2. Configuration

```json
{
  "ExternalSystems": {
    "payment-gateway": {
      "SystemId": "payment-gateway",
      "BaseUrl": "https://api.paymentgateway.com",
      "ApiKey": "your-api-key",
      "Timeout": "00:00:30",
      "RetryCount": 3,
      "CircuitBreakerThreshold": 5,
      "DefaultHeaders": {
        "X-Client-Version": "1.0"
      }
    }
  },
  "Webhooks": {
    "Systems": {
      "payment-gateway": {
        "SecretKey": "webhook-secret",
        "SignatureFormat": "HmacSha256",
        "EventTypeField": "event_type"
      }
    }
  }
}
```

### 3. Usage Examples

#### Sending Requests to External Systems

```csharp
[ApiController]
public class PaymentController : ControllerBase
{
    private readonly PaymentGatewayAdapter _paymentGateway;

    public PaymentController(PaymentGatewayAdapter paymentGateway)
    {
        _paymentGateway = paymentGateway;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequest request)
    {
        await _paymentGateway.ProcessPaymentAsync(
            request.OrderId, 
            request.Amount, 
            request.PaymentMethod);

        return Ok(new { Message = "Payment processing initiated" });
    }
}
```

#### Receiving Webhooks

```csharp
[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookReceiver _webhookReceiver;

    [HttpPost("payment-gateway")]
    public async Task<IActionResult> ReceivePaymentWebhook()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();
        var signature = Request.Headers["X-Signature"].FirstOrDefault() ?? string.Empty;

        var isValid = await _webhookReceiver.ValidateWebhookAsync("payment-gateway", payload, signature);
        if (!isValid) return Unauthorized();

        await _webhookReceiver.ProcessWebhookAsync("payment-gateway", payload);
        return Ok();
    }
}
```

## Built-in Adapters

### Payment Gateway Adapter

```csharp
public class PaymentGatewayAdapter
{
    public async Task ProcessPaymentAsync(int orderId, decimal amount, string paymentMethod);
    public async Task RefundPaymentAsync(string transactionId, decimal amount, string reason);
    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string transactionId);
}
```

**Events Generated:**
- `PaymentCompletedEvent`
- `PaymentFailedEvent`
- `PaymentRefundedEvent`

### Notification Service Adapter

```csharp
public class NotificationServiceAdapter
{
    public async Task SendEmailAsync(string to, string subject, string body);
    public async Task SendSmsAsync(string phoneNumber, string message);
    public async Task SendPushNotificationAsync(string userId, string title, string message);
}
```

**Events Generated:**
- `EmailDeliveredEvent`
- `SmsDeliveredEvent`
- `PushNotificationDeliveredEvent`

## Creating Custom Adapters

### 1. Define Your Adapter

```csharp
public class CustomExternalSystemAdapter : IExternalSystemAdapter
{
    private readonly IExternalEventBus _externalEventBus;
    private readonly ILogger<CustomExternalSystemAdapter> _logger;
    
    public string SystemId => "custom-system";

    public CustomExternalSystemAdapter(IExternalEventBus externalEventBus, ILogger<CustomExternalSystemAdapter> logger)
    {
        _externalEventBus = externalEventBus;
        _logger = logger;
    }

    public async Task CreateRecordAsync(CreateRecordRequest request)
    {
        try
        {
            var response = await _externalEventBus.RequestFromExternalSystemAsync<CreateRecordRequest, CreateRecordResponse>(
                SystemId, "/api/records", request);

            _logger.LogInformation("Record created with ID {RecordId}", response.RecordId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create record in {SystemId}", SystemId);
            throw;
        }
    }
}
```

### 2. Register Your Adapter

```csharp
// Program.cs
builder.Services.AddExternalSystem<CustomExternalSystemAdapter>("custom-system", builder.Configuration);
```

### 3. Configure Your System

```json
{
  "ExternalSystems": {
    "custom-system": {
      "SystemId": "custom-system",
      "BaseUrl": "https://api.customsystem.com",
      "ApiKey": "your-api-key",
      "Timeout": "00:01:00"
    }
  }
}
```

## Resilience Patterns

### Circuit Breaker

Automatically opens the circuit when failures exceed threshold:

```json
{
  "ExternalSystems": {
    "payment-gateway": {
      "CircuitBreakerThreshold": 5,
      "CircuitBreakerDurationMinutes": 2
    }
  }
}
```

### Retry Policies

Exponential backoff with jitter:

```json
{
  "ExternalSystems": {
    "payment-gateway": {
      "RetryCount": 3
    }
  }
}
```

### Timeout Handling

Per-request timeouts:

```json
{
  "ExternalSystems": {
    "payment-gateway": {
      "Timeout": "00:00:30"
    }
  }
}
```

## Webhook Security

### Signature Validation

Supports multiple signature formats:

```csharp
public enum WebhookSignatureFormat
{
    HmacSha256,
    HmacSha1, 
    Sha256
}
```

### Configuration

```json
{
  "Webhooks": {
    "Systems": {
      "payment-gateway": {
        "SecretKey": "your-webhook-secret",
        "SignatureFormat": "HmacSha256",
        "EventTypeField": "event_type",
        "EventIdField": "transaction_id"
      }
    }
  }
}
```

## Event Flow Examples

### Outbound Integration Flow

```
Internal Event -> External Event Bus -> HTTP Client -> External System
```

1. Internal service publishes event
2. External event bus receives it
3. Adapts event format for external system
4. Sends HTTP request with retry/circuit breaker
5. Logs success/failure

### Inbound Integration Flow  

```
External System -> Webhook -> Webhook Receiver -> Internal Event Bus
```

1. External system sends webhook
2. Webhook controller receives request
3. Validates signature
4. Processes payload into internal event
5. Publishes to internal event bus

## Monitoring and Observability

### Health Checks

```csharp
[HttpGet("health/{systemId}")]
public async Task<IActionResult> CheckHealth(string systemId)
{
    var isHealthy = await _externalEventBus.IsExternalSystemHealthyAsync(systemId);
    return Ok(new { SystemId = systemId, IsHealthy = isHealthy });
}
```

### Logging

The external integration bus provides structured logging:

```
[Information] Publishing event OrderCreatedEvent to external system payment-gateway
[Warning] Retry 2 for payment-gateway in 4000ms due to: HTTP request timeout  
[Error] Circuit breaker opened for payment-gateway for 120000ms
```

### Metrics

Track external system interactions:

- Request count by system and endpoint
- Response times
- Error rates
- Circuit breaker state changes

## Best Practices

### 1. Error Handling

```csharp
try
{
    await _paymentGateway.ProcessPaymentAsync(orderId, amount, paymentMethod);
}
catch (ExternalSystemException ex)
{
    // Handle external system specific errors
    _logger.LogError(ex, "Payment system error for order {OrderId}", orderId);
    
    // Publish compensating event if needed
    await _eventBus.PublishAsync(new PaymentFailedEvent { OrderId = orderId, Reason = ex.Message });
}
```

### 2. Idempotency

Ensure external calls are idempotent:

```csharp
public record PaymentProcessRequest
{
    public string IdempotencyKey { get; init; } = Guid.NewGuid().ToString();
    public int OrderId { get; init; }
    public decimal Amount { get; init; }
}
```

### 3. Event Mapping

Create mapping between internal and external events:

```csharp
public class PaymentEventMapper
{
    public PaymentProcessRequest MapToExternalRequest(OrderCreatedEvent internalEvent)
    {
        return new PaymentProcessRequest
        {
            OrderId = internalEvent.OrderId,
            Amount = internalEvent.TotalAmount,
            Currency = "USD",
            IdempotencyKey = internalEvent.Id.ToString()
        };
    }
}
```

### 4. Configuration Management

Use strongly-typed configuration:

```csharp
public class PaymentGatewayOptions : ExternalSystemOptions
{
    public string MerchantId { get; set; } = string.Empty;
    public bool EnableSandbox { get; set; } = false;
    public string WebhookEndpoint { get; set; } = "/webhooks/payment-gateway";
}
```

### 5. Testing

Mock external systems for testing:

```csharp
[Test]
public async Task ProcessPayment_Should_CallExternalSystem()
{
    // Arrange
    var mockClient = new Mock<IExternalSystemClient>();
    mockClient.Setup(c => c.SendAsync<PaymentProcessRequest, PaymentProcessResponse>(
        It.IsAny<string>(), It.IsAny<PaymentProcessRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PaymentProcessResponse { TransactionId = "123", Status = "Success" });

    // Act & Assert
    // Test your adapter logic
}
```

This external integration bus provides a robust foundation for integrating with external systems while maintaining the same event-driven architecture patterns as your internal microservices.