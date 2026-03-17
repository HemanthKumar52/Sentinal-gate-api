using SentinelGate.Shared.Models.Enums;

namespace SentinelGate.Shared.Models.Configuration;

public class SentinelGateOptions
{
    public const string SectionName = "SentinelGate";

    public RateLimitingOptions RateLimiting { get; set; } = new();
    public ThreatDetectionOptions ThreatDetection { get; set; } = new();
    public RedisOptions Redis { get; set; } = new();
    public AnalyticsOptions Analytics { get; set; } = new();
    public NotificationsOptions Notifications { get; set; } = new();
}

public class RateLimitingOptions
{
    public RateLimitAlgorithm DefaultAlgorithm { get; set; } = RateLimitAlgorithm.SlidingWindow;
    public int DefaultLimit { get; set; } = 100;
    public int DefaultWindowSeconds { get; set; } = 60;
    public int DefaultBurstLimit { get; set; } = 20;
    public double DefaultRefillRate { get; set; } = 10.0;
    public bool EnableGlobalRateLimit { get; set; } = true;
    public int GlobalLimit { get; set; } = 10000;
    public int GlobalWindowSeconds { get; set; } = 60;
}

public class ThreatDetectionOptions
{
    public bool Enabled { get; set; } = true;
    public double MonitorThreshold { get; set; } = 31.0;
    public double CaptchaThreshold { get; set; } = 31.0;
    public double ThrottleThreshold { get; set; } = 60.0;
    public double TemporaryBlockThreshold { get; set; } = 80.0;
    public double PermanentBlockThreshold { get; set; } = 90.0;
    public double AutoBlockThreshold { get; set; } = 90.0;
    public double DecayHalfLifeHours { get; set; } = 24.0;
    public int RateLimitViolationWeight { get; set; } = 15;
    public int High4xxRateWeight { get; set; } = 20;
    public int AuthFailureWeight { get; set; } = 25;
    public int SingleEndpointHammeringWeight { get; set; } = 20;
    public int UserAgentAnomalyWeight { get; set; } = 10;
    public int GeoMismatchWeight { get; set; } = 10;
    public int PayloadAnomalyWeight { get; set; } = 15;
}

public class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "SentinelGate:";
    public int Database { get; set; }
    public int ConnectTimeout { get; set; } = 5000;
    public int SyncTimeout { get; set; } = 3000;
    public bool AbortOnConnectFail { get; set; }
}

public class AnalyticsOptions
{
    public bool Enabled { get; set; } = true;
    public int HourlyAggregationIntervalMinutes { get; set; } = 60;
    public int DailyAggregationIntervalMinutes { get; set; } = 1440;
    public int RetentionDays { get; set; } = 90;
    public int RequestLogRetentionDays { get; set; } = 30;
}

public class NotificationsOptions
{
    public bool EnableWebhooks { get; set; } = true;
    public bool EnableEmail { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? FromEmail { get; set; }
    public List<string> AlertRecipients { get; set; } = new();
    public int WebhookTimeoutSeconds { get; set; } = 10;
    public int WebhookRetryCount { get; set; } = 3;
}
