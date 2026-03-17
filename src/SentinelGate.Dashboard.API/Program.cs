using SentinelGate.Dashboard.API.Hubs;
using SentinelGate.Dashboard.API.Services;
using SentinelGate.Shared.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Database - InMemory for development
builder.Services.AddSentinelGateDbContext(
    builder.Configuration.GetConnectionString("DefaultConnection") ?? "",
    useInMemory: true);

// SignalR
builder.Services.AddSignalR();

// Services
builder.Services.AddScoped<DashboardDataService>();
builder.Services.AddHostedService<MetricsBroadcaster>();

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SentinelGate Dashboard API", Version = "v1" });
});

// CORS - permissive for development (SignalR-friendly)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("SignalR", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard").RequireCors("SignalR");

app.Run();
