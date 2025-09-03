using IntegrationBus.External.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationBus.External.Examples.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookReceiver _webhookReceiver;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(IWebhookReceiver webhookReceiver, ILogger<WebhookController> logger)
    {
        _webhookReceiver = webhookReceiver;
        _logger = logger;
    }

    [HttpPost("payment-gateway")]
    public async Task<IActionResult> ReceivePaymentGatewayWebhook()
    {
        const string systemId = "payment-gateway";
        return await ProcessWebhook(systemId);
    }

    [HttpPost("notification-service")]
    public async Task<IActionResult> ReceiveNotificationServiceWebhook()
    {
        const string systemId = "notification-service";
        return await ProcessWebhook(systemId);
    }

    [HttpPost("{systemId}")]
    public async Task<IActionResult> ReceiveGenericWebhook(string systemId)
    {
        return await ProcessWebhook(systemId);
    }

    private async Task<IActionResult> ProcessWebhook(string systemId)
    {
        try
        {
            // Read the raw payload
            using var reader = new StreamReader(Request.Body);
            var payload = await reader.ReadToEndAsync();

            if (string.IsNullOrEmpty(payload))
            {
                _logger.LogWarning("Received empty webhook payload from {SystemId}", systemId);
                return BadRequest(new { Error = "Empty payload" });
            }

            // Get signature from headers (common header names)
            var signature = Request.Headers["X-Signature"].FirstOrDefault() ??
                           Request.Headers["X-Hub-Signature-256"].FirstOrDefault() ??
                           Request.Headers["X-Webhook-Signature"].FirstOrDefault() ??
                           string.Empty;

            _logger.LogInformation("Received webhook from {SystemId} with payload length {PayloadLength}", 
                systemId, payload.Length);

            // Validate webhook signature
            var isValid = await _webhookReceiver.ValidateWebhookAsync(systemId, payload, signature);
            if (!isValid)
            {
                _logger.LogWarning("Invalid webhook signature from {SystemId}", systemId);
                return Unauthorized(new { Error = "Invalid signature" });
            }

            // Process the webhook
            await _webhookReceiver.ProcessWebhookAsync(systemId, payload);

            _logger.LogInformation("Successfully processed webhook from {SystemId}", systemId);
            return Ok(new { Message = "Webhook processed successfully", SystemId = systemId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook from {SystemId}", systemId);
            return StatusCode(500, new { Error = "Webhook processing failed", Message = ex.Message });
        }
    }
}