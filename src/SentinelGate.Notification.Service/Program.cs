using SentinelGate.Notification.Service.Services;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Database - InMemory
builder.Services.AddSentinelGateDbContext("", useInMemory: true);

// HttpClient registrations
builder.Services.AddHttpClient<WebhookDispatcher>();
builder.Services.AddHttpClient<SlackNotifier>();
builder.Services.AddHttpClient<TeamsNotifier>();

// Application services
builder.Services.AddScoped<EmailNotifier>();
builder.Services.AddScoped<NotificationOrchestrator>();

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SentinelGate Notification Service",
        Version = "v1",
        Description = "Notification and alerting microservice for SentinelGate"
    });
});

// Kestrel port
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5004);
});

var app = builder.Build();

// Ensure DB is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();
    db.Database.EnsureCreated();
}

// Middleware pipeline
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();
