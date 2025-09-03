using IntegrationBus.Core.Abstractions;
using IntegrationBus.External.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntegrationBus.External.Services;

public class WebhookReceiver : IWebhookReceiver
{
    private readonly ILogger<WebhookReceiver> _logger;
    private readonly IEventBus _eventBus;
    private readonly Dictionary<string, WebhookSystemConfig> _systemConfigs;
    private readonly JsonSerializerOptions _jsonOptions;

    public WebhookReceiver(
        ILogger<WebhookReceiver> logger,
        IEventBus eventBus,
        IOptions<WebhookOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _systemConfigs = options.Value?.Systems ?? new Dictionary<string, WebhookSystemConfig>();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public Task<bool> ValidateWebhookAsync(string systemId, string payload, string signature, CancellationToken cancellationToken = default)
    {
        if (!_systemConfigs.TryGetValue(systemId, out var config))
        {
            _logger.LogWarning("No webhook configuration found for system {SystemId}", systemId);
            return Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(config.SecretKey))
        {
            _logger.LogWarning("No secret key configured for webhook validation for system {SystemId}", systemId);
            return Task.FromResult(true); // Allow if no validation is configured
        }

        try
        {
            var expectedSignature = GenerateSignature(payload, config.SecretKey, config.SignatureFormat);
            var isValid = string.Equals(signature, expectedSignature, StringComparison.OrdinalIgnoreCase);

            if (!isValid)
            {
                _logger.LogWarning("Webhook signature validation failed for system {SystemId}", systemId);
            }

            return Task.FromResult(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating webhook signature for system {SystemId}", systemId);
            return Task.FromResult(false);
        }
    }

    public async Task ProcessWebhookAsync(string systemId, string payload, CancellationToken cancellationToken = default)
    {
        if (!_systemConfigs.TryGetValue(systemId, out var config))
        {
            _logger.LogError("No webhook configuration found for system {SystemId}", systemId);
            return;
        }

        try
        {
            _logger.LogInformation("Processing webhook from system {SystemId}", systemId);

            var webhookData = JsonSerializer.Deserialize<Dictionary<string, object>>(payload, _jsonOptions);
            if (webhookData == null)
            {
                _logger.LogWarning("Failed to deserialize webhook payload from system {SystemId}", systemId);
                return;
            }

            // Extract event type from webhook payload
            var eventType = ExtractEventType(webhookData, config);
            if (string.IsNullOrEmpty(eventType))
            {
                _logger.LogWarning("Could not determine event type from webhook payload for system {SystemId}", systemId);
                return;
            }

            // Create and publish external integration event
            var externalEvent = new GenericExternalWebhookEvent
            {
                ExternalSystemId = systemId,
                ExternalEventId = ExtractEventId(webhookData, config),
                EventTypeFromWebhook = eventType,
                RawPayload = payload,
                ProcessedData = webhookData,
                Metadata = new Dictionary<string, object>
                {
                    ["webhook_received_at"] = DateTime.UtcNow,
                    ["payload_size"] = payload.Length,
                    ["system_id"] = systemId
                }
            };

            await _eventBus.PublishAsync(externalEvent, cancellationToken);

            _logger.LogInformation("Successfully processed webhook from system {SystemId} with event type {EventType}",
                systemId, eventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook from system {SystemId}", systemId);
            throw;
        }
    }

    private string GenerateSignature(string payload, string secretKey, WebhookSignatureFormat format)
    {
        return format switch
        {
            WebhookSignatureFormat.HmacSha256 => GenerateHmacSha256Signature(payload, secretKey),
            WebhookSignatureFormat.HmacSha1 => GenerateHmacSha1Signature(payload, secretKey),
            WebhookSignatureFormat.Sha256 => GenerateSha256Hash(payload + secretKey),
            _ => throw new NotSupportedException($"Signature format {format} is not supported")
        };
    }

    private string GenerateHmacSha256Signature(string payload, string secretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(hash).ToLower();
    }

    private string GenerateHmacSha1Signature(string payload, string secretKey)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return "sha1=" + Convert.ToHexString(hash).ToLower();
    }

    private string GenerateSha256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLower();
    }

    private string ExtractEventType(Dictionary<string, object> webhookData, WebhookSystemConfig config)
    {
        if (!string.IsNullOrEmpty(config.EventTypeField) && 
            webhookData.TryGetValue(config.EventTypeField, out var eventTypeValue))
        {
            return eventTypeValue?.ToString() ?? string.Empty;
        }

        // Fallback: try common field names
        var commonFields = new[] { "event_type", "eventType", "type", "event", "action" };
        foreach (var field in commonFields)
        {
            if (webhookData.TryGetValue(field, out var value))
            {
                return value?.ToString() ?? string.Empty;
            }
        }

        return "unknown";
    }

    private string ExtractEventId(Dictionary<string, object> webhookData, WebhookSystemConfig config)
    {
        if (!string.IsNullOrEmpty(config.EventIdField) &&
            webhookData.TryGetValue(config.EventIdField, out var eventIdValue))
        {
            return eventIdValue?.ToString() ?? Guid.NewGuid().ToString();
        }

        // Fallback: try common field names
        var commonFields = new[] { "id", "event_id", "eventId", "uuid", "guid" };
        foreach (var field in commonFields)
        {
            if (webhookData.TryGetValue(field, out var value))
            {
                return value?.ToString() ?? Guid.NewGuid().ToString();
            }
        }

        return Guid.NewGuid().ToString();
    }
}

public class WebhookOptions
{
    public const string SectionName = "Webhooks";
    
    public Dictionary<string, WebhookSystemConfig> Systems { get; set; } = new();
}

public class WebhookSystemConfig
{
    public string SecretKey { get; set; } = string.Empty;
    public WebhookSignatureFormat SignatureFormat { get; set; } = WebhookSignatureFormat.HmacSha256;
    public string EventTypeField { get; set; } = "event_type";
    public string EventIdField { get; set; } = "id";
}

public enum WebhookSignatureFormat
{
    HmacSha256,
    HmacSha1,
    Sha256
}

public record GenericExternalWebhookEvent : IntegrationBus.External.Events.ExternalIntegrationEvent
{
    public string EventTypeFromWebhook { get; init; } = string.Empty;
    public string RawPayload { get; init; } = string.Empty;
    public Dictionary<string, object> ProcessedData { get; init; } = new();
}