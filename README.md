# dotnet-order-api

[![CI](https://github.com/ronybrand/dotnet-order-api/actions/workflows/ci.yml/badge.svg)](https://github.com/ronybrand/dotnet-order-api/actions/workflows/ci.yml)
[![CodeQL](https://github.com/ronybrand/dotnet-order-api/actions/workflows/codeql.yml/badge.svg)](https://github.com/ronybrand/dotnet-order-api/actions/workflows/codeql.yml)

[API docs](https://ronybrand.github.io/dotnet-order-api)

Order Management API in ASP.NET Core (.NET 10) with EF Core and PostgreSQL. It implements the same domain
as `fastapi-order-api` (the specification source: customers, orders, items, status transitions and the
notification flow; see [`DOMAIN.md`](./DOMAIN.md)), following the Clean Architecture conventions of the `dotnet-feature` guidelines
summarized in [`AGENTS.md`](./AGENTS.md).

## Architecture

```
src/
  OrderApi.Domain          entities, domain exceptions, error codes, repository interfaces, search primitives
  OrderApi.Application     services (business rules), DTOs, FluentValidation validators, event contract
  OrderApi.Infrastructure  EF Core (DbContext, migrations, repositories), search engine, RabbitMQ publisher
  OrderApi.Api             controllers, middleware, JWT auth, rate limiting, Program.cs
  OrderApi.Worker          RabbitMQ consumer that sends the status e-mails (SMTP)
  Test/OrderApi.Test             unit tests (no Docker)
  Test/OrderApi.IntegrationTest  HTTP tests against real Postgres (Testcontainers, needs Docker)
```

**Request flow (synchronous):** `CorrelationId` -> `SecurityHeaders` -> `GlobalExceptionHandler` -> CORS ->
authentication (JWT RS256) -> rate limiter -> authorization -> `ValidationFilter` -> controller -> service ->
repository -> PostgreSQL.

**Notification flow (asynchronous):** `confirm`/`cancel` commit the status change, then publish an
`OrderStatusChangedEvent` to the `order.status.changed` RabbitMQ queue. `OrderApi.Worker` consumes it and
sends an HTML e-mail via SMTP (Mailpit in dev), retrying 3 times with a 2s backoff before routing to
`order.status.changed.dlq`.

## Domain

```
Customer (1) ──< Order (1) ──< Item
```

Out of scope: payment, inventory, product catalog, shipping.

| Entity | Rules |
|---|---|
| Customer | `name` required; `tax_id` required, unique, `^[A-Za-z0-9./-]{5,20}$`; `passport_number` optional, unique when present, `^[A-Z0-9]{6,9}$`; `email` valid. Soft delete. `tax_id`, `passport_number` and `email` are PII (`[Sensitive]`, never in responses or logs). |
| Order | `customer_id` must exist; status `OPEN -> CONFIRMED -> CANCELED` (or `OPEN -> CANCELED`); `total` derived; max 200 items; optimistic concurrency (Postgres `xmin`); soft delete. |
| Item | `description` non blank, max 255; `unit_price > 0` with at most 2 decimals; `quantity > 0`; subtotal = price x quantity. |

Business rules: items are editable only while `OPEN`; confirm needs `OPEN` and at least one item; cancel is
allowed from `OPEN` or `CONFIRMED`; a customer with non-deleted orders cannot be deleted; unique values of a
soft-deleted customer are released for reuse (filtered unique indexes).

## Endpoints

JSON uses `snake_case`; unknown request fields are rejected with `400`.

### `/orders` (authenticated user)

| Method | Path | Description |
|---|---|---|
| POST | `/orders` | Create order (201) |
| GET / POST | `/orders/search` | Search (query params / body with filters) |
| GET | `/orders/{id}` | Get order |
| DELETE | `/orders/{id}` | Soft delete (204) |
| POST | `/orders/{id}/items` | Add item |
| PATCH | `/orders/{orderId}/items/{itemId}` | Update item quantity |
| DELETE | `/orders/{orderId}/items/{itemId}` | Remove item |
| POST | `/orders/{id}/confirm` | Confirm |
| POST | `/orders/{id}/cancel` | Cancel |

### `/customers` (mutations need role `admin`, reads need an authenticated user)

| Method | Path | Description |
|---|---|---|
| POST | `/customers` | Create (admin, 201) |
| GET / POST | `/customers/search` | Search |
| GET | `/customers/{id}` | Get |
| PUT | `/customers/{id}` | Update (admin) |
| DELETE | `/customers/{id}` | Soft delete (admin, 204) |

Search body: `{ "page": 0, "size": 20, "sort": "-name", "filters": { "name": [{ "op": "lk", "value": "ana" }] } }`.
Operators: `eq neq gt gte lt lte in between lk`. `page` is 0-based, `size` is at most 100. Fields are resolved from
the EF Core model (snake_case or CLR name); an unknown filter field is ignored, an unknown sort field is `400`.
`GET /health` is anonymous. Swagger (`/swagger`) exists only in the Development environment.

## Error catalog

Every error has the shape `{ "message", "code", "params", "request_id" }`.

| Code | HTTP | Meaning |
|---|---|---|
| `VALIDATION-01` | 400 | invalid request body |
| `VALIDATION-02` | 400 | order not editable in its status |
| `VALIDATION-03` | 400 | confirming an order with no items |
| `VALIDATION-04` | 400 | invalid status transition when confirming |
| `VALIDATION-05` | 400 | invalid filter value |
| `VALIDATION-06` | 400 | unknown sort field |
| `VALIDATION-07` | 400 | nonexistent customer when creating an order |
| `VALIDATION-08` | 400 | canceling an already canceled order |
| `RESOURCE-NOT-FOUND-01/02/03` | 404 | customer / order / item |
| `CONFLICT-00` | 409 | concurrent modification (optimistic lock) |
| `CONFLICT-01/02` | 409 | duplicate `tax_id` / `passport_number` |
| `CONFLICT-03` | 409 | deleting a customer with orders |
| `RATE-00` | 429 | too many requests (100/min global, 10/min on customer writes) |
| `INTERNAL-00` | 500 | unexpected error (generic message, detail only in the log) |

401 is returned for a missing/invalid token and 403 for a valid token without the required role.

## Running locally

Requirements: .NET 10 SDK and Docker (Postgres, RabbitMQ, Mailpit).

```bash
docker compose up -d

# RS256 key pair: the API only verifies tokens, so it only needs the public key
mkdir keys && openssl genrsa -out keys/jwt-private.pem 2048
openssl rsa -in keys/jwt-private.pem -pubout -out keys/jwt-public.pem

export ConnectionStrings__DefaultConnection="Host=localhost;Database=orders;Username=postgres;Password=postgres"
export Jwt__PublicKeyPath=keys/jwt-public.pem
dotnet run --project src/OrderApi.Api          # applies migrations in Development
dotnet run --project src/OrderApi.Worker       # separate process
```

Configuration (environment variables use `__` as separator): `ConnectionStrings:DefaultConnection`,
`Jwt:PublicKeyPath` (required), `Jwt:Audience`, `Jwt:Issuer`, `Cors:AllowedOrigins` (comma separated; **required
when `ASPNETCORE_ENVIRONMENT=Production`**, startup fails otherwise), `RabbitMq:*`, `Smtp:Host/Port/From`,
`Database:MigrateOnStartup`. No secret is committed; `keys/` is git-ignored.

New migration: `dotnet ef migrations add <Name> --project src/OrderApi.Infrastructure --output-dir Data/Migrations`,
then review it by hand. Warning: for the `xmin` concurrency token EF generates a real `xmin` column in
`CreateTable`; it must be removed from the migration because it is a Postgres system column.

## Architecture decisions

Non-obvious technical decisions (and the alternatives considered) are logged in [`docs/adr/`](./docs/adr/README.md).

## Tests and quality

```bash
dotnet test src/Test/OrderApi.Test                  # unit, no Docker
dotnet test src/Test/OrderApi.IntegrationTest       # Testcontainers, needs Docker
dotnet format --verify-no-changes
dotnet build -warnaserror                           # analyzers are warnings-as-errors (Directory.Build.props)
make verify                                         # single local gate: format, build, unit + integration, coverage >= 80%
```

`make verify` reproduces the CI quality gates in one command. Coverage is measured over the unit and integration
suites merged (controllers, middleware and repositories are only exercised by the integration tests); the worker's
broker and SMTP glue needs real infrastructure and is excluded. CI also checks migration drift
(`dotnet ef migrations has-pending-model-changes`), builds both Docker images (pushed to GHCR from `main`) and
builds an OpenAPI snapshot (`scripts/export-openapi.sh`) and publishes it as a Redoc page on GitHub Pages
(guarded by the repository variable `ENABLE_PAGES=true`, so a fork without Pages enabled does not fail).

Integration tests use real RS256 tokens signed with a throwaway key. The only host difference from production
is an in-memory `IOrderEventPublisher` (no broker); rate limiting, CORS, security headers and body limits stay
active.

## Design decisions

- **Binary RBAC** (`user` reads, `admin` mutates customers): the domain has no ownership or tenancy concept.
- **Optimistic concurrency with `xmin`** on `Order`; item mutations also touch the order's audit columns so the
  token covers children. A stale write becomes `409 CONFLICT-00`.
- **Soft delete with a global EF query filter** applied by reflection to every `ISoftDeletable`, and unique
  indexes filtered by `deleted_at IS NULL`. Uniqueness is checked in the service for a readable `409` and
  enforced by the indexes under concurrency (violation `23505` is translated to `ConflictDomainException`).
- **Asynchronous notification (RabbitMQ + worker) instead of a synchronous SMTP call:** sending e-mail is slow
  and unreliable, must not delay or fail the HTTP response, and needs independent retry and a dead-letter queue.
  The publish is best-effort after the commit (no transactional outbox): a broker outage only logs an error and a
  notification can be lost. This trade-off is intentional and mirrors the reference implementations.
- **Rate limiter after authentication** so authenticated callers are partitioned by subject (anonymous callers by
  IP); the order differs slightly from "limiter before auth" but keeps per-user fairness.
- **POST/PUT return the resource** (kept from the Python contract) instead of an empty `201` with only a location.
