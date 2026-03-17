using SentinelGate.Analytics.Service.Services;
using SentinelGate.Shared.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Database - InMemory by default, PostgreSQL when connection string is configured
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var useInMemory = string.IsNullOrEmpty(connectionString);
builder.Services.AddSentinelGateDbContext(connectionString ?? string.Empty, useInMemory);

// Telemetry channel
builder.Services.AddTelemetryChannel();

// Application services
builder.Services.AddScoped<AggregationService>();

// Background services
builder.Services.AddHostedService<TelemetryIngestionService>();
builder.Services.AddHostedService<AggregationBackgroundService>();
builder.Services.AddHostedService<DataRetentionService>();

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SentinelGate Analytics API",
        Version = "v1",
        Description = "Analytics and telemetry service for the SentinelGate API Gateway"
    });
});

// Kestrel port
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5002);
});

var app = builder.Build();

// Swagger in development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
