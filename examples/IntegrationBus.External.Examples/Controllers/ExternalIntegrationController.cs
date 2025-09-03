using IntegrationBus.External.Abstractions;
using IntegrationBus.External.Adapters;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationBus.External.Examples.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExternalIntegrationController : ControllerBase
{
    private readonly IExternalEventBus _externalEventBus;
    private readonly PaymentGatewayAdapter _paymentGateway;
    private readonly NotificationServiceAdapter _notificationService;
    private readonly ILogger<ExternalIntegrationController> _logger;

    public ExternalIntegrationController(
        IExternalEventBus externalEventBus,
        PaymentGatewayAdapter paymentGateway,
        NotificationServiceAdapter notificationService,
        ILogger<ExternalIntegrationController> logger)
    {
        _externalEventBus = externalEventBus;
        _paymentGateway = paymentGateway;
        _notificationService = notificationService;
        _logger = logger;
    }

    [HttpPost("payment/process")]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequest request)
    {
        try
        {
            _logger.LogInformation("Processing payment for order {OrderId}", request.OrderId);
            
            await _paymentGateway.ProcessPaymentAsync(
                request.OrderId, 
                request.Amount, 
                request.PaymentMethod);

            return Ok(new { Message = "Payment processing initiated", OrderId = request.OrderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process payment for order {OrderId}", request.OrderId);
            return StatusCode(500, new { Error = "Payment processing failed", Message = ex.Message });
        }
    }

    [HttpPost("payment/refund")]
    public async Task<IActionResult> RefundPayment([FromBody] RefundPaymentRequest request)
    {
        try
        {
            _logger.LogInformation("Processing refund for transaction {TransactionId}", request.TransactionId);
            
            await _paymentGateway.RefundPaymentAsync(
                request.TransactionId, 
                request.Amount, 
                request.Reason);

            return Ok(new { Message = "Refund processing initiated", TransactionId = request.TransactionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process refund for transaction {TransactionId}", request.TransactionId);
            return StatusCode(500, new { Error = "Refund processing failed", Message = ex.Message });
        }
    }

    [HttpGet("payment/status/{transactionId}")]
    public async Task<IActionResult> GetPaymentStatus(string transactionId)
    {
        try
        {
            _logger.LogInformation("Getting payment status for transaction {TransactionId}", transactionId);
            
            var status = await _paymentGateway.GetPaymentStatusAsync(transactionId);

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get payment status for transaction {TransactionId}", transactionId);
            return StatusCode(500, new { Error = "Failed to get payment status", Message = ex.Message });
        }
    }

    [HttpPost("notifications/email")]
    public async Task<IActionResult> SendEmail([FromBody] SendEmailRequest request)
    {
        try
        {
            _logger.LogInformation("Sending email to {EmailAddress}", request.To);
            
            await _notificationService.SendEmailAsync(request.To, request.Subject, request.Body);

            return Ok(new { Message = "Email sent successfully", To = request.To });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {EmailAddress}", request.To);
            return StatusCode(500, new { Error = "Email sending failed", Message = ex.Message });
        }
    }

    [HttpPost("notifications/sms")]
    public async Task<IActionResult> SendSms([FromBody] SendSmsRequest request)
    {
        try
        {
            _logger.LogInformation("Sending SMS to {PhoneNumber}", request.PhoneNumber);
            
            await _notificationService.SendSmsAsync(request.PhoneNumber, request.Message);

            return Ok(new { Message = "SMS sent successfully", PhoneNumber = request.PhoneNumber });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {PhoneNumber}", request.PhoneNumber);
            return StatusCode(500, new { Error = "SMS sending failed", Message = ex.Message });
        }
    }

    [HttpPost("notifications/push")]
    public async Task<IActionResult> SendPushNotification([FromBody] SendPushRequest request)
    {
        try
        {
            _logger.LogInformation("Sending push notification to user {UserId}", request.UserId);
            
            await _notificationService.SendPushNotificationAsync(request.UserId, request.Title, request.Message, request.Data);

            return Ok(new { Message = "Push notification sent successfully", UserId = request.UserId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notification to user {UserId}", request.UserId);
            return StatusCode(500, new { Error = "Push notification sending failed", Message = ex.Message });
        }
    }

    [HttpGet("health/{systemId}")]
    public async Task<IActionResult> CheckExternalSystemHealth(string systemId)
    {
        try
        {
            _logger.LogInformation("Checking health of external system {SystemId}", systemId);
            
            var isHealthy = await _externalEventBus.IsExternalSystemHealthyAsync(systemId);

            return Ok(new { SystemId = systemId, IsHealthy = isHealthy, CheckedAt = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check health of external system {SystemId}", systemId);
            return StatusCode(500, new { Error = "Health check failed", Message = ex.Message });
        }
    }
}

// Request DTOs
public record ProcessPaymentRequest(int OrderId, decimal Amount, string PaymentMethod);
public record RefundPaymentRequest(string TransactionId, decimal Amount, string Reason);
public record SendEmailRequest(string To, string Subject, string Body);
public record SendSmsRequest(string PhoneNumber, string Message);
public record SendPushRequest(string UserId, string Title, string Message, Dictionary<string, object>? Data);