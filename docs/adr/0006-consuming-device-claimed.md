# 6. Consuming device.claimed.v1

Status: accepted

## Context
Orderflow publishes order events (ADR 5). The shop should also show that goods it sold are in use, which only another system knows: a device is claimed when its owner links it. The event is `device.claimed.v1` from the shared AsyncAPI contract, and a device built at home has no order.

## Decision
- A hosted listener consumes `device.claimed.v1` from the topic exchange `integration.events` with the raw RabbitMQ client, like the publisher writes. MassTransit is not used because these messages are plain JSON from outside, not MassTransit envelopes.
- The queue is durable. A message is acknowledged after its effect is saved. A message that fails is retried once and then dead-lettered (`orders.device-claimed.dead`), so one bad message never blocks the rest.
- Handling is idempotent: the message id and an inbox row are saved in the same transaction as the change, so a redelivery is skipped. The business rule is idempotent too: only a **shipped** order becomes activated, and only once. A second device of the same order, a claim for an order that is not shipped, an unknown order, or a message without an order (a device not sold here) changes nothing and is acknowledged.
- Orderflow reads the order id, the device id and the time. It does not read the owner or the pot, and the order status stays `Shipped`: activation is a fact beside the status, `ActivatedAt`, not a new state in the saga.
- The vendored copy of the contract is refreshed, since `orderId` became optional in it.

## Consequences
- The shop shows "In use" without knowing anything about devices. Any system that publishes the event for an order has the same effect.
- Restocking returned goods and consuming other events would use the same listener pattern.