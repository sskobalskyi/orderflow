# 2. Orchestrated saga inside the Orders service

Status: accepted

## Context
Confirming an order spans Inventory and Payments, each with its own database. A distributed transaction is not an option.

## Decision
A MassTransit state machine in Orders orchestrates the flow: `OrderSubmitted` -> reserve stock -> take payment -> `OrderConfirmed`. If payment fails, the saga publishes `ReleaseStock` (compensation) and `OrderRejected`. Saga state lives in `orders.order_saga` with an optimistic `xmin` concurrency token. Finished instances are kept so the UI can render a timeline.

Orchestration was chosen over choreography because the flow has one owner and an explicit compensation step that is easy to test in isolation (see `OrderStateMachineTests`).

## Consequences
- Orders knows the process; Inventory and Payments only react to commands and reply.
- Payments declines amounts above `Payments:Limit` (default 1000) so the compensation path is demoable.
- Messages are published (not sent to fixed queues), keeping services decoupled at the cost of looser routing visibility.
