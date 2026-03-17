# SentinelGate

**Intelligent API Gateway with adaptive rate limiting, real-time threat detection, and traffic analytics.**

SentinelGate is a microservices-based API gateway platform that protects backend services through multi-algorithm rate limiting, behavioral threat scoring, and comprehensive traffic observability. Built with .NET 8 and designed for multi-tenant SaaS environments.

---

## Architecture Overview

```
                              +------------------+
                              |   Client Request  |
                              +--------+---------+
                                       |
                              +--------v---------+
                              |  Gateway API:5000 |
                              |  (Middleware Pipeline)|
                              |                   |
                              |  1. Telemetry     |
                              |  2. Identity      |
                              |  3. Block List    |
                              |  4. Rate Limit    |
                              |  5. Threat Score  |
                              +---+-----+-----+--+
                                  |     |     |
                 +----------------+     |     +----------------+
                 |                      |                      |
        +--------v--------+   +--------v--------+   +---------v-------+
        | RateLimiter:5001|   | ThreatDetect:5003|   | Analytics:5002 |
        | Fixed Window    |   | Scoring Engine   |   | Aggregation    |
        | Sliding Window  |   | Decay Service    |   | Retention      |
        | Token Bucket    |   | Geo Lookup       |   | Export         |
        | Leaky Bucket    |   +---------+--------+   +--------+-------+
        +--------+--------+             |                      |
                 |              +-------v--------+             |
                 |              | Notification   |             |
                 |              | :5004          |             |
                 |              | Webhook/Slack  |             |
                 |              | Teams/Email    |             |
                 |              +----------------+             |
                 |                                             |
        +--------v--------+                          +--------v-------+
        | Identity:5005   |                          | Dashboard:5006 |
        | JWT Auth        |                          | Live Metrics   |
        | API Keys        |                          | SignalR Hub    |
        | Tenants         |                          +--------+-------+
        +--------+--------+                                   |
                 |                                             |
        +--------v---------------------------------------------v-------+
        |                    PostgreSQL :5432                           |
        |                    Redis :6379                                |
        +--------------------------------------------------------------+
```

## Tech Stack

| Component          | Technology                              |
|--------------------|-----------------------------------------|
| Runtime            | .NET 8 (ASP.NET Core)                   |
| Language           | C# 12                                   |
| Database           | PostgreSQL 16 + EF Core 8               |
| Cache              | Redis 7 (StackExchange.Redis)           |
| Auth               | JWT Bearer (System.IdentityModel)       |
| Real-time          | ASP.NET Core SignalR                     |
| API Docs           | Swagger / Swashbuckle                   |
| Containers         | Docker + Docker Compose                 |
| Testing            | xUnit + EF Core InMemory                |

## Features

### Gateway API
- Middleware pipeline: Telemetry, Identity Resolution, Block List Check, Rate Limiting, Threat Scoring
- Admin endpoints for policies, block lists, audit logs, and threat scores
- Health check with Redis and database connectivity status
- Swagger UI at `/swagger`

### Rate Limiter Service
- Four algorithms: Fixed Window, Sliding Window, Token Bucket, Leaky Bucket
- Per-client and per-endpoint rate limiting
- Redis-backed counters with in-memory fallback
- Counter inspection and reset endpoints

### Analytics Service
- Request telemetry ingestion and storage
- Hourly and daily traffic aggregation (background service)
- Traffic summary, endpoint stats, top clients, latency percentiles
- CSV export of raw request logs
- Configurable data retention policies

### Threat Detection Service
- Behavioral threat scoring engine with 7 signal types
- Automatic score decay over time (background service)
- Tiered response actions (Monitor, CAPTCHA, Throttle, Temporary Block, Permanent Block)
- Block list management with import/export
- Geo-IP lookup integration

### Notification Service
- Multi-channel alerting: Webhooks, Slack, Microsoft Teams, Email (SMTP)
- Webhook subscription management
- Alert event tracking with acknowledgment
- Configurable retry policies

### Identity Service
- JWT authentication with login and registration
- API key generation, rotation, and revocation
- Multi-tenant support with tier-based policies (Free, Pro, Enterprise)
- Developer portal endpoints for self-service key and webhook management

### Dashboard API
- Real-time live metrics endpoint
- Top clients analysis
- Error heatmap data
- Threat leaderboard
- System health overview
- SignalR hub for real-time metric broadcasting

## Quick Start

### Prerequisites
- [Docker](https://docs.docker.com/get-docker/) and Docker Compose
- OR [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) for local development

### Using Docker (recommended)

```bash
git clone https://github.com/your-org/SentinelGate.git
cd SentinelGate
docker-compose up --build
```

Services will be available at:
| Service           | URL                          |
|-------------------|------------------------------|
| Gateway API       | http://localhost:5000        |
| Swagger UI        | http://localhost:5000/swagger |
| RateLimiter       | http://localhost:5001        |
| Analytics         | http://localhost:5002        |
| ThreatDetection   | http://localhost:5003        |
| Notification      | http://localhost:5004        |
| Identity          | http://localhost:5005        |
| Dashboard         | http://localhost:5006        |

### Local Development (without Docker)

By default, all services use in-memory databases, so no external dependencies are needed:

```bash
# Build the entire solution
dotnet build SentinelGate.slnx

# Run each service in a separate terminal
dotnet run --project src/SentinelGate.Gateway.API
dotnet run --project src/SentinelGate.RateLimiter.Service
dotnet run --project src/SentinelGate.Analytics.Service
dotnet run --project src/SentinelGate.ThreatDetection.Service
dotnet run --project src/SentinelGate.Notification.Service
dotnet run --project src/SentinelGate.Identity.Service
dotnet run --project src/SentinelGate.Dashboard.API
```

To use PostgreSQL and Redis locally, set environment variables:
```bash
export USE_INMEMORY=false
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=sentinelgate;Username=postgres;Password=postgres"
export SentinelGate__Redis__ConnectionString="localhost:6379"
```

## API Documentation

Full API documentation with request/response examples is available in [docs/API.md](docs/API.md).

### Endpoint Summary

#### Gateway API (`:5000`)
| Method | Endpoint                              | Description                    |
|--------|---------------------------------------|--------------------------------|
| GET    | `/health`                             | Service health check           |
| GET    | `/api/admin/policies`                 | List rate policies             |
| POST   | `/api/admin/policies`                 | Create rate policy             |
| PUT    | `/api/admin/policies/{id}`            | Update rate policy             |
| DELETE | `/api/admin/policies/{id}`            | Delete rate policy             |
| GET    | `/api/admin/blocklist`                | List blocked clients           |
| POST   | `/api/admin/blocklist`                | Add to block list              |
| DELETE | `/api/admin/blocklist/{id}`           | Remove from block list         |
| GET    | `/api/admin/audit-log`                | Query audit log                |
| GET    | `/api/admin/threat-scores`            | List threat scores             |
| POST   | `/api/admin/threat-scores/{id}/reset` | Reset threat score             |

#### RateLimiter Service (`:5001`)
| Method | Endpoint                                | Description              |
|--------|-----------------------------------------|--------------------------|
| POST   | `/api/ratelimit/check`                  | Check rate limit          |
| GET    | `/api/ratelimit/counters/{clientId}`    | Get counter state         |
| DELETE | `/api/ratelimit/counters/{clientId}`    | Reset counters            |
| GET    | `/api/ratelimit/health`                 | Health check              |

#### Analytics Service (`:5002`)
| Method | Endpoint                          | Description              |
|--------|-----------------------------------|--------------------------|
| GET    | `/api/analytics/summary`          | Traffic summary           |
| GET    | `/api/analytics/endpoints`        | Endpoint statistics       |
| GET    | `/api/analytics/clients/top`      | Top clients               |
| GET    | `/api/analytics/latency/percentiles` | Latency percentiles   |
| GET    | `/api/analytics/reports/export`   | Export raw logs (CSV)     |
| GET    | `/api/analytics/health`           | Health check              |

#### Threat Detection Service (`:5003`)
| Method | Endpoint                              | Description              |
|--------|---------------------------------------|--------------------------|
| POST   | `/api/threat/score/update`            | Update threat score       |
| GET    | `/api/threat/score/{clientId}`        | Get threat score          |
| POST   | `/api/threat/score/{clientId}/reset`  | Reset threat score        |
| GET    | `/api/threat/scores`                  | List all scores           |
| GET    | `/api/threat/blocklist`               | List blocked clients      |
| POST   | `/api/threat/blocklist`               | Block a client            |
| DELETE | `/api/threat/blocklist/{id}`          | Unblock a client          |
| POST   | `/api/threat/blocklist/import`        | Import block list         |
| GET    | `/api/threat/blocklist/export`        | Export block list          |
| GET    | `/api/threat/health`                  | Health check              |

#### Notification Service (`:5004`)
| Method | Endpoint                                       | Description              |
|--------|------------------------------------------------|--------------------------|
| POST   | `/api/notifications/send`                      | Send notification         |
| GET    | `/api/notifications/events`                    | List alert events         |
| POST   | `/api/notifications/events/{id}/acknowledge`   | Acknowledge alert         |
| GET    | `/api/notifications/webhooks`                  | List webhooks             |
| POST   | `/api/notifications/webhooks`                  | Register webhook          |
| DELETE | `/api/notifications/webhooks/{id}`             | Delete webhook            |
| GET    | `/api/notifications/health`                    | Health check              |

#### Identity Service (`:5005`)
| Method | Endpoint                            | Description              |
|--------|-------------------------------------|--------------------------|
| POST   | `/api/auth/login`                   | Login (get JWT)           |
| POST   | `/api/auth/register`                | Register tenant           |
| GET    | `/api/auth/me`                      | Current user info         |
| GET    | `/api/developer/keys`               | List API keys             |
| POST   | `/api/developer/keys`               | Create API key            |
| POST   | `/api/developer/keys/{id}/rotate`   | Rotate API key            |
| DELETE | `/api/developer/keys/{id}`          | Revoke API key            |
| GET    | `/api/developer/usage`              | Get usage stats           |
| GET    | `/api/developer/webhooks`           | List webhooks             |
| POST   | `/api/developer/webhooks`           | Register webhook          |
| GET    | `/api/tenants`                      | List tenants              |
| GET    | `/api/tenants/{id}`                 | Get tenant by ID          |
| POST   | `/api/tenants`                      | Create tenant             |
| PUT    | `/api/tenants/{id}/tier`            | Update tenant tier        |

#### Dashboard API (`:5006`)
| Method | Endpoint                          | Description              |
|--------|-----------------------------------|--------------------------|
| GET    | `/api/dashboard/metrics`          | Live metrics              |
| GET    | `/api/dashboard/top-clients`      | Top clients               |
| GET    | `/api/dashboard/error-heatmap`    | Error heatmap             |
| GET    | `/api/dashboard/threat-leaderboard` | Threat leaderboard     |
| GET    | `/api/dashboard/system-health`    | System health             |
| GET    | `/api/dashboard/health`           | Health check              |

## Configuration Reference

All configuration lives under the `SentinelGate` section in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=sentinelgate;Username=postgres;Password=postgres"
  },
  "USE_INMEMORY": true,
  "SentinelGate": {
    "RateLimiting": {
      "DefaultAlgorithm": "SlidingWindow",
      "DefaultLimit": 100,
      "DefaultWindowSeconds": 60,
      "DefaultBurstLimit": 20,
      "DefaultRefillRate": 10.0,
      "EnableGlobalRateLimit": true,
      "GlobalLimit": 10000,
      "GlobalWindowSeconds": 60
    },
    "ThreatDetection": {
      "Enabled": true,
      "MonitorThreshold": 30.0,
      "CaptchaThreshold": 50.0,
      "ThrottleThreshold": 70.0,
      "TemporaryBlockThreshold": 85.0,
      "PermanentBlockThreshold": 95.0,
      "DecayRatePerHour": 5.0
    },
    "Redis": {
      "ConnectionString": "localhost:6379",
      "InstanceName": "SentinelGate:",
      "Database": 0,
      "ConnectTimeout": 5000,
      "AbortOnConnectFail": false
    },
    "Analytics": {
      "Enabled": true,
      "HourlyAggregationIntervalMinutes": 60,
      "DailyAggregationIntervalMinutes": 1440,
      "RetentionDays": 90,
      "RequestLogRetentionDays": 30
    },
    "Notifications": {
      "EnableWebhooks": true,
      "EnableEmail": false,
      "WebhookTimeoutSeconds": 10,
      "WebhookRetryCount": 3
    }
  }
}
```

Environment variables use `__` as separator (e.g., `SentinelGate__Redis__ConnectionString`).

## Rate Limiting Algorithms

SentinelGate supports four rate limiting algorithms, configurable per policy:

### Fixed Window
Divides time into fixed-size windows (e.g., 60 seconds). A counter tracks requests within each window. When the window expires, the counter resets. Simple and predictable, but susceptible to burst traffic at window boundaries.

### Sliding Window
Combines the current window's count with a weighted portion of the previous window's count based on elapsed time. Provides smoother rate limiting than fixed windows by eliminating the boundary burst problem.

### Token Bucket
Maintains a bucket of tokens that refills at a constant rate. Each request consumes one token. If the bucket is empty, the request is rejected. Allows controlled bursts up to the bucket capacity while enforcing a sustained average rate.

### Leaky Bucket
Processes requests at a fixed rate, queuing excess requests. If the queue (bucket) overflows, new requests are rejected. Produces a perfectly smooth output rate regardless of input burstiness, ideal for protecting downstream services.

## Threat Scoring

SentinelGate uses a composite threat score (0-100) calculated from multiple behavioral signals. Each signal carries a configurable weight:

| Signal                      | Default Weight | Description                                    |
|-----------------------------|---------------|------------------------------------------------|
| Rate Limit Violations       | 15            | Client exceeds rate limits repeatedly          |
| High 4xx Rate              | 10            | Elevated client-error response rate            |
| Auth Failures              | 20            | Repeated authentication failures               |
| Single Endpoint Hammering  | 10            | Excessive requests to a single endpoint        |
| User Agent Anomaly         | 5             | Missing, spoofed, or suspicious User-Agent     |
| Geo Mismatch               | 10            | Request origin differs from registered region  |
| Payload Anomaly            | 15            | Unusual request body size or content patterns  |

Scores decay at a configurable rate (default: 5 points per hour) to allow recovery from transient issues.

**Threshold Actions:**
| Score Range | Action          |
|-------------|-----------------|
| 0-29        | Allow           |
| 30-49       | Monitor         |
| 50-69       | CAPTCHA         |
| 70-84       | Throttle        |
| 85-94       | Temporary Block |
| 95-100      | Permanent Block |

## Testing

```bash
# Run all tests
dotnet test SentinelGate.slnx

# Run with verbose output
dotnet test SentinelGate.slnx --verbosity normal

# Run specific test project
dotnet test tests/SentinelGate.Tests/SentinelGate.Tests.csproj
```

## Project Structure

```
SentinelGate/
+-- SentinelGate.slnx                    # Solution file
+-- docker-compose.yml                    # Docker Compose orchestration
+-- .dockerignore
+-- .gitignore
+-- README.md
+-- CLAUDE.md
+-- src/
|   +-- SentinelGate.Gateway.API/         # API Gateway service
|   |   +-- Controllers/                  # AdminController, HealthController
|   |   +-- Middleware/                   # Telemetry, Identity, BlockList, RateLimit, ThreatScore
|   |   +-- Program.cs
|   |   +-- Dockerfile
|   |   +-- appsettings.json
|   +-- SentinelGate.RateLimiter.Service/ # Rate limiting service
|   |   +-- Controllers/                  # RateLimitController
|   |   +-- Services/                     # FixedWindow, SlidingWindow, TokenBucket, LeakyBucket
|   |   +-- Dockerfile
|   +-- SentinelGate.Analytics.Service/   # Analytics service
|   |   +-- Controllers/                  # AnalyticsController
|   |   +-- Services/                     # TelemetryIngestion, Aggregation, DataRetention
|   |   +-- Dockerfile
|   +-- SentinelGate.ThreatDetection.Service/ # Threat detection service
|   |   +-- Controllers/                  # ThreatController
|   |   +-- Services/                     # ThreatScoringEngine, ThreatDecay, GeoLookup
|   |   +-- Models/                       # ThreatSignal, UpdateScoreRequest
|   |   +-- Dockerfile
|   +-- SentinelGate.Notification.Service/ # Notification service
|   |   +-- Controllers/                  # NotificationController
|   |   +-- Services/                     # WebhookDispatcher, Slack, Teams, Email, Orchestrator
|   |   +-- Dockerfile
|   +-- SentinelGate.Identity.Service/    # Identity & auth service
|   |   +-- Controllers/                  # AuthController, DeveloperController, TenantsController
|   |   +-- Services/                     # ApiKeyService, TenantService, JwtTokenService
|   |   +-- Dockerfile
|   +-- SentinelGate.Dashboard.API/       # Dashboard API service
|   |   +-- Controllers/                  # DashboardController
|   |   +-- Hubs/                         # DashboardHub (SignalR)
|   |   +-- Services/                     # MetricsBroadcaster, DashboardDataService
|   |   +-- Dockerfile
|   +-- SentinelGate.Shared.Models/       # Shared models library
|   |   +-- Entities/                     # RequestLog, RatePolicy, BlockedClient, ThreatScore, etc.
|   |   +-- DTOs/                         # RateLimitResult, ThreatScoreResult, PolicyDto, etc.
|   |   +-- Enums/                        # RateLimitAlgorithm, BlockType, TenantTier, AlertSeverity
|   |   +-- Configuration/               # SentinelGateOptions
|   +-- SentinelGate.Shared.Infrastructure/ # Shared infrastructure library
|       +-- Data/                         # SentinelGateDbContext, DbInitializer
|       +-- Redis/                        # RedisConnectionManager
|       +-- Extensions/                   # DI registration extensions
+-- tests/
|   +-- SentinelGate.Tests/              # Unit and integration tests
+-- docs/
    +-- API.md                            # Detailed API documentation
    +-- ARCHITECTURE.md                   # System architecture document
```

## Roadmap

### Phase 1 - Core Gateway (Current)
- [x] Middleware pipeline (Telemetry, Identity, BlockList, RateLimit, ThreatScore)
- [x] Four rate limiting algorithms with Redis-backed counters
- [x] Behavioral threat scoring engine with 7 signal types
- [x] Multi-channel notification system (Webhook, Slack, Teams, Email)
- [x] JWT authentication and API key management
- [x] Multi-tenant support with tier-based policies
- [x] Analytics with aggregation and CSV export
- [x] Real-time dashboard with SignalR
- [x] Admin CRUD for policies, block lists, audit logs

### Phase 2 - Advanced Features
- [ ] GraphQL query support for analytics
- [ ] Machine learning anomaly detection
- [ ] Distributed rate limiting across gateway instances
- [ ] Request/response transformation rules
- [ ] Circuit breaker and retry policies
- [ ] OpenTelemetry integration
- [ ] Kubernetes Helm charts

### Phase 3 - Enterprise
- [ ] RBAC with fine-grained permissions
- [ ] SSO integration (SAML, OIDC)
- [ ] Multi-region deployment support
- [ ] Custom plugin system
- [ ] SLA monitoring and reporting
- [ ] Compliance audit trails (SOC 2, GDPR)

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Make your changes and add tests
4. Ensure all tests pass (`dotnet test SentinelGate.slnx`)
5. Commit with a descriptive message
6. Push to your fork and open a Pull Request

Please follow existing code conventions:
- Use file-scoped namespaces
- Follow the existing controller pattern (route prefix, `[ApiController]`, XML doc comments)
- Add unit tests for new business logic
- Keep services stateless where possible

## License

This project is licensed under the MIT License.

```
MIT License

Copyright (c) 2024 SentinelGate

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
