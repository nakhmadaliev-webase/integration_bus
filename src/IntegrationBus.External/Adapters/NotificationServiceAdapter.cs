using IntegrationBus.External.Abstractions;
using IntegrationBus.External.Events;
using Microsoft.Extensions.Logging;

namespace IntegrationBus.External.Adapters;

public class NotificationServiceAdapter : IExternalSystemAdapter
{
    private readonly IExternalEventBus _externalEventBus;
    private readonly ILogger<NotificationServiceAdapter> _logger;
    
    public string SystemId => "notification-service";

    public NotificationServiceAdapter(IExternalEventBus externalEventBus, ILogger<NotificationServiceAdapter> logger)
    {
        _externalEventBus = externalEventBus ?? throw new ArgumentNullException(nameof(externalEventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var emailRequest = new SendEmailRequest
        {
            To = to,
            Subject = subject,
            Body = body,
            IsHtml = true
        };

        await SendNotificationAsync("/api/email/send", emailRequest, cancellationToken);
    }

    public async Task SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        var smsRequest = new SendSmsRequest
        {
            PhoneNumber = phoneNumber,
            Message = message
        };

        await SendNotificationAsync("/api/sms/send", smsRequest, cancellationToken);
    }

    public async Task SendPushNotificationAsync(string userId, string title, string message, Dictionary<string, object>? data = null, CancellationToken cancellationToken = default)
    {
        var pushRequest = new SendPushNotificationRequest
        {
            UserId = userId,
            Title = title,
            Message = message,
            Data = data ?? new Dictionary<string, object>()
        };

        await SendNotificationAsync("/api/push/send", pushRequest, cancellationToken);
    }

    private async Task SendNotificationAsync<T>(string endpoint, T request, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var response = await _externalEventBus.RequestFromExternalSystemAsync<T, NotificationResponse>(
                SystemId, endpoint, request, cancellationToken);

            _logger.LogInformation("Notification sent successfully with ID {NotificationId}", response.NotificationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification via {Endpoint}", endpoint);
            throw;
        }
    }
}

// Request/Response DTOs for Notification Service
public record SendEmailRequest
{
    public string To { get; init; } = string.Empty;
    public string? Cc { get; init; }
    public string? Bcc { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public bool IsHtml { get; init; } = false;
    public List<EmailAttachment>? Attachments { get; init; }
}

public record EmailAttachment
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public byte[] Data { get; init; } = Array.Empty<byte>();
}

public record SendSmsRequest
{
    public string PhoneNumber { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public record SendPushNotificationRequest
{
    public string UserId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, object> Data { get; init; } = new();
}

public record NotificationResponse
{
    public string NotificationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime SentAt { get; init; }
    public string Message { get; init; } = string.Empty;
}

// External events that can be received via webhooks
public record EmailDeliveredEvent : ExternalIntegrationEvent
{
    public string NotificationId { get; init; } = string.Empty;
    public string EmailAddress { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public DateTime DeliveredAt { get; init; }
}

public record EmailFailedEvent : ExternalIntegrationEvent
{
    public string NotificationId { get; init; } = string.Empty;
    public string EmailAddress { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public DateTime FailedAt { get; init; }
}

public record SmsDeliveredEvent : ExternalIntegrationEvent
{
    public string NotificationId { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public DateTime DeliveredAt { get; init; }
}

public record SmsFailedEvent : ExternalIntegrationEvent
{
    public string NotificationId { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public DateTime FailedAt { get; init; }
}

public record PushNotificationDeliveredEvent : ExternalIntegrationEvent
{
    public string NotificationId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public DateTime DeliveredAt { get; init; }
}