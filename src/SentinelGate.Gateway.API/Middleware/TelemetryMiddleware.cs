using System.Diagnostics;
using SentinelGate.Shared.Infrastructure.Services;
using SentinelGate.Shared.Models.Entities;
using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Gateway.API.Middleware;

/// <summary>
/// Wraps the entire pipeline to record telemetry data.
/// Captures latency, status code, request/response sizes, client identity, endpoint, and method.
/// Writes RequestLog entries to the TelemetryChannel in a non-blocking manner.
/// </summary>
public class TelemetryMiddleware : IMiddleware
{
    private readonly TelemetryChannel _telemetryChannel;
    private readonly ILogger<TelemetryMiddleware> _logger;

    public TelemetryMiddleware(
        TelemetryChannel telemetryChannel,
        ILogger<TelemetryMiddleware> logger)
    {
        _telemetryChannel = telemetryChannel;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestBodySize = context.Request.ContentLength ?? 0;

        // Enable response body buffering to capture size
        var originalBodyStream = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            // Copy the response body back to the original stream
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;

            var responseSize = responseBodyStream.Length;

            // Build the telemetry log entry
            var log = new RequestLog
            {
                Id = Guid.NewGuid(),
                ClientIdentity = context.Items["ClientIdentity"]?.ToString(),
                ClientIp = context.Items["ClientIp"]?.ToString(),
                ApiKey = context.Items["ApiKey"]?.ToString(),
                TenantId = context.Items["TenantId"]?.ToString(),
                EndpointPath = context.Request.Path.Value ?? "/",
                HttpMethod = context.Request.Method,
                ResponseStatusCode = context.Response.StatusCode,
                LatencyMs = stopwatch.Elapsed.TotalMilliseconds,
                RequestBodySize = requestBodySize,
                ResponseSize = responseSize,
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                IsBlocked = context.Items.ContainsKey("IsBlocked") && (bool)context.Items["IsBlocked"]!,
                IsRateLimited = context.Items.ContainsKey("IsRateLimited") && (bool)context.Items["IsRateLimited"]!,
                RateLimitAlgorithm = context.Items.TryGetValue("RateLimitAlgorithm", out var algo)
                    ? (RateLimitAlgorithm)algo!
                    : null,
                Timestamp = DateTime.UtcNow
            };

            // Non-blocking write to telemetry channel
            if (!_telemetryChannel.TryWrite(log))
            {
                _logger.LogWarning("Telemetry channel is full, dropping request log for {Endpoint}",
                    log.EndpointPath);
            }
            else
            {
                _logger.LogDebug(
                    "Telemetry: {Method} {Endpoint} -> {StatusCode} in {LatencyMs:F1}ms",
                    log.HttpMethod, log.EndpointPath, log.ResponseStatusCode, log.LatencyMs);
            }
        }
    }
}
