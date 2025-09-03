using IntegrationBus.Core.Extensions;
using IntegrationBus.Examples.Events;
using IntegrationBus.Examples.Handlers;
using IntegrationBus.InMemory.Extensions;
using IntegrationBus.Core.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Integration Bus - using InMemory implementation for this example
// For RabbitMQ, use: builder.Services.AddRabbitMQEventBus(builder.Configuration);
builder.Services.AddInMemoryEventBus();

// Register event handlers
builder.Services.AddIntegrationEventHandler<OrderCreatedEventHandler, OrderCreatedEvent>();
builder.Services.AddIntegrationEventHandler<PaymentProcessedEventHandler, PaymentProcessedEvent>();
builder.Services.AddIntegrationEventHandler<InventoryUpdatedEventHandler, InventoryUpdatedEvent>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Subscribe to events
var eventBus = app.Services.GetRequiredService<IEventBus>();
eventBus.Subscribe<OrderCreatedEvent, OrderCreatedEventHandler>();
eventBus.Subscribe<PaymentProcessedEvent, PaymentProcessedEventHandler>();
eventBus.Subscribe<InventoryUpdatedEvent, InventoryUpdatedEventHandler>();

app.Run();