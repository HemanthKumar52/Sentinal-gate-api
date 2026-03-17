# SentinelGate API Documentation

Complete API reference for all SentinelGate microservices.

---

## Gateway API (Port 5000)

### Health Check

**GET** `/health`

Returns service health status including Redis and database connectivity.

**Response 200:**
```json
{
  "status": "healthy",
  "service": "SentinelGate.Gateway",
  "timestamp": "2024-01-15T10:30:00Z",
  "redis": "connected",
  "database": "connected"
}
```

**Response 503 (degraded):**
```json
{
  "status": "degraded",
  "service": "SentinelGate.Gateway",
  "timestamp": "2024-01-15T10:30:00Z",
  "redis": "disconnected",
  "database": "connected"
}
```

---

### Rate Policies

#### List Policies

**GET** `/api/admin/policies?page=1&pageSize=20`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| page      | int  | 1       | Page number (1-based) |
| pageSize  | int  | 20      | Items per page (max 100) |

**Response 200:**
```json
{
  "data": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "name": "Default API Limit",
      "algorithm": "SlidingWindow",
      "limit": 100,
      "windowSeconds": 60,
      "burstLimit": 20,
      "refillRate": 10.0,
      "leakyCapacity": null,
      "leakyRate": null,
      "endpointPattern": "*",
      "tenantId": null,
      "priority": 0,
      "isGlobal": true,
      "isEnabled": true,
      "createdAt": "2024-01-15T10:00:00Z",
      "updatedAt": "2024-01-15T10:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

#### Create Policy

**POST** `/api/admin/policies`

**Request Body:**
```json
{
  "name": "Premium Tier Limit",
  "algorithm": "TokenBucket",
  "limit": 500,
  "windowSeconds": 60,
  "burstLimit": 50,
  "refillRate": 25.0,
  "leakyCapacity": null,
  "leakyRate": null,
  "endpointPattern": "/api/*",
  "tenantId": null,
  "priority": 10,
  "isGlobal": false,
  "isEnabled": true
}
```

**Response 201:**
```json
{
  "id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "name": "Premium Tier Limit",
  "algorithm": "TokenBucket",
  "limit": 500,
  "windowSeconds": 60,
  "burstLimit": 50,
  "refillRate": 25.0,
  "leakyCapacity": null,
  "leakyRate": null,
  "endpointPattern": "/api/*",
  "tenantId": null,
  "priority": 10,
  "isGlobal": false,
  "isEnabled": true,
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-01-15T10:30:00Z"
}
```

#### Update Policy

**PUT** `/api/admin/policies/{id}`

Request body is the same as Create Policy. Returns the updated policy object.

**Response 200:** Updated policy DTO.

**Response 404:**
```json
{
  "error": "Not Found",
  "message": "Policy b2c3d4e5-f6a7-8901-bcde-f12345678901 not found"
}
```

#### Delete Policy

**DELETE** `/api/admin/policies/{id}`

**Response 204:** No content.

**Response 404:**
```json
{
  "error": "Not Found",
  "message": "Policy b2c3d4e5-f6a7-8901-bcde-f12345678901 not found"
}
```

---

### Block List

#### List Blocked Clients

**GET** `/api/admin/blocklist?page=1&pageSize=20&activeOnly=true`

| Parameter  | Type | Default | Description |
|------------|------|---------|-------------|
| page       | int  | 1       | Page number |
| pageSize   | int  | 20      | Items per page (max 100) |
| activeOnly | bool | true    | Filter to active blocks |

**Response 200:**
```json
{
  "data": [
    {
      "id": "c3d4e5f6-a7b8-9012-cdef-123456789012",
      "clientIdentity": "abusive-client-key",
      "ipAddress": "192.168.1.100",
      "cidrRange": null,
      "reason": "Excessive rate limit violations",
      "blockType": "Temporary",
      "threatScore": 87.5,
      "expiresAt": "2024-01-16T10:00:00Z",
      "isActive": true,
      "isDeleted": false,
      "createdAt": "2024-01-15T10:00:00Z",
      "createdBy": "system"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

#### Add to Block List

**POST** `/api/admin/blocklist`

**Request Body:**
```json
{
  "clientIdentity": "abusive-client-key",
  "ipAddress": "192.168.1.100",
  "cidrRange": null,
  "reason": "Manual block for abuse",
  "blockType": "Permanent",
  "expiresAt": null
}
```

At least one of `clientIdentity`, `ipAddress`, or `cidrRange` must be provided.

**Response 201:** The created block entry.

**Response 400:**
```json
{
  "error": "Bad Request",
  "message": "At least one of ClientIdentity, IpAddress, or CidrRange must be provided"
}
```

#### Remove from Block List

**DELETE** `/api/admin/blocklist/{id}`

Performs a soft delete (marks as inactive and deleted).

**Response 204:** No content.

**Response 404:**
```json
{
  "error": "Not Found",
  "message": "Block entry c3d4e5f6-a7b8-9012-cdef-123456789012 not found"
}
```

---

### Audit Log

#### Query Audit Log

**GET** `/api/admin/audit-log?actor=admin&action=CreatePolicy&from=2024-01-01&to=2024-01-31&page=1&pageSize=20`

| Parameter | Type     | Default | Description |
|-----------|----------|---------|-------------|
| actor     | string?  | null    | Filter by actor (partial match) |
| action    | string?  | null    | Filter by action (exact match) |
| from      | DateTime?| null    | Start date (inclusive) |
| to        | DateTime?| null    | End date (inclusive) |
| page      | int      | 1       | Page number |
| pageSize  | int      | 20      | Items per page (max 100) |

**Response 200:**
```json
{
  "data": [
    {
      "id": "d4e5f6a7-b8c9-0123-def0-123456789abc",
      "actor": "admin",
      "action": "CreatePolicy",
      "resource": "RatePolicy",
      "resourceId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "details": "Created policy 'Default API Limit'",
      "ipAddress": "127.0.0.1",
      "timestamp": "2024-01-15T10:30:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

---

### Threat Scores

#### List Threat Scores

**GET** `/api/admin/threat-scores?page=1&pageSize=20`

**Response 200:**
```json
{
  "data": [
    {
      "id": "e5f6a7b8-c9d0-1234-ef01-23456789abcd",
      "clientIdentity": "suspicious-client",
      "score": 72.5,
      "rateLimitViolations": 3,
      "high4xxRate": 2,
      "authFailures": 1,
      "singleEndpointHammering": 0,
      "userAgentAnomaly": 1,
      "geoMismatch": 0,
      "payloadAnomaly": 0,
      "lastUpdated": "2024-01-15T10:30:00Z",
      "lastDecayed": "2024-01-15T10:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

#### Reset Threat Score

**POST** `/api/admin/threat-scores/{clientId}/reset`

Resets all signal counters and score to zero.

**Response 200:** The reset threat score entity.

**Response 404:**
```json
{
  "error": "Not Found",
  "message": "Threat score for client 'suspicious-client' not found"
}
```

---

## RateLimiter Service (Port 5001)

### Check Rate Limit

**POST** `/api/ratelimit/check`

**Request Body:**
```json
{
  "clientIdentity": "client-api-key-123",
  "endpointPath": "/api/data",
  "algorithm": "SlidingWindow",
  "limit": 100,
  "windowSeconds": 60,
  "burstLimit": 20,
  "refillRate": 10.0
}
```

| Field           | Type   | Default       | Description |
|-----------------|--------|---------------|-------------|
| clientIdentity  | string | required      | Client identifier |
| endpointPath    | string?| null          | Endpoint being accessed |
| algorithm       | enum   | FixedWindow   | Algorithm: FixedWindow, SlidingWindow, TokenBucket, LeakyBucket |
| limit           | int    | 100           | Max requests allowed |
| windowSeconds   | int    | 60            | Time window in seconds |
| burstLimit      | int?   | null          | Max burst (Token Bucket) |
| refillRate      | double?| null          | Refill rate per second (Token Bucket) |

**Response 200 (allowed):**
```json
{
  "isAllowed": true,
  "limit": 100,
  "remaining": 95,
  "resetAt": "2024-01-15T10:31:00Z",
  "retryAfter": null
}
```

**Response 429 (rate limited):**
```json
{
  "isAllowed": false,
  "limit": 100,
  "remaining": 0,
  "resetAt": "2024-01-15T10:31:00Z",
  "retryAfter": "00:00:45"
}
```

Response headers included: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`, `Retry-After` (on 429).

### Get Counter State

**GET** `/api/ratelimit/counters/{clientIdentity}`

**Response 200:**
```json
{
  "clientIdentity": "client-api-key-123",
  "redisConnected": true,
  "error": null,
  "counters": [
    {
      "key": "rl:fw:client-api-key-123:1705312200",
      "type": "String",
      "value": "5",
      "ttlSeconds": 45
    },
    {
      "key": "rl:sw:client-api-key-123",
      "type": "SortedSet",
      "value": "entries: 12",
      "ttlSeconds": 60
    }
  ]
}
```

### Reset Counters

**DELETE** `/api/ratelimit/counters/{clientIdentity}`

**Response 200:**
```json
{
  "clientIdentity": "client-api-key-123",
  "deletedKeys": 3
}
```

**Response 503:**
```json
{
  "error": "Redis unavailable. Cannot reset counters."
}
```

### Health Check

**GET** `/api/ratelimit/health`

**Response 200:**
```json
{
  "status": "healthy",
  "service": "SentinelGate.RateLimiter.Service",
  "timestamp": "2024-01-15T10:30:00Z",
  "redis": "connected"
}
```

---

## Analytics Service (Port 5002)

### Traffic Summary

**GET** `/api/analytics/summary?from=2024-01-01T00:00:00Z&to=2024-01-15T23:59:59Z`

| Parameter | Type     | Required | Description |
|-----------|----------|----------|-------------|
| from      | DateTime | Yes      | Start of time range |
| to        | DateTime | Yes      | End of time range |

**Response 200:**
```json
{
  "totalRequests": 125430,
  "blockedRequests": 342,
  "rateLimitedRequests": 1205,
  "avgLatencyMs": 45.2,
  "p95LatencyMs": 120.5,
  "p99LatencyMs": 250.0,
  "uniqueClients": 89,
  "errorRate": 2.3
}
```

### Endpoint Statistics

**GET** `/api/analytics/endpoints?from=2024-01-01T00:00:00Z&to=2024-01-15T23:59:59Z`

**Response 200:**
```json
[
  {
    "endpointPath": "/api/data",
    "httpMethod": "GET",
    "totalRequests": 45000,
    "avgLatencyMs": 32.1,
    "errorRate": 1.2,
    "p95LatencyMs": 85.0
  }
]
```

### Top Clients

**GET** `/api/analytics/clients/top?from=2024-01-01T00:00:00Z&to=2024-01-15T23:59:59Z&top=10`

| Parameter | Type     | Default | Description |
|-----------|----------|---------|-------------|
| from      | DateTime | required| Start time |
| to        | DateTime | required| End time |
| top       | int      | 10      | Number of top clients |

**Response 200:**
```json
[
  {
    "clientIdentity": "client-api-key-123",
    "totalRequests": 15000,
    "avgLatencyMs": 28.5,
    "errorRate": 0.5
  }
]
```

### Latency Percentiles

**GET** `/api/analytics/latency/percentiles?from=2024-01-01T00:00:00Z&to=2024-01-15T23:59:59Z`

**Response 200:**
```json
{
  "p50": 25.0,
  "p75": 55.0,
  "p90": 95.0,
  "p95": 120.5,
  "p99": 250.0
}
```

### Export Raw Logs

**GET** `/api/analytics/reports/export?from=2024-01-01T00:00:00Z&to=2024-01-15T23:59:59Z`

Returns a CSV file download with columns: `Id, ClientIdentity, ClientIp, ApiKey, TenantId, EndpointPath, HttpMethod, ResponseStatusCode, LatencyMs, RequestBodySize, ResponseSize, GeoCountry, UserAgent, IsBlocked, IsRateLimited, Timestamp`.

**Response 200:** `Content-Type: text/csv`

---

## Threat Detection Service (Port 5003)

### Update Threat Score

**POST** `/api/threat/score/update`

**Request Body:**
```json
{
  "clientIdentity": "suspicious-client",
  "ipAddress": "203.0.113.50",
  "signal": "RateLimitViolation"
}
```

Valid signals: `RateLimitViolation`, `High4xxRate`, `AuthFailure`, `SingleEndpointHammering`, `UserAgentAnomaly`, `GeoMismatch`, `PayloadAnomaly`.

**Response 200:**
```json
{
  "clientIdentity": "suspicious-client",
  "score": 45.0,
  "action": "Monitor",
  "signals": {
    "rateLimitViolations": 3,
    "high4xxRate": 0,
    "authFailures": 0,
    "singleEndpointHammering": 0,
    "userAgentAnomaly": 0,
    "geoMismatch": 0,
    "payloadAnomaly": 0
  }
}
```

### Get Threat Score

**GET** `/api/threat/score/{clientIdentity}`

**Response 200:** Same as Update response format.

**Response 404:**
```json
{
  "message": "No threat score found for 'unknown-client'."
}
```

### Reset Threat Score

**POST** `/api/threat/score/{clientIdentity}/reset`

**Response 200:**
```json
{
  "message": "Threat score reset for 'suspicious-client'."
}
```

### List All Scores

**GET** `/api/threat/scores?page=1&pageSize=25`

**Response 200:**
```json
{
  "page": 1,
  "pageSize": 25,
  "totalCount": 10,
  "totalPages": 1,
  "items": [
    {
      "id": "...",
      "clientIdentity": "suspicious-client",
      "score": 72.5,
      "rateLimitViolations": 3,
      "high4xxRate": 2,
      "authFailures": 1,
      "singleEndpointHammering": 0,
      "userAgentAnomaly": 1,
      "geoMismatch": 0,
      "payloadAnomaly": 0,
      "lastUpdated": "2024-01-15T10:30:00Z",
      "lastDecayed": "2024-01-15T10:00:00Z"
    }
  ]
}
```

### Block List Endpoints

#### List Blocked Clients

**GET** `/api/threat/blocklist?page=1&pageSize=25&activeOnly=true`

**Response 200:** Paginated list of blocked clients.

#### Block a Client

**POST** `/api/threat/blocklist`

**Request Body:**
```json
{
  "clientIdentity": "bad-actor",
  "ipAddress": "203.0.113.100",
  "cidrRange": null,
  "reason": "Automated threat detection",
  "blockType": "Temporary",
  "expiresAt": "2024-01-16T10:00:00Z"
}
```

**Response 201:** The created block entry.

#### Unblock a Client

**DELETE** `/api/threat/blocklist/{id}`

**Response 200:**
```json
{
  "message": "Client 'c3d4e5f6-...' unblocked."
}
```

#### Import Block List

**POST** `/api/threat/blocklist/import`

**Request Body:**
```json
[
  {
    "clientIdentity": "bad-actor-1",
    "ipAddress": "203.0.113.10",
    "reason": "Known attacker",
    "blockType": "Permanent"
  },
  {
    "ipAddress": "198.51.100.0",
    "cidrRange": "198.51.100.0/24",
    "reason": "Malicious network",
    "blockType": "Permanent"
  }
]
```

**Response 200:**
```json
{
  "imported": 2,
  "skipped": 0
}
```

#### Export Block List

**GET** `/api/threat/blocklist/export`

**Response 200:** JSON array of all active blocked clients.

---

## Notification Service (Port 5004)

### Send Notification

**POST** `/api/notifications/send`

**Request Body:**
```json
{
  "eventType": "threat.high_score",
  "severity": "Warning",
  "message": "Client suspicious-client reached threat score 85",
  "clientIdentity": "suspicious-client"
}
```

Severity values: `Info`, `Warning`, `Critical`.

**Response 200:**
```json
{
  "id": "f6a7b8c9-d0e1-2345-f012-3456789abcde",
  "status": "dispatched"
}
```

### List Alert Events

**GET** `/api/notifications/events?page=1&pageSize=20`

**Response 200:**
```json
{
  "data": [
    {
      "id": "f6a7b8c9-d0e1-2345-f012-3456789abcde",
      "eventType": "threat.high_score",
      "severity": "Warning",
      "details": "Client suspicious-client reached threat score 85",
      "clientIdentity": "suspicious-client",
      "isAcknowledged": false,
      "createdAt": "2024-01-15T10:30:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

### Acknowledge Alert

**POST** `/api/notifications/events/{id}/acknowledge`

**Response 200:**
```json
{
  "message": "Acknowledged",
  "id": "f6a7b8c9-d0e1-2345-f012-3456789abcde"
}
```

### Webhook Management

#### List Webhooks

**GET** `/api/notifications/webhooks`

**Response 200:**
```json
[
  {
    "id": "a7b8c9d0-e1f2-3456-0123-456789abcdef",
    "tenantId": "tenant-123",
    "url": "https://example.com/webhook",
    "events": "threat.*,ratelimit.exceeded",
    "secret": "whsec_...",
    "isActive": true,
    "createdAt": "2024-01-15T10:00:00Z"
  }
]
```

#### Register Webhook

**POST** `/api/notifications/webhooks`

**Request Body:**
```json
{
  "tenantId": "tenant-123",
  "url": "https://example.com/webhook",
  "events": "threat.*,ratelimit.exceeded",
  "secret": "whsec_mysecretkey"
}
```

**Response 201:** The created webhook subscription.

#### Delete Webhook

**DELETE** `/api/notifications/webhooks/{id}`

**Response 204:** No content.

---

## Identity Service (Port 5005)

### Authentication

#### Login

**POST** `/api/auth/login`

**Request Body:**
```json
{
  "username": "developer@example.com",
  "password": "mypassword",
  "role": "developer"
}
```

Note: Demo mode accepts any credentials.

**Response 200:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "tokenType": "Bearer"
}
```

#### Register

**POST** `/api/auth/register`

**Request Body:**
```json
{
  "name": "My Company",
  "tier": "Pro"
}
```

Tier values: `Free`, `Pro`, `Enterprise`.

**Response 201:**
```json
{
  "tenantId": "b8c9d0e1-f2a3-4567-0123-456789abcdef",
  "name": "My Company",
  "tier": "Pro",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 3600,
  "tokenType": "Bearer"
}
```

#### Get Current User

**GET** `/api/auth/me`

Requires `Authorization: Bearer {token}` header.

**Response 200:**
```json
{
  "userId": "b8c9d0e1-f2a3-4567-0123-456789abcdef",
  "role": "developer",
  "claims": [
    { "type": "userId", "value": "b8c9d0e1-..." },
    { "type": "role", "value": "developer" }
  ]
}
```

### Developer Portal (Requires Auth)

All developer endpoints require `Authorization: Bearer {token}`.

#### List API Keys

**GET** `/api/developer/keys`

**Response 200:**
```json
[
  {
    "id": "c9d0e1f2-a3b4-5678-1234-56789abcdef0",
    "name": "Production Key",
    "keyPrefix": "sg_prod_abc...",
    "tenantId": "b8c9d0e1-...",
    "isActive": true,
    "createdAt": "2024-01-15T10:00:00Z",
    "expiresAt": "2025-01-15T10:00:00Z"
  }
]
```

#### Create API Key

**POST** `/api/developer/keys`

**Request Body:**
```json
{
  "name": "Production Key",
  "tenantId": "auto-populated",
  "expiresInDays": 365
}
```

**Response 201:** The created API key DTO (includes the full key value only on creation).

#### Rotate API Key

**POST** `/api/developer/keys/{id}/rotate`

**Response 200:** New API key DTO with rotated key value.

#### Revoke API Key

**DELETE** `/api/developer/keys/{id}`

**Response 204:** No content.

#### Get Usage Stats

**GET** `/api/developer/usage`

**Response 200:**
```json
{
  "tenantId": "b8c9d0e1-...",
  "totalRequests": 15000,
  "rateLimitedRequests": 25,
  "blockedRequests": 0,
  "avgLatencyMs": 28.5,
  "tier": "Pro",
  "rateLimit": 500,
  "usage": 15000
}
```

### Tenant Management (Requires Auth)

#### List Tenants

**GET** `/api/tenants`

**Response 200:**
```json
[
  {
    "id": "b8c9d0e1-f2a3-4567-0123-456789abcdef",
    "name": "My Company",
    "tier": "Pro",
    "isActive": true,
    "createdAt": "2024-01-15T10:00:00Z"
  }
]
```

#### Get Tenant

**GET** `/api/tenants/{id}`

**Response 200:** Single tenant object.

**Response 404:**
```json
{
  "message": "Tenant not found"
}
```

#### Create Tenant

**POST** `/api/tenants`

**Request Body:**
```json
{
  "name": "New Organization",
  "tier": "Enterprise"
}
```

**Response 201:** The created tenant.

#### Update Tenant Tier

**PUT** `/api/tenants/{id}/tier`

**Request Body:**
```json
{
  "tier": "Enterprise"
}
```

**Response 204:** No content.

**Response 400:**
```json
{
  "message": "Invalid tier. Valid values: Free, Pro, Enterprise"
}
```

---

## Dashboard API (Port 5006)

### Live Metrics

**GET** `/api/dashboard/metrics`

**Response 200:**
```json
{
  "requestsPerMinute": 245,
  "avgLatencyMs": 32.5,
  "errorRate": 1.8,
  "activeClients": 42,
  "blockedClients": 3,
  "rateLimitedRequests": 15,
  "topEndpoints": [
    { "path": "/api/data", "count": 120 }
  ]
}
```

### Top Clients

**GET** `/api/dashboard/top-clients?hours=1`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| hours     | int  | 1       | Time window in hours |

**Response 200:**
```json
[
  {
    "clientIdentity": "client-123",
    "requestCount": 500,
    "avgLatencyMs": 25.0,
    "errorCount": 2
  }
]
```

### Error Heatmap

**GET** `/api/dashboard/error-heatmap?days=7`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| days      | int  | 7       | Number of days |

**Response 200:**
```json
[
  {
    "hour": 14,
    "day": "2024-01-15",
    "errorCount": 25,
    "totalRequests": 1200
  }
]
```

### Threat Leaderboard

**GET** `/api/dashboard/threat-leaderboard`

**Response 200:**
```json
[
  {
    "clientIdentity": "suspicious-client",
    "score": 72.5,
    "action": "Throttle"
  }
]
```

### System Health

**GET** `/api/dashboard/system-health`

**Response 200:**
```json
{
  "gateway": "healthy",
  "rateLimiter": "healthy",
  "analytics": "healthy",
  "threatDetection": "healthy",
  "notifications": "healthy",
  "identity": "healthy",
  "database": "connected",
  "redis": "connected"
}
```

### Health Check

**GET** `/api/dashboard/health`

**Response 200:**
```json
{
  "status": "Healthy",
  "service": "SentinelGate.Dashboard.API",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

---

## SignalR Hub

The Dashboard API exposes a SignalR hub for real-time metric streaming.

**Hub URL:** `http://localhost:5006/hubs/dashboard`

The hub broadcasts live metrics periodically to all connected clients.

---

## Common Response Patterns

### Pagination
All paginated endpoints return:
```json
{
  "data": [...],
  "page": 1,
  "pageSize": 20,
  "totalCount": 100,
  "totalPages": 5
}
```

### Errors
Standard error responses follow:
```json
{
  "error": "Error Type",
  "message": "Human-readable description"
}
```

### Rate Limit Headers
All requests through the Gateway receive rate limit headers:
- `X-RateLimit-Limit` - Maximum requests allowed
- `X-RateLimit-Remaining` - Requests remaining in current window
- `X-RateLimit-Reset` - Unix timestamp when the window resets
- `Retry-After` - Seconds to wait (only on 429 responses)
