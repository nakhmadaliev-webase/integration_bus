namespace IntegrationBus.RabbitMQ;

public class RabbitMQEventBusOptions
{
    public const string SectionName = "RabbitMQ";
    
    public string Connection { get; set; } = "localhost";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string QueueName { get; set; } = "integration_event_bus_queue";
    public int RetryCount { get; set; } = 5;
}