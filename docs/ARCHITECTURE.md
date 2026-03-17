# SentinelGate System Architecture

## Overview

SentinelGate is an intelligent API gateway platform built as a collection of microservices. Each service owns a specific domain and communicates with others through HTTP APIs. The system is designed to be horizontally scalable, fault-tolerant, and operationally transparent.

---

## Microservices Decomposition

The system is decomposed into seven services plus two infrastructure components:

### Application Services

| Service | Responsibility | Port |
|---------|---------------|------|
| **Gateway API** | Entry point for all client traffic. Runs the middleware pipeline that orchestrates identity resolution, block list checking, rate limiting, and threat scoring. | 5000 |
| **RateLimiter Service** | Implements four rate limiting algorithms (Fixed Window, Sliding Window, Token Bucket, Leaky Bucket). Maintains counters in Redis with in-memory fallback. | 5001 |
| **Analytics Service** | Ingests request telemetry, runs background aggregation jobs (hourly/daily), manages data retention, and serves analytics queries. | 5002 |
| **ThreatDetection Service** | Maintains per-client threat scores based on behavioral signals. Runs score decay as a background process. Manages block lists with import/export. | 5003 |
| **Notification Service** | Dispatches alerts through multiple channels (Webhooks, Slack, Microsoft Teams, Email). Manages webhook subscriptions and alert event history. | 5004 |
| **Identity Service** | Handles JWT authentication, API key lifecycle (generate, rotate, revoke), tenant management, and developer portal features. | 5005 |
| **Dashboard API** | Provides real-time operational metrics, error heatmaps, threat leaderboards, and system health. Includes a SignalR hub for live streaming. | 5006 |

### Shared Libraries

| Library | Contents |
|---------|----------|
| **Shared.Models** | Entity classes, DTOs, enums (RateLimitAlgorithm, BlockType, TenantTier, AlertSeverity, ThreatAction), and configuration options. |
| **Shared.Infrastructure** | EF Core DbContext (SentinelGateDbContext), database initializer/seeder, Redis connection manager, and DI registration extensions. |

### Infrastructure

| Component | Purpose | Port |
|-----------|---------|------|
| **PostgreSQL** | System of record for all persistent data: policies, block lists, threat scores, request logs, tenants, API keys, audit logs, alert events, webhooks, aggregates. | 5432 |
| **Redis** | Fast-path cache for rate limit counters, block list lookups, and real-time metrics. All Redis usage is optional; services degrade to in-memory alternatives when Redis is unavailable. | 6379 |

---

## Data Flow Through the Middleware Pipeline

Every request entering through the Gateway API passes through a five-stage middleware pipeline. The order is significant and carefully designed:

```
Request In
    |
    v
+-------------------+
| 1. Telemetry      |  Wraps the entire pipeline. Records start time, captures
|    Middleware      |  response status code, latency, body sizes. Writes a
|                   |  RequestLog entry after the response completes.
+-------------------+
    |
    v
+-------------------+
| 2. Identity       |  Extracts client identity from (in priority order):
|    Resolution     |  1. X-API-Key header -> validates against DB
|    Middleware      |  2. Authorization: Bearer JWT -> decodes claims
|                   |  3. Falls back to client IP address
|                   |  Sets HttpContext.Items["ClientIdentity"] and
|                   |  HttpContext.Items["ClientIp"].
+-------------------+
    |
    v
+-------------------+
| 3. Block List     |  Checks if the resolved client identity or IP is in the
|    Check          |  block list. First checks Redis cache for O(1) lookup,
|    Middleware      |  then falls back to database query. Returns 403 if blocked.
+-------------------+
    |
    v
+-------------------+
| 4. Rate Limit     |  Resolves applicable rate policy for the client/endpoint
|    Middleware      |  combination. Calls the RateLimiter service (or applies
|                   |  locally) to check and decrement the counter. Returns
|                   |  429 with rate limit headers if exceeded.
+-------------------+
    |
    v
+-------------------+
| 5. Threat Score   |  Runs AFTER the response. Evaluates behavioral signals
|    Middleware      |  (4xx rate, auth failures, rate limit violations, etc.)
|                   |  and calls ThreatDetection service to update the score.
|                   |  If score exceeds threshold, auto-blocks the client.
+-------------------+
    |
    v
  Controller
  (processes request)
```

---

## Data Storage Strategy

### PostgreSQL (System of Record)

All persistent data is stored in PostgreSQL through Entity Framework Core. The `SentinelGateDbContext` manages these entity sets:

| Entity | Table | Purpose |
|--------|-------|---------|
| `RatePolicy` | `RatePolicies` | Rate limiting policy definitions |
| `BlockedClient` | `BlockedClients` | Block list entries (soft-delete enabled) |
| `ThreatScore` | `ThreatScores` | Per-client composite threat scores |
| `RequestLog` | `RequestLogs` | Individual request telemetry records |
| `HourlyAggregate` | `HourlyAggregates` | Pre-computed hourly traffic statistics |
| `DailyAggregate` | `DailyAggregates` | Pre-computed daily traffic statistics |
| `ApiKeyEntity` | `ApiKeys` | API key records with hashed keys |
| `Tenant` | `Tenants` | Multi-tenant organization records |
| `AuditLog` | `AuditLogs` | Administrative action audit trail |
| `WebhookSubscription` | `WebhookSubscriptions` | Registered webhook endpoints |
| `AlertEvent` | `AlertEvents` | Notification/alert history |

### Redis (Fast-Path Cache)

Redis is used for data that requires sub-millisecond access on the hot path:

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `rl:fw:{clientKey}:{windowId}` | String (counter) | Fixed window request count |
| `rl:sw:{clientKey}` | Sorted Set | Sliding window request timestamps |
| `rl:tb:{clientKey}` | Hash | Token bucket state (tokens, lastRefill) |
| `rl:lb:{clientKey}` | Hash | Leaky bucket state (level, lastDrain) |
| `blocked:{clientIdentity}` | String | Block list cache (reason) |
| `blocked:ip:{ipAddress}` | String | IP-based block list cache |

### In-Memory Fallback

When Redis is unavailable, services transparently fall back:
- Rate limit counters use `ConcurrentDictionary` with periodic cleanup
- Block list checks fall back to database queries
- This ensures the system continues operating (with slightly higher latency) even if Redis is down

---

## Communication Patterns

### Synchronous HTTP

Inter-service communication uses named `HttpClient` instances registered via DI:

```
Gateway API ---HTTP POST---> ThreatDetection Service  (score updates)
Gateway API ---HTTP POST---> Analytics Service         (telemetry events)
Gateway API ---HTTP POST---> Notification Service      (alert dispatch)
```

Each HttpClient has a 5-second timeout to prevent cascading failures. If a downstream service is unavailable, the gateway logs a warning and continues processing (fail-open for non-critical paths).

### SignalR (Real-time)

The Dashboard API uses ASP.NET Core SignalR to push live metrics to connected dashboard clients:

```
Dashboard API ---> SignalR Hub ---> Connected Browsers/Clients
```

The `MetricsBroadcaster` background service periodically collects metrics and broadcasts them through the hub.

### Background Services

Several services run `BackgroundService` (hosted service) instances:

| Service | Background Job | Interval |
|---------|---------------|----------|
| Analytics | `AggregationBackgroundService` - Computes hourly/daily aggregates | Configurable (default: 60min / 1440min) |
| Analytics | `DataRetentionService` - Purges old request logs and aggregates | Daily |
| ThreatDetection | `ThreatDecayBackgroundService` - Decays threat scores over time | Periodic |
| Dashboard | `MetricsBroadcaster` - Broadcasts live metrics via SignalR | Every few seconds |

---

## Scaling Strategy

### Horizontal Scaling

Each microservice is stateless (state lives in PostgreSQL and Redis) and can be horizontally scaled:

```
                    +-- Gateway API (1)
Load Balancer ----> +-- Gateway API (2)
                    +-- Gateway API (N)
```

Considerations for multi-instance deployment:

1. **Rate Limiting:** Redis-backed counters are shared across all gateway instances, ensuring accurate global rate limiting. If using in-memory fallback, each instance tracks independently (not recommended for production).

2. **Threat Scores:** The PostgreSQL-backed scoring engine ensures consistent scores across instances. Updates are serialized through the database.

3. **Block List:** The Redis cache layer provides consistent block list enforcement across instances. TTL-based expiration ensures cache coherence.

4. **Analytics:** Telemetry writes are append-only and safe for concurrent multi-instance writes. Aggregation jobs should run on a single instance (use distributed locking or leader election in production).

### Database Scaling

- **Read replicas:** Analytics queries can be directed to read replicas to offload the primary.
- **Partitioning:** `RequestLogs` can be range-partitioned by `Timestamp` for efficient pruning and query performance.
- **Connection pooling:** Use PgBouncer or similar connection poolers when running many service instances.

### Redis Scaling

- **Redis Sentinel:** For high availability with automatic failover.
- **Redis Cluster:** For horizontal scaling of the cache layer when counter volume exceeds single-node capacity.

### Container Orchestration

The Docker Compose setup is designed for development and single-node deployment. For production:
- Deploy on Kubernetes with individual Deployments per service
- Use Kubernetes Services for inter-service DNS resolution
- Configure HPA (Horizontal Pod Autoscaler) based on CPU/memory or custom metrics
- Use persistent volumes for PostgreSQL and Redis

---

## Security Architecture

### Authentication Flow

1. Client registers via `/api/auth/register` and receives a JWT token
2. Client can also generate API keys via `/api/developer/keys`
3. The Gateway's Identity Resolution middleware validates:
   - `X-API-Key` header: Looked up in the database, resolved to tenant
   - `Authorization: Bearer` header: JWT decoded and validated
   - Fallback: Client IP address used as identity

### Multi-Tenancy

Tenants are isolated through:
- Tenant-scoped API keys
- Tier-based rate limiting policies (Free: 100 req/min, Pro: 500 req/min, Enterprise: custom)
- Tenant-specific webhook subscriptions
- Per-tenant usage analytics

### Audit Trail

All administrative actions (policy changes, block list modifications, score resets) are logged to the `AuditLogs` table with:
- Actor identity
- Action performed
- Affected resource
- Timestamp
- Client IP address

---

## Failure Modes and Resilience

| Failure | Impact | Mitigation |
|---------|--------|------------|
| Redis down | Rate limiting falls back to in-memory; block list checks go to DB | Automatic fallback, warning logged |
| PostgreSQL down | Gateway health reports degraded; writes fail | Health check returns 503; upstream load balancer can route away |
| ThreatDetection service down | Threat scoring skipped for requests | Gateway continues with warning; fail-open |
| Notification service down | Alerts not dispatched | Gateway continues; alerts queued for retry |
| Analytics service down | Telemetry not recorded | Gateway continues; no data loss for primary function |

The system follows a **fail-open** philosophy for non-critical paths: the Gateway will always attempt to proxy requests even if auxiliary services are unavailable. Only the block list check and rate limit check can reject requests.
