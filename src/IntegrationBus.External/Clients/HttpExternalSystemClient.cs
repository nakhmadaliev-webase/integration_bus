using IntegrationBus.External.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace IntegrationBus.External.Clients;

public class HttpExternalSystemClient : IExternalSystemClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpExternalSystemClient> _logger;
    private readonly ExternalSystemOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public string SystemId => _options.SystemId;

    public HttpExternalSystemClient(
        HttpClient httpClient,
        IOptions<ExternalSystemOptions> options,
        ILogger<HttpExternalSystemClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        ConfigureHttpClient();
    }

    public async Task<TResponse> SendAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        try
        {
            _logger.LogDebug("Sending request to {SystemId} at endpoint {Endpoint}", SystemId, endpoint);

            var response = await _httpClient.PostAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new ExternalSystemException(SystemId, endpoint, 
                    $"HTTP {response.StatusCode}: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
            if (result == null)
            {
                throw new ExternalSystemException(SystemId, endpoint, "Response deserialization returned null");
            }

            _logger.LogDebug("Successfully received response from {SystemId} at endpoint {Endpoint}", SystemId, endpoint);
            return result;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("Timeout calling {SystemId} at endpoint {Endpoint}", SystemId, endpoint);
            throw new ExternalSystemException(SystemId, endpoint, "Request timeout", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling {SystemId} at endpoint {Endpoint}", SystemId, endpoint);
            throw new ExternalSystemException(SystemId, endpoint, "HTTP request failed", ex);
        }
    }

    public async Task SendAsync<TRequest>(string endpoint, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
    {
        try
        {
            _logger.LogDebug("Sending request to {SystemId} at endpoint {Endpoint}", SystemId, endpoint);

            var response = await _httpClient.PostAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new ExternalSystemException(SystemId, endpoint, 
                    $"HTTP {response.StatusCode}: {errorContent}");
            }

            _logger.LogDebug("Successfully sent request to {SystemId} at endpoint {Endpoint}", SystemId, endpoint);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("Timeout calling {SystemId} at endpoint {Endpoint}", SystemId, endpoint);
            throw new ExternalSystemException(SystemId, endpoint, "Request timeout", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling {SystemId} at endpoint {Endpoint}", SystemId, endpoint);
            throw new ExternalSystemException(SystemId, endpoint, "HTTP request failed", ex);
        }
    }

    public async Task<TResponse> GetAsync<TResponse>(string endpoint, CancellationToken cancellationToken = default)
        where TResponse : class
    {
        try
        {
            _logger.LogDebug("Getting data from {SystemId} at endpoint {Endpoint}", SystemId, endpoint);

            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new ExternalSystemException(SystemId, endpoint, 
                    $"HTTP {response.StatusCode}: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
            if (result == null)
            {
                throw new ExternalSystemException(SystemId, endpoint, "Response deserialization returned null");
            }

            _logger.LogDebug("Successfully received data from {SystemId} at endpoint {Endpoint}", SystemId, endpoint);
            return result;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("Timeout calling {SystemId} at endpoint {Endpoint}", SystemId, endpoint);
            throw new ExternalSystemException(SystemId, endpoint, "Request timeout", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling {SystemId} at endpoint {Endpoint}", SystemId, endpoint);
            throw new ExternalSystemException(SystemId, endpoint, "HTTP request failed", ex);
        }
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var healthEndpoint = _options.HealthCheckEndpoint ?? "/health";
            _logger.LogDebug("Checking health of {SystemId} at {Endpoint}", SystemId, healthEndpoint);

            var response = await _httpClient.GetAsync(healthEndpoint, cancellationToken);
            var isHealthy = response.IsSuccessStatusCode;

            _logger.LogDebug("Health check for {SystemId}: {Status}", SystemId, isHealthy ? "Healthy" : "Unhealthy");
            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for {SystemId}", SystemId);
            return false;
        }
    }

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        
        if (_options.Timeout.HasValue)
        {
            _httpClient.Timeout = _options.Timeout.Value;
        }

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        }

        foreach (var header in _options.DefaultHeaders)
        {
            _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

public class ExternalSystemOptions
{
    public const string SectionName = "ExternalSystems";
    
    public string SystemId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public TimeSpan? Timeout { get; set; }
    public string? HealthCheckEndpoint { get; set; }
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();
    public int? RetryCount { get; set; } = 3;
    public int? CircuitBreakerThreshold { get; set; } = 5;
    public int? CircuitBreakerDurationMinutes { get; set; } = 2;
}

public class ExternalSystemException : Exception
{
    public string SystemId { get; }
    public string Endpoint { get; }

    public ExternalSystemException(string systemId, string endpoint, string message)
        : base(message)
    {
        SystemId = systemId;
        Endpoint = endpoint;
    }

    public ExternalSystemException(string systemId, string endpoint, string message, Exception innerException)
        : base(message, innerException)
    {
        SystemId = systemId;
        Endpoint = endpoint;
    }
}