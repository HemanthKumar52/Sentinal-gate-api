using Microsoft.EntityFrameworkCore;
using SentinelGate.Shared.Infrastructure.Data;
using SentinelGate.Shared.Models.Configuration;
using SentinelGate.ThreatDetection.Service.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Kestrel configuration ──────────────────────────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5003);
});

// ─── Configuration ──────────────────────────────────────────────────────────
builder.Services.Configure<ThreatDetectionOptions>(
    builder.Configuration.GetSection("ThreatDetection"));

// ─── Database (InMemory by default, PostgreSQL via config) ──────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<SentinelGateDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    builder.Services.AddDbContext<SentinelGateDbContext>(options =>
        options.UseInMemoryDatabase("SentinelGate_ThreatDetection"));
}

// ─── Services ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<ThreatScoringEngine>();
builder.Services.AddSingleton<GeoLookupService>();
builder.Services.AddHostedService<ThreatDecayBackgroundService>();

// ─── Controllers & Swagger ──────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SentinelGate Threat Detection Service",
        Version = "v1",
        Description = "Threat scoring, auto-blocking, and blocklist management microservice."
    });
});

var app = builder.Build();

// ─── Ensure database is created ────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SentinelGateDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// ─── Middleware pipeline ────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ThreatDetection Service v1");
        c.RoutePrefix = "swagger";
    });
}

app.MapControllers();

app.Run();
