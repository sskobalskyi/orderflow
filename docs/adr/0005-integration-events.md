# 5. Versioned integration events, separate from saga messages

Status: accepted

## Context
Other projects (billing, fulfilment, analytics) need to react to what happens to an order: confirmed, shipped, returned, refunded. Orderflow's internal saga messages are the wrong thing to expose: `OrderConfirmed` carries only an order id, it is a MassTransit type that changes with the saga, and consumers would have to share Orderflow's assemblies. The contract for the cross-project events lives outside Orderflow (AsyncAPI, owned by the Greenhaus repo), and Orderflow must stay a generic marketplace that knows nothing about its consumers.

## Decision
- **Separate message types.** `order.confirmed.v1`, `order.shipped.v1`, `order.returned.v1` and `order.refunded.v1` are their own records (`Orderflow.Contracts.Integration`), carrying everything a consumer needs (customer, lines, totals, currency). The saga messages stay internal and free to change.
- **A wire contract, not a MassTransit one.** Events are raw JSON on the topic exchange `integration.events`, routing key = event name, with the AMQP headers `messageId`, `occurredAt`, `schemaVersion` and `traceparent` (so one trace spans projects). Consumers need not use MassTransit.
- **Their own transactional outbox.** MassTransit's EF outbox stores and re-reads every message through its own envelope serializer, which rules out raw bodies (tried first: internal messages broke when the raw serializer was registered on the bus, and the outbox could not relay a raw body). A small `integration_outbox` table is written by the same `SaveChanges` as the order change, and a background publisher relays it with publisher confirms and `FOR UPDATE SKIP LOCKED`, so several instances can share it. Delivery is at-least-once; consumers deduplicate on `messageId`.
- **Refunds go through Payments.** The admin endpoint validates and sends `RefundPayment`; the order becomes `Refunded` and `order.refunded.v1` is published only after Payments confirms (`PaymentRefunded`). A refund is idempotent per order.
- **Conformance is tested, not assumed.** A vendored copy of the contract (`contracts/integration-events.yaml`) is the schema source for tests. A plain RabbitMQ listener, standing in for an outside consumer, reads each raw message and validates payload and headers with JSON Schema, and one test proves the validator rejects a broken payload.
- **Prices come from the catalog.** Orders resolves each line against the Catalog service (typed client with the standard resilience handler), so a client can no longer choose its own price, and the totals in events are trustworthy.

## Consequences
- Two outboxes in the Orders database with different jobs: MassTransit's for internal messages, the integration outbox for the public contract. The cost is a little code; the gain is an exact wire format.
- The contract is vendored. When the Greenhaus copy changes, copy it again and the conformance tests show what broke.
- Not done yet: consuming `device.claimed.v1` to show an order as activated, and restocking returned goods.
