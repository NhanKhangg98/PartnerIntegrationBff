# PartnerIntegrationBff

BFF (Backend-for-Frontend) Microservice built with **.NET 8** that accepts, validates, and enriches partner transactions before pushing them to a message queue for downstream legacy system processing.

---

## Table of Contents

- [Architecture Decisions](#architecture-decisions)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Running the Application](#running-the-application)
  - [Option A — Docker (Recommended)](#option-a--docker-recommended)
  - [Option B — Local (dotnet run)](#option-b--local-dotnet-run)
- [Running Tests](#running-tests)
- [API Reference](#api-reference)
- [Security](#security)

---

## Architecture Decisions

### Clean Architecture (3-layer)

```
PartnerIntegration.Api            → HTTP layer: Controllers, Middleware, DI wiring
PartnerIntegration.Core           → Business logic: Interfaces, DTOs, Validators, Services
PartnerIntegration.Infrastructure → External concerns: RabbitMQ publisher, HTTP clients
```

**Why?** Each layer only depends on layers below it. `Core` has zero infrastructure dependencies — it can be unit-tested without any real RabbitMQ or HTTP connections.

### Validation — FluentValidation

`AddFluentValidationAutoValidation()` integrates with the ASP.NET model binding pipeline. Invalid payloads are rejected with `400 Bad Request` before they ever reach the service layer — no manual null-checks inside business logic.

Rules enforced:
- All fields **required**
- `amount` **> 0**
- `currency` must be one of: `VND, USD, EUR, JPY, GBP, SGD, AUD, CAD`
- `timestamp` must be within **last 24 hours** and no more than **5 minutes in the future**

### Resilience — Polly v8

Pipeline order (outermost → innermost):

```
[Total Timeout 30s] → [Retry 3× exp+jitter] → [Circuit Breaker] → [Per-attempt Timeout 3s]
```

| Strategy | Config | Rationale |
|---|---|---|
| Total Timeout | 30 s | Hard cap on entire operation including all retries |
| Retry | 3×, 200 ms base, exponential + jitter | Handles transient 5xx / 408 / `TimeoutRejectedException` |
| Circuit Breaker | 50% failure ratio, min 5 calls, 10 s break | Prevents hammering a dead service |
| Per-attempt Timeout | 3 s | Cuts off individual slow calls; triggers retry |

`TimeoutRejectedException` (thrown by the inner Polly timeout) is explicitly included in `ShouldHandle` so slow requests correctly trigger the retry strategy.

### Mock Partner Verification Endpoint

The downstream partner verification service is **mocked inside the same application** at:

```
GET /api/internal/mock/partners/{partnerId}/verify
```

- **30%** probability → delays 5 s (longer than per-attempt timeout → triggers `TimeoutRejectedException` → Polly retries)
- **70%** probability → returns `200 OK` immediately

### Async Messaging — RabbitMQ

- `IMessagePublisher` interface in `Core` — business logic never references RabbitMQ directly
- `RabbitMqPublisher` in `Infrastructure` — lazy connection with `SemaphoreSlim` double-check locking
- `DeliveryMode.Persistent` + `durable: true` queue — messages survive RabbitMQ restarts
- Reconnect-aware: checks `IsOpen` on the channel before publishing

### Security

| Mechanism | Implementation |
|---|---|
| **API Key** | `ApiKeyMiddleware` validates `X-Api-Key` header on every request |
| **Rate Limiting** | Global: 100 req/min per IP · Transaction endpoint: 20 req/min |

API Key is read from `Security:ApiKeys` (comma-separated for multi-key rotation support).  
Routes excluded from auth: `/swagger/**`, `/api/internal/mock/**`.

### Global Exception Handler

Implements `IExceptionHandler` (.NET 8 native) — returns RFC 7807 `ProblemDetails` with `traceId` for log correlation. `exception.Message` is **never** exposed to the client.

---

## Project Structure

```
PartnerIntegrationBff/
├── Dockerfile
├── docker-compose.yml
├── .dockerignore
├── PartnerIntegration.Api/
│   ├── Controllers/
│   │   ├── PartnerTransactionsController.cs   # POST /api/v1/partner/transactions
│   │   └── MockPartnerController.cs           # GET /api/internal/mock/partners/{id}/verify
│   ├── Middlewares/
│   │   ├── ExceptionHandler.cs                # Global RFC 7807 error handler
│   │   └── ApiKeyMiddleware.cs                # X-Api-Key authentication
│   └── Program.cs                             # DI wiring, Polly pipeline, Rate Limiting
├── PartnerIntegration.Core/
│   ├── DTOs/
│   │   ├── PartnerTransactionRequestDto.cs
│   │   └── TransactionIngestionResponseDto.cs
│   ├── Interfaces/
│   │   ├── IMessagePublisher.cs
│   │   ├── IPartnerVerificationClient.cs
│   │   └── ITransactionProcessingService.cs
│   ├── Services/
│   │   └── TransactionProcessingService.cs    # Orchestrates verify → publish flow
│   └── Validators/
│       └── PartnerTransactionValidator.cs     # FluentValidation rules
├── PartnerIntegration.Infrastructure/
│   ├── Clients/
│   │   └── PartnerVerificationClient.cs       # Typed HttpClient
│   ├── DependencyInjection/
│   │   └── RabbitMqExtensions.cs
│   └── Messaging/
│       ├── RabbitMqOptions.cs
│       └── RabbitMqPublisher.cs               # Thread-safe singleton publisher
└── PartnerIntegration.UnitTests/
    ├── Helpers/
    │   ├── CountingHttpMessageHandler.cs      # Mock HTTP handler for retry tests
    │   └── InMemoryMessagePublisher.cs        # In-memory queue for integration tests
    ├── Integration/
    │   ├── RetryResilienceTests.cs            # Polly retry with real pipeline
    │   └── TransactionIngestionIntegrationTests.cs  # WebApplicationFactory E2E tests
    ├── Services/
    │   └── TransactionProcessingServiceTests.cs
    └── Validators/
        └── PartnerTransactionValidatorTests.cs
```

---

## Prerequisites

### Docker (Option A)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) with **WSL2 or Hyper-V** enabled
- Windows: requires Virtualization enabled in BIOS + Virtual Machine Platform Windows feature

### Local (Option B)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- RabbitMQ running locally (default: `localhost:5672`, guest/guest)
  - Docker: `docker run -d -p 5672:5672 -p 15672:15672 rabbitmq:3-management-alpine`
  - Or install via [Chocolatey](https://chocolatey.org/): `choco install rabbitmq -y`

---

## Running the Application

### Option A — Docker (Recommended)

Starts both the API and RabbitMQ with a single command:

```bash
docker compose up --build
```

| Service | URL |
|---|---|
| API | http://localhost:8080 |
| Swagger UI | http://localhost:8080/swagger |
| RabbitMQ Management | http://localhost:15672 (guest / guest) |

**Stop:**
```bash
docker compose down        # keep RabbitMQ data
docker compose down -v     # wipe RabbitMQ data
```

**Default API Key (Docker):** `docker-local-api-key`

---

### Option B — Local (dotnet run)

**1. Configure appsettings** (or use environment variables):

`appsettings.json` already has sensible defaults for local development:
- `PartnerService:BaseUrl` → `http://localhost:5000`
- `RabbitMq:Host` → `localhost`
- `Security:ApiKeys` → `dev-api-key-change-me-in-production`

**2. Run the API:**

```bash
dotnet run --project PartnerIntegration.Api
```

API will be available at `https://localhost:7xxx` / `http://localhost:5xxx` (see console output).

---

## Running Tests

```bash
# Run all tests
dotnet test

# With detailed output
dotnet test --logger "console;verbosity=normal"

# With code coverage report
dotnet test --collect:"XPlat Code Coverage"
```

**Test suite: 32 tests — 100% passing**

| Category | Tests | Description |
|---|---|---|
| Validator | 18 | All 5 fields, boundary cases, Theory + InlineData |
| Service | 3 | Happy path, invalid partner, timeout → 503 |
| Retry Resilience | 4 | Real Polly pipeline with `CountingHttpMessageHandler` |
| Integration (E2E) | 7 | `WebApplicationFactory`: 202, 400, 401, 422, 503 |

---

## API Reference

### POST `/api/v1/partner/transactions`

Accepts a partner transaction, verifies the partner, and enqueues for legacy system processing.

**Headers:**

| Header | Required | Description |
|---|---|---|
| `X-Api-Key` | ✅ | API key from `Security:ApiKeys` config |
| `Content-Type` | ✅ | `application/json` |

**Request Body:**

```json
{
  "partnerId": "PARTNER_001",
  "transactionReference": "TX_20260910_001",
  "amount": 1500000,
  "currency": "VND",
  "timestamp": "2026-09-10T06:30:00Z"
}
```

**Responses:**

| Status | Condition |
|---|---|
| `202 Accepted` | Transaction queued successfully |
| `400 Bad Request` | Validation failed (missing fields, invalid amount/currency/timestamp) |
| `401 Unauthorized` | Missing or invalid `X-Api-Key` |
| `422 Unprocessable Entity` | Partner ID not found or inactive |
| `429 Too Many Requests` | Rate limit exceeded |
| `503 Service Unavailable` | Partner verification service unreachable after retries |

**Example (curl):**

```bash
curl -X POST http://localhost:8080/api/v1/partner/transactions \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: docker-local-api-key" \
  -d '{
    "partnerId": "PARTNER_001",
    "transactionReference": "TX_20260910_001",
    "amount": 1500000,
    "currency": "VND",
    "timestamp": "2026-09-10T06:30:00Z"
  }'
```

**Example (PowerShell):**

```powershell
Invoke-RestMethod -Method POST -Uri "http://localhost:8080/api/v1/partner/transactions" `
  -Headers @{ "X-Api-Key" = "docker-local-api-key"; "Content-Type" = "application/json" } `
  -Body '{"partnerId":"PARTNER_001","transactionReference":"TX_001","amount":100,"currency":"USD","timestamp":"2026-09-10T06:30:00Z"}'
```

---

## Security

### API Key

Pass the key in every request header:
```
X-Api-Key: <your-key>
```

To add multiple keys (e.g., for rotation), separate with commas in config:
```json
"Security": {
  "ApiKeys": "key-one,key-two,key-old"
}
```

Or via environment variable:
```bash
Security__ApiKeys=key-one,key-two
```

### Rate Limiting

| Scope | Limit |
|---|---|
| All endpoints (per IP) | 100 requests / minute |
| `/api/v1/partner/transactions` | 20 requests / minute |

Exceeded requests receive `429 Too Many Requests`.

### Production Checklist

- [ ] Replace `Security:ApiKeys` with a secrets manager (Azure Key Vault, AWS Secrets Manager)
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production` (disables Swagger, mock endpoint should be removed)
- [ ] Use HTTPS with a valid certificate
- [ ] Rotate RabbitMQ credentials from default `guest/guest`
- [ ] Consider upgrading to JWT for partner-scoped authorization
