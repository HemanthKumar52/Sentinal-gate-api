using SentinelGate.Gateway.API.Middleware;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Infrastructure.Extensions;
using SentinelGate.Shared.Models.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ───────────────────────────────────────────────────────────
builder.Services.Configure<SentinelGateOptions>(
    builder.Configuration.GetSection(SentinelGateOptions.SectionName));

// ─── Database ────────────────────────────────────────────────────────────────
var useInMemory = builder.Configuration.GetValue<bool>("USE_INMEMORY", true)
                  || Environment.GetEnvironmentVariable("USE_INMEMORY")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Host=localhost;Database=sentinelgate;Username=postgres;Password=postgres";

builder.Services.AddSentinelGateDbContext(connectionString, useInMemory);

// ─── Redis ───────────────────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetValue<string>("SentinelGate:Redis:ConnectionString")
                            ?? "localhost:6379";
builder.Services.AddSentinelGateRedis(redisConnectionString);

// ─── Telemetry Channel ───────────────────────────────────────────────────────
builder.Services.AddTelemetryChannel();

// ─── Middleware (IMiddleware requires DI registration) ────────────────────────
builder.Services.AddTransient<TelemetryMiddleware>();
builder.Services.AddTransient<IdentityResolutionMiddleware>();
builder.Services.AddTransient<BlockListCheckMiddleware>();
builder.Services.AddTransient<RateLimitMiddleware>();
builder.Services.AddTransient<ThreatScoreMiddleware>();

// ─── Controllers & Swagger ───────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SentinelGate Gateway API",
        Version = "v1",
        Description = "API Gateway with intelligent rate limiting, threat detection, and traffic management."
    });

    // Include XML comments for Swagger documentation
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// ─── CORS (allow all for development) ────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders(
                  "X-RateLimit-Limit",
                  "X-RateLimit-Remaining",
                  "X-RateLimit-Reset",
                  "X-RateLimit-Policy",
                  "Retry-After");
    });
});

// ─── HttpClient for inter-service communication ──────────────────────────────
builder.Services.AddHttpClient("ThreatDetection", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration.GetValue<string>("ServiceUrls:ThreatDetection")
        ?? "http://localhost:5003");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient("Analytics", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration.GetValue<string>("ServiceUrls:Analytics")
        ?? "http://localhost:5004");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient("Notification", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration.GetValue<string>("ServiceUrls:Notification")
        ?? "http://localhost:5005");
    client.Timeout = TimeSpan.FromSeconds(5);
});

// ─── Build ───────────────────────────────────────────────────────────────────
var app = builder.Build();

// ─── Seed database ───────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();
    await DbInitializer.SeedAsync(dbContext);
}

// ─── Swagger ─────────────────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "SentinelGate Gateway API v1");
    options.RoutePrefix = "swagger";
});

// ─── CORS ────────────────────────────────────────────────────────────────────
app.UseCors();

// ─── Middleware pipeline (order matters) ─────────────────────────────────────
// 1. Telemetry wraps everything — records latency, status codes, sizes
app.UseMiddleware<TelemetryMiddleware>();

// 2. Identity resolution — extract client identity from API key / JWT / IP
app.UseMiddleware<IdentityResolutionMiddleware>();

// 3. Block list check — reject blocked clients early
app.UseMiddleware<BlockListCheckMiddleware>();

// 4. Rate limiting — enforce rate policies
app.UseMiddleware<RateLimitMiddleware>();

// 5. Threat score — evaluate signals after response
app.UseMiddleware<ThreatScoreMiddleware>();

// ─── Map controllers ─────────────────────────────────────────────────────────
app.MapControllers();

// ─── Run ─────────────────────────────────────────────────────────────────────
app.Run();
