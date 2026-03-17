using SentinelGate.RateLimiter.Service.Services;
using SentinelGate.Shared.Infrastructure.Redis;

var builder = WebApplication.CreateBuilder(args);

// ─── Kestrel configuration ──────────────────────────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001);
});

// ─── Redis ──────────────────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "localhost:6379";
builder.Services.AddSingleton(new RedisConnectionManager(redisConnectionString));

// ─── Rate limiter services ──────────────────────────────────────────────────
builder.Services.AddSingleton<FixedWindowLimiter>();
builder.Services.AddSingleton<SlidingWindowLimiter>();
builder.Services.AddSingleton<TokenBucketLimiter>();
builder.Services.AddSingleton<LeakyBucketLimiter>();
builder.Services.AddSingleton<RateLimiterFactory>();

// ─── Controllers & Swagger ──────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SentinelGate Rate Limiter Service",
        Version = "v1",
        Description = "Rate limiting microservice supporting Fixed Window, Sliding Window, Token Bucket, and Leaky Bucket algorithms."
    });
});

var app = builder.Build();

// ─── Middleware pipeline ────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "RateLimiter Service v1");
        c.RoutePrefix = "swagger";
    });
}

app.MapControllers();

app.Run();
