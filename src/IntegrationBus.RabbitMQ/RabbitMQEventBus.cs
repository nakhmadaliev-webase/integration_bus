using IntegrationBus.Core.Abstractions;
using IntegrationBus.Core.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace IntegrationBus.RabbitMQ;

public class RabbitMQEventBus : IEventBus, IDisposable
{
    private const string BROKER_NAME = "integration_event_bus";
    private const string AUTOFAC_SCOPE_NAME = "integration_event_bus";

    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBusSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<RabbitMQEventBus> _logger;
    private readonly RabbitMQEventBusOptions _options;
    private readonly int _retryCount;

    private IConnection? _connection;
    private IModel? _consumerChannel;

    public RabbitMQEventBus(
        IServiceProvider serviceProvider,
        IEventBusSubscriptionsManager subscriptionsManager,
        ILogger<RabbitMQEventBus> logger,
        IOptions<RabbitMQEventBusOptions> options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _subscriptionsManager = subscriptionsManager ?? new InMemoryEventBusSubscriptionsManager();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _retryCount = _options.RetryCount;

        _consumerChannel = CreateConsumerChannel();
        _subscriptionsManager.OnEventRemoved += SubscriptionsManager_OnEventRemoved!;
    }

    public async Task PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        where TIntegrationEvent : IIntegrationEvent
    {
        if (!IsConnected)
        {
            TryConnect();
        }

        var policy = Policy.Handle<BrokerUnreachableException>()
            .Or<SocketException>()
            .WaitAndRetryAsync(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
            {
                _logger.LogWarning(ex, "Could not publish event: {EventId} after {Timeout}s ({ExceptionMessage})", integrationEvent.Id, $"{time.TotalSeconds:n1}", ex.Message);
            });

        var eventName = integrationEvent.GetType().Name;

        _logger.LogTrace("Creating RabbitMQ channel to publish event: {EventId} ({EventName})", integrationEvent.Id, eventName);

        using var channel = _connection?.CreateModel() ?? throw new InvalidOperationException("RabbitMQ connection is not available");

        _logger.LogTrace("Declaring RabbitMQ exchange to publish event: {EventId}", integrationEvent.Id);

        channel.ExchangeDeclare(exchange: BROKER_NAME, type: "direct");

        var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, integrationEvent.GetType(), new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await policy.ExecuteAsync(async () =>
        {
            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2; // persistent

            _logger.LogTrace("Publishing event to RabbitMQ: {EventId}", integrationEvent.Id);

            channel.BasicPublish(
                exchange: BROKER_NAME,
                routingKey: eventName,
                mandatory: true,
                basicProperties: properties,
                body: body);
        });
    }

    public void Subscribe<TIntegrationEvent, TIntegrationEventHandler>()
        where TIntegrationEvent : IIntegrationEvent
        where TIntegrationEventHandler : class, IIntegrationEventHandler<TIntegrationEvent>
    {
        var eventName = _subscriptionsManager.GetEventKey<TIntegrationEvent>();
        DoInternalSubscription(eventName);

        _logger.LogInformation("Subscribing to event {EventName} with {EventHandler}", eventName, typeof(TIntegrationEventHandler).GetGenericTypeName());

        _subscriptionsManager.AddSubscription<TIntegrationEvent, TIntegrationEventHandler>();
        StartBasicConsume();
    }

    public void Unsubscribe<TIntegrationEvent, TIntegrationEventHandler>()
        where TIntegrationEvent : IIntegrationEvent
        where TIntegrationEventHandler : class, IIntegrationEventHandler<TIntegrationEvent>
    {
        var eventName = _subscriptionsManager.GetEventKey<TIntegrationEvent>();

        _logger.LogInformation("Unsubscribing from event {EventName}", eventName);

        _subscriptionsManager.RemoveSubscription<TIntegrationEvent, TIntegrationEventHandler>();
    }

    private void DoInternalSubscription(string eventName)
    {
        var containsKey = _subscriptionsManager.HasSubscriptionsForEvent(eventName);
        if (!containsKey)
        {
            if (!IsConnected)
            {
                TryConnect();
            }

            _consumerChannel!.QueueBind(queue: _options.QueueName,
                                      exchange: BROKER_NAME,
                                      routingKey: eventName);
        }
    }

    private void SubscriptionsManager_OnEventRemoved(object sender, string eventName)
    {
        if (!IsConnected)
        {
            TryConnect();
        }

        using var channel = _connection!.CreateModel();
        channel.QueueUnbind(queue: _options.QueueName,
                           exchange: BROKER_NAME,
                           routingKey: eventName);

        if (_subscriptionsManager.IsEmpty)
        {
            _options.QueueName = string.Empty;
            _consumerChannel!.Close();
        }
    }

    public void Dispose()
    {
        if (_consumerChannel != null)
        {
            _consumerChannel.Dispose();
        }

        _subscriptionsManager.Clear();
    }

    private void StartBasicConsume()
    {
        _logger.LogTrace("Starting RabbitMQ basic consume");

        if (_consumerChannel != null)
        {
            var consumer = new AsyncEventingBasicConsumer(_consumerChannel);

            consumer.Received += Consumer_Received;

            _consumerChannel.BasicConsume(
                queue: _options.QueueName,
                autoAck: false,
                consumer: consumer);
        }
        else
        {
            _logger.LogError("StartBasicConsume can't call on _consumerChannel == null");
        }
    }

    private async Task Consumer_Received(object sender, BasicDeliverEventArgs eventArgs)
    {
        var eventName = eventArgs.RoutingKey;
        var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

        try
        {
            if (message.ToLowerInvariant().Contains("throw-fake-exception"))
            {
                throw new InvalidOperationException($"Fake exception requested: \"{message}\"");
            }

            await ProcessEvent(eventName, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "----- ERROR Processing message \"{Message}\"", message);
        }

        _consumerChannel!.BasicAck(eventArgs.DeliveryTag, multiple: false);
    }

    private IModel CreateConsumerChannel()
    {
        if (!IsConnected)
        {
            TryConnect();
        }

        _logger.LogTrace("Creating RabbitMQ consumer channel");

        var channel = _connection!.CreateModel();

        channel.ExchangeDeclare(exchange: BROKER_NAME, type: "direct");

        channel.QueueDeclare(queue: _options.QueueName,
                           durable: true,
                           exclusive: false,
                           autoDelete: false,
                           arguments: null);

        channel.CallbackException += (sender, ea) =>
        {
            _logger.LogWarning(ea.Exception, "Recreating RabbitMQ consumer channel");

            _consumerChannel!.Dispose();
            _consumerChannel = CreateConsumerChannel();
            StartBasicConsume();
        };

        return channel;
    }

    private async Task ProcessEvent(string eventName, string message)
    {
        _logger.LogTrace("Processing RabbitMQ event: {EventName}", eventName);

        if (_subscriptionsManager.HasSubscriptionsForEvent(eventName))
        {
            using var scope = _serviceProvider.CreateScope();
            var subscriptions = _subscriptionsManager.GetHandlersForEvent(eventName);
            
            foreach (var subscription in subscriptions)
            {
                if (subscription.IsDynamic)
                {
                    if (scope.ServiceProvider.GetService(subscription.HandlerType) is not IDynamicIntegrationEventHandler handler) continue;
                    using dynamic eventData = JsonDocument.Parse(message);
                    await Task.Yield();
                    await handler.Handle(eventData);
                }
                else
                {
                    var handler = scope.ServiceProvider.GetService(subscription.HandlerType);
                    if (handler == null) continue;
                    
                    var eventType = _subscriptionsManager.GetEventTypeByName(eventName);
                    if (eventType == null) continue;
                    
                    var integrationEvent = JsonSerializer.Deserialize(message, eventType, new JsonSerializerOptions()
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    var concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

                    await Task.Yield();
                    await (Task)concreteType.GetMethod("HandleAsync")!.Invoke(handler, new object[] { integrationEvent!, CancellationToken.None })!;
                }
            }
        }
        else
        {
            _logger.LogWarning("No subscription for RabbitMQ event: {EventName}", eventName);
        }
    }

    private bool IsConnected =>
        _connection != null && _connection.IsOpen && !_disposed;

    private bool _disposed;

    private void TryConnect()
    {
        _logger.LogInformation("RabbitMQ Client is trying to connect");

        lock (_connection!)
        {
            var policy = Policy.Handle<SocketException>()
                .Or<BrokerUnreachableException>()
                .WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                {
                    _logger.LogWarning(ex, "RabbitMQ Client could not connect after {TimeOut}s ({ExceptionMessage})", $"{time.TotalSeconds:n1}", ex.Message);
                }
            );

            policy.Execute(() =>
            {
                var factory = new ConnectionFactory()
                {
                    HostName = _options.Connection,
                    DispatchConsumersAsync = true
                };

                if (!string.IsNullOrEmpty(_options.UserName))
                {
                    factory.UserName = _options.UserName;
                }

                if (!string.IsNullOrEmpty(_options.Password))
                {
                    factory.Password = _options.Password;
                }

                _connection = factory.CreateConnection();
            });

            if (IsConnected)
            {
                _connection!.ConnectionShutdown += OnConnectionShutdown;
                _connection.CallbackException += OnCallbackException;
                _connection.ConnectionBlocked += OnConnectionBlocked;

                _logger.LogInformation("RabbitMQ Client acquired a persistent connection to '{HostName}' and is subscribed to failure events", _connection.Endpoint.HostName);
            }
            else
            {
                _logger.LogCritical("FATAL ERROR: RabbitMQ connections could not be created and opened");

                throw new InvalidOperationException("FATAL ERROR: RabbitMQ connections could not be created and opened");
            }
        }
    }

    private void OnConnectionBlocked(object? sender, ConnectionBlockedEventArgs e)
    {
        if (_disposed) return;

        _logger.LogWarning("A RabbitMQ connection is shutdown. Trying to re-connect...");

        TryConnect();
    }

    void OnCallbackException(object? sender, CallbackExceptionEventArgs e)
    {
        if (_disposed) return;

        _logger.LogWarning("A RabbitMQ connection throw exception. Trying to re-connect...");

        TryConnect();
    }

    void OnConnectionShutdown(object? sender, ShutdownEventArgs reason)
    {
        if (_disposed) return;

        _logger.LogWarning("A RabbitMQ connection is on shutdown. Trying to re-connect...");

        TryConnect();
    }
}

public interface IDynamicIntegrationEventHandler
{
    Task Handle(dynamic eventData);
}