using IntegrationBus.External.Abstractions;
using IntegrationBus.External.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using System.Net;

namespace IntegrationBus.External.Policies;

public class ResilientHttpExternalSystemClient : IExternalSystemClient, IDisposable
{
    private readonly HttpExternalSystemClient _innerClient;
    private readonly ILogger<ResilientHttpExternalSystemClient> _logger;
    private readonly IAsyncPolicy _resiliencePolicy;
    private readonly ExternalSystemOptions _options;

    public string SystemId => _innerClient.SystemId;

    public ResilientHttpExternalSystemClient(
        HttpExternalSystemClient innerClient,
        IOptions<ExternalSystemOptions> options,
        ILogger<ResilientHttpExternalSystemClient> logger)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));

        _resiliencePolicy = CreateResiliencePolicy();
    }

    public async Task<TResponse> SendAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
        {
            return await _innerClient.SendAsync<TRequest, TResponse>(endpoint, request, cancellationToken);
        });
    }

    public async Task SendAsync<TRequest>(string endpoint, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _innerClient.SendAsync(endpoint, request, cancellationToken);
        });
    }

    public async Task<TResponse> GetAsync<TResponse>(string endpoint, CancellationToken cancellationToken = default)
        where TResponse : class
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
        {
            return await _innerClient.GetAsync<TResponse>(endpoint, cancellationToken);
        });
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Don't use resilience policies for health checks to avoid masking issues
            return await _innerClient.HealthCheckAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for external system {SystemId}", SystemId);
            return false;
        }
    }

    private IAsyncPolicy CreateResiliencePolicy()
    {
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<ExternalSystemException>(ex => IsTransientError(ex))
            .WaitAndRetryAsync(
                retryCount: _options.RetryCount ?? 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} for {SystemId} in {Delay}ms", 
                        retryCount, SystemId, timespan.TotalMilliseconds);
                });

        var timeoutPolicy = Policy.TimeoutAsync(_options.Timeout ?? TimeSpan.FromSeconds(30));

        return Policy.WrapAsync(retryPolicy, timeoutPolicy);
    }

    private static bool IsTransientError(ExternalSystemException ex)
    {
        // Consider these as transient errors that should be retried
        return ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("503", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("502", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("500", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _innerClient?.Dispose();
    }
}

// Enhanced options for resilience configuration
public class ResilientExternalSystemOptions : ExternalSystemOptions
{
    public new int? RetryCount { get; set; } = 3;
    public new int? CircuitBreakerThreshold { get; set; } = 5;
    public new int? CircuitBreakerDurationMinutes { get; set; } = 2;
    public bool EnableBulkhead { get; set; } = false;
    public int? BulkheadMaxParallelization { get; set; } = 10;
    public int? BulkheadMaxQueuingActions { get; set; } = 20;
}