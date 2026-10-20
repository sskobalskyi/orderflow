# 1. Transactional outbox from the first service

Status: accepted

## Context
Orders must save an order and announce `OrderSubmitted` to RabbitMQ. Doing both without a shared transaction can lose the event (crash after commit) or announce an order that was never saved.

## Decision
Use the MassTransit EF Core bus outbox on top of PostgreSQL. `IPublishEndpoint.Publish` inside the request writes the message to an outbox table in the same `SaveChanges` transaction as the order. A background delivery service relays it to RabbitMQ with at-least-once semantics. Consumers will use the inbox for idempotency.

MassTransit is pinned to v8 (Apache-2.0); v9 is commercial.

## Consequences
- No dual-write problem; delivery survives broker outages.
- Duplicates are possible, so every consumer must be idempotent.
- Each service owns a schema (`orders`, later `payments`, `inventory`) in one PostgreSQL instance to keep local setup light.
