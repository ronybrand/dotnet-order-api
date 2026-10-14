# ADR 0002: Best-effort publish after commit, not a transactional outbox

## Status
Accepted

## Context
`OrderService.ConfirmAsync` and `CancelAsync` change the order status and then notify the customer. Sending
an e-mail is slow and unreliable, so it must not run inside the HTTP request. The same domain is
implemented in `fastapi-order-api`, `spring-order-api` and `nest-order-api`; the Spring version uses a
transactional outbox so an event is never lost when the broker is down at commit time.

## Decision
The service commits the status change, then awaits `IOrderEventPublisher.PublishStatusChangedAsync`.
`RabbitMqOrderEventPublisher` publishes to `order.status.changed` and catches every failure, only logging
the order id (never the e-mail). The separate `OrderApi.Worker` process consumes the queue, renders the
HTML and sends it through SMTP, retrying three times with a 2 second backoff before nacking to
`order.status.changed.dlq`.

Messaging was chosen over a direct SMTP call because the effect is slow and external, needs independent
retry and a dead-letter queue, and must never delay or roll back the main response.

## Alternatives considered
- **Transactional outbox (table plus poller):** the correct choice for a system that cannot lose
  notifications, but it would repeat the lesson already shown by the Spring implementation without adding
  signal about the .NET stack.
- **Synchronous SMTP in the request:** simplest, but couples request latency and success to an external
  server and has no retry or dead-letter path.
- **Publish from a background task inside the API:** removes the latency, but a process restart loses the
  work and the retry story is the same as publishing directly.

## Consequences
- Positive: no extra table or poller; the whole event flow is one traceable call.
- Negative accepted: if RabbitMQ is down exactly when a status changes, that notification is lost. The
  order change itself is never affected.
- The publisher opens a connection per message. That is fine at this volume; a long-lived connection
  would be the first optimization.
