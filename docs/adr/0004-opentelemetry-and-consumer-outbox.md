# 4. OpenTelemetry everywhere, and the outbox on consumers too

Status: accepted

## Context
One order touches five processes and two brokers' worth of hops. Debugging it needs a single trace, and the first load test through the Observatory stack exposed a race that was invisible without it.

## Decision
- Every service calls `AddOrderflowTelemetry` (`Orderflow.Observability`): ASP.NET Core, HttpClient, Npgsql, runtime and MassTransit traces and metrics, plus logs. The W3C trace context rides in message headers, so one order is one trace from the gateway through RabbitMQ to every service. Export is OTLP and only active when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- Business metrics (`orderflow.orders.*`, `orderflow.payments.processed`, `orderflow.stock.reservations`) feed the "Orderflow saga flow" Grafana dashboard (`docs/grafana/orderflow-saga.json`).
- The EF outbox is applied to every receive endpoint (`AddConfigureEndpointsCallback`), not only to publishes from HTTP handlers. Without it the saga published `ReserveStock` before its own state was committed, Inventory replied fast, and `StockReserved` hit a saga instance that did not exist yet: about 6% of orders stayed in `AwaitingStock` forever, with no error.
- The saga ignores late or duplicate replies (`Ignore`) instead of faulting them into the error queue.

## Consequences
- Latency gains a small outbox polling delay (p50 about 170 ms submit to final outcome locally).
- The inbox gives consumers duplicate detection for free.
- All services share one telemetry setup, so Observatory dashboards work for any of them.
