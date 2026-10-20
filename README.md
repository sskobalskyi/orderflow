# Orderflow

Event-driven order processing built from small .NET services coordinated by a saga. A portfolio project showing microservices, the transactional outbox, CQRS, an API gateway and OIDC, all running locally in Docker.

> Status: early. Orders, Payments and Inventory run an orchestrated saga with compensation; Notifications, CQRS read model, gateway, Keycloak and the React UI are next.

## Architecture (target)

```mermaid
flowchart LR
    UI[React UI] --> GW[YARP gateway]
    GW --> Orders
    Orders -- OrderSubmitted --> MQ[(RabbitMQ)]
    MQ --> Saga[Order saga]
    Saga --> Payments
    Saga --> Inventory
    Saga --> Notifications
    Orders & Payments & Inventory --> PG[(PostgreSQL, schema per service)]
    GW -. JWT .- KC[Keycloak]
```

## Run

```powershell
docker compose up --build
```

| Service | URL |
| --- | --- |
| Orders API | http://localhost:8081 |
| RabbitMQ UI | http://localhost:15672 (guest / guest) |

```powershell
curl -X POST http://localhost:8081/orders -H "content-type: application/json" `
  -d '{"customerId":"11111111-1111-1111-1111-111111111111","lines":[{"sku":"A","quantity":2,"unitPrice":10}]}'
```

## Test

```powershell
dotnet test
```

Decisions are recorded in [docs/adr](docs/adr).

## Roadmap
- [x] Orders service with transactional outbox
- [x] Order saga (MassTransit state machine) with stock release on payment failure
- [x] Payments and Inventory with outbox and idempotent consumers
- [ ] Notifications, retry policies tuned and DLQ handling
- [ ] CQRS read model, Redis idempotency keys
- [ ] YARP gateway and Keycloak
- [ ] React UI with saga timeline, `en` / `uk`
- [ ] OpenTelemetry into the shared observability stack

## Try the saga

Seed stock: A=100, B=50, C=5. Payments declines totals above 1000.

| Order | Outcome |
| --- | --- |
| 2 x A at 10 | `Confirmed` |
| 1 x B at 5000 | `Rejected` (card declined), stock for B released |
| 9 x C | `Rejected` (insufficient stock) |

Stock levels: http://localhost:8082/stock
