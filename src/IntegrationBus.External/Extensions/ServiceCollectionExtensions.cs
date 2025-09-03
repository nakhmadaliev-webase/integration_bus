using IntegrationBus.Core.Extensions;
using IntegrationBus.External.Abstractions;
using IntegrationBus.External.Adapters;
using IntegrationBus.External.Clients;
using IntegrationBus.External.Policies;
using IntegrationBus.External.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace IntegrationBus.External.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExternalIntegrationBus(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIntegrationBusCore();
        
        // Core external integration services
        services.AddSingleton<IExternalEventBus, ExternalEventBus>();
        services.AddSingleton<IWebhookReceiver, WebhookReceiver>();
        
        // Configure webhook options
        services.Configure<WebhookOptions>(configuration.GetSection(WebhookOptions.SectionName));
        
        return services;
    }

    public static IServiceCollection AddExternalSystem<TAdapter>(
        this IServiceCollection services,
        string systemId,
        IConfiguration configuration,
        bool useResilience = true)
        where TAdapter : class, IExternalSystemAdapter
    {
        var sectionName = $"{ExternalSystemOptions.SectionName}:{systemId}";
        services.Configure<ExternalSystemOptions>(systemId, configuration.GetSection(sectionName));

        if (useResilience)
        {
            services.Configure<ResilientExternalSystemOptions>(systemId, configuration.GetSection(sectionName));
        }

        // Register HTTP client with Polly policies
        services.AddHttpClient<HttpExternalSystemClient>(systemId, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptionsSnapshot<ExternalSystemOptions>>().Get(systemId);
            client.BaseAddress = new Uri(options.BaseUrl);
            
            if (options.Timeout.HasValue)
            {
                client.Timeout = options.Timeout.Value;
            }

            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
            }

            foreach (var header in options.DefaultHeaders)
            {
                client.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        })
        .AddPolicyHandler((serviceProvider, request) =>
        {
            var options = serviceProvider.GetRequiredService<IOptionsSnapshot<ResilientExternalSystemOptions>>().Get(systemId);
            
            return Policy.WrapAsync(
                GetRetryPolicy(options, serviceProvider.GetRequiredService<ILogger<HttpExternalSystemClient>>()),
                GetCircuitBreakerPolicy(options, serviceProvider.GetRequiredService<ILogger<HttpExternalSystemClient>>())
            );
        });

        // Register the external system client
        services.AddTransient<IExternalSystemClient>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(systemId);
            var options = serviceProvider.GetRequiredService<IOptionsSnapshot<ExternalSystemOptions>>().Get(systemId);
            var logger = serviceProvider.GetRequiredService<ILogger<HttpExternalSystemClient>>();

            var baseClient = new HttpExternalSystemClient(httpClient, 
                Microsoft.Extensions.Options.Options.Create(options), logger);

            if (useResilience)
            {
                var resilientOptions = serviceProvider.GetRequiredService<IOptionsSnapshot<ResilientExternalSystemOptions>>().Get(systemId);
                var resilientLogger = serviceProvider.GetRequiredService<ILogger<ResilientHttpExternalSystemClient>>();
                return new ResilientHttpExternalSystemClient(baseClient, 
                    Microsoft.Extensions.Options.Options.Create(resilientOptions), resilientLogger);
            }

            return baseClient;
        });

        // Register the adapter
        services.AddTransient<TAdapter>();

        // Auto-register the external system with the event bus
        services.AddTransient<IHostedService>(serviceProvider =>
            new ExternalSystemRegistrationService(serviceProvider, systemId));

        return services;
    }

    public static IServiceCollection AddPaymentGatewayIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddExternalSystem<PaymentGatewayAdapter>("payment-gateway", configuration);
    }

    public static IServiceCollection AddNotificationServiceIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddExternalSystem<NotificationServiceAdapter>("notification-service", configuration);
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ResilientExternalSystemOptions options, ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: options.RetryCount ?? 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    logger.LogWarning("Retry {RetryCount} for {SystemId} in {Delay}ms",
                        retryCount, options.SystemId, timespan.TotalMilliseconds);
                });
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(ResilientExternalSystemOptions options, ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: options.CircuitBreakerThreshold ?? 5,
                durationOfBreak: TimeSpan.FromMinutes(options.CircuitBreakerDurationMinutes ?? 2),
                onBreak: (result, timespan) =>
                {
                    logger.LogError("Circuit breaker opened for {SystemId} for {Duration}ms",
                        options.SystemId, timespan.TotalMilliseconds);
                },
                onReset: () =>
                {
                    logger.LogInformation("Circuit breaker reset for {SystemId}", options.SystemId);
                });
    }
}

// Background service to register external systems with the event bus
public class ExternalSystemRegistrationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _systemId;

    public ExternalSystemRegistrationService(IServiceProvider serviceProvider, string systemId)
    {
        _serviceProvider = serviceProvider;
        _systemId = systemId;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000, stoppingToken); // Wait for services to be ready

        using var scope = _serviceProvider.CreateScope();
        var externalEventBus = scope.ServiceProvider.GetRequiredService<IExternalEventBus>();
        var client = scope.ServiceProvider.GetRequiredService<IExternalSystemClient>();

        externalEventBus.RegisterExternalSystem(_systemId, client);
    }
}