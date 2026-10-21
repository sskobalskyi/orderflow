using MassTransit;
using Orderflow.Contracts;

namespace Orderflow.Orders.Sagas;

public class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public uint RowVersion { get; set; }
}

/// <summary>
/// Orchestrates an order: reserve stock, take payment, and compensate (release stock) if payment fails.
/// Finished instances are kept so the UI can show the saga timeline.
/// </summary>
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State AwaitingStock { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public State Confirmed { get; private set; } = null!;
    public State Rejected { get; private set; } = null!;

    public Event<OrderSubmitted> Submitted { get; private set; } = null!;
    public Event<StockReserved> StockReservedEvent { get; private set; } = null!;
    public Event<StockRejected> StockRejectedEvent { get; private set; } = null!;
    public Event<PaymentCompleted> PaymentCompletedEvent { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailedEvent { get; private set; } = null!;

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => Submitted, e => e.CorrelateById(c => c.Message.OrderId));
        Event(() => StockReservedEvent, e => e.CorrelateById(c => c.Message.OrderId));
        Event(() => StockRejectedEvent, e => e.CorrelateById(c => c.Message.OrderId));
        Event(() => PaymentCompletedEvent, e => e.CorrelateById(c => c.Message.OrderId));
        Event(() => PaymentFailedEvent, e => e.CorrelateById(c => c.Message.OrderId));

        Initially(
            When(Submitted)
                .Then(c =>
                {
                    c.Saga.CustomerId = c.Message.CustomerId;
                    c.Saga.Total = c.Message.Total;
                    c.Saga.CreatedAt = DateTimeOffset.UtcNow;
                })
                .Publish(c => new ReserveStock(c.Message.OrderId, c.Message.Lines))
                .TransitionTo(AwaitingStock));

        During(AwaitingStock,
            When(StockReservedEvent)
                .Publish(c => new ProcessPayment(c.Saga.CorrelationId, c.Saga.CustomerId, c.Saga.Total))
                .TransitionTo(AwaitingPayment),
            When(StockRejectedEvent)
                .Then(c => c.Saga.Reason = c.Message.Reason)
                .Publish(c => new OrderRejected(c.Saga.CorrelationId, c.Message.Reason))
                .TransitionTo(Rejected));

        During(AwaitingPayment,
            When(PaymentCompletedEvent)
                .Publish(c => new OrderConfirmed(c.Saga.CorrelationId))
                .TransitionTo(Confirmed),
            When(PaymentFailedEvent)
                .Then(c => c.Saga.Reason = c.Message.Reason)
                .Publish(c => new ReleaseStock(c.Saga.CorrelationId))
                .Publish(c => new OrderRejected(c.Saga.CorrelationId, c.Message.Reason))
                .TransitionTo(Rejected));

        // Delivery is at-least-once, so a reply can arrive twice or after the saga has moved on.
        // Ignoring it is the correct idempotent behaviour; failing would only park it in the error queue.
        During(AwaitingPayment, Ignore(StockReservedEvent), Ignore(StockRejectedEvent));
        During(Confirmed, Rejected,
            Ignore(Submitted), Ignore(StockReservedEvent), Ignore(StockRejectedEvent),
            Ignore(PaymentCompletedEvent), Ignore(PaymentFailedEvent));
    }
}
