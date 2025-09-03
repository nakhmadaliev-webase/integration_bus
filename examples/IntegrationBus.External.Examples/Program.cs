using IntegrationBus.External.Extensions;
using IntegrationBus.InMemory.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Internal Integration Bus (in-memory for this example)
builder.Services.AddInMemoryEventBus();

// Add External Integration Bus
builder.Services.AddExternalIntegrationBus(builder.Configuration);

// Add external system integrations
builder.Services.AddPaymentGatewayIntegration(builder.Configuration);
builder.Services.AddNotificationServiceIntegration(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "External Integration Bus API V1");
        c.RoutePrefix = string.Empty; // Set Swagger UI at app's root
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();