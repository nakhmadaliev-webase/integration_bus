using IntegrationBus.External.Abstractions;
using IntegrationBus.External.Events;
using Microsoft.Extensions.Logging;

namespace IntegrationBus.External.Adapters;

public class PaymentGatewayAdapter : IExternalSystemAdapter
{
    private readonly IExternalEventBus _externalEventBus;
    private readonly ILogger<PaymentGatewayAdapter> _logger;
    
    public string SystemId => "payment-gateway";

    public PaymentGatewayAdapter(IExternalEventBus externalEventBus, ILogger<PaymentGatewayAdapter> logger)
    {
        _externalEventBus = externalEventBus ?? throw new ArgumentNullException(nameof(externalEventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ProcessPaymentAsync(int orderId, decimal amount, string paymentMethod, CancellationToken cancellationToken = default)
    {
        var paymentRequest = new PaymentProcessRequest
        {
            OrderId = orderId,
            Amount = amount,
            Currency = "USD",
            PaymentMethod = paymentMethod,
            Description = $"Payment for order {orderId}"
        };

        try
        {
            var response = await _externalEventBus.RequestFromExternalSystemAsync<PaymentProcessRequest, PaymentProcessResponse>(
                SystemId, "/api/payments/process", paymentRequest, cancellationToken);

            _logger.LogInformation("Payment processing initiated for order {OrderId} with transaction ID {TransactionId}",
                orderId, response.TransactionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process payment for order {OrderId}", orderId);
            throw;
        }
    }

    public async Task RefundPaymentAsync(string transactionId, decimal amount, string reason, CancellationToken cancellationToken = default)
    {
        var refundRequest = new PaymentRefundRequest
        {
            TransactionId = transactionId,
            Amount = amount,
            Reason = reason
        };

        try
        {
            var response = await _externalEventBus.RequestFromExternalSystemAsync<PaymentRefundRequest, PaymentRefundResponse>(
                SystemId, "/api/payments/refund", refundRequest, cancellationToken);

            _logger.LogInformation("Payment refund initiated for transaction {TransactionId} with refund ID {RefundId}",
                transactionId, response.RefundId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refund payment for transaction {TransactionId}", transactionId);
            throw;
        }
    }

    public async Task<PaymentStatusResponse> GetPaymentStatusAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _externalEventBus.RequestFromExternalSystemAsync<object, PaymentStatusResponse>(
                SystemId, $"/api/payments/{transactionId}/status", new { }, cancellationToken);

            _logger.LogInformation("Retrieved payment status for transaction {TransactionId}: {Status}",
                transactionId, response.Status);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get payment status for transaction {TransactionId}", transactionId);
            throw;
        }
    }
}

// Request/Response DTOs for Payment Gateway
public record PaymentProcessRequest
{
    public int OrderId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public record PaymentProcessResponse
{
    public string TransactionId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime ProcessedAt { get; init; }
    public string Message { get; init; } = string.Empty;
}

public record PaymentRefundRequest
{
    public string TransactionId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public record PaymentRefundResponse
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime RefundedAt { get; init; }
    public string Message { get; init; } = string.Empty;
}

public record PaymentStatusResponse
{
    public string TransactionId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}

// External events that can be received via webhooks
public record PaymentCompletedEvent : ExternalIntegrationEvent
{
    public string TransactionId { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public DateTime CompletedAt { get; init; }
}

public record PaymentFailedEvent : ExternalIntegrationEvent
{
    public string TransactionId { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public DateTime FailedAt { get; init; }
}

public record PaymentRefundedEvent : ExternalIntegrationEvent
{
    public string TransactionId { get; init; } = string.Empty;
    public string RefundId { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public decimal RefundAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public DateTime RefundedAt { get; init; }
}