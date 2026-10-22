using MassTransit;
using Microsoft.EntityFrameworkCore;
using Api = Orderflow.Orders.Api;
using Orderflow.Contracts;
using Orderflow.Contracts.Integration;
using Orderflow.Observability;
using Orderflow.Orders.Domain;
using Orderflow.Orders.Infrastructure;

namespace Orderflow.Orders.Sagas;

/// <summary>
/// Applies the saga outcome to the order aggregate and tells the outside world with a versioned
/// integration event. Idempotent: the order only moves out of Submitted once.
/// </summary>
public class OrderConfirmedConsumer(OrdersDbContext db, TimeProvider clock) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var order = await db.Orders.FirstAsync(o => o.Id == context.Message.OrderId, context.CancellationToken);
        if (order.Status != OrderStatus.Submitted) return;

        order.Confirm(clock);
        db.AddIntegrationEvent(new OrderConfirmedV1(order.Id, order.CustomerId.ToString(),
            Api.OrderEndpoints.ToIntegrationLines(order), order.Total, order.Currency, order.ConfirmedAt!.Value), clock);
        await db.SaveChangesAsync(context.CancellationToken);
        OrderOutcomeMetrics.Record(order, "confirmed", clock);
    }
}

public class OrderRejectedConsumer(OrdersDbContext db, TimeProvider clock) : IConsumer<OrderRejected>
{
    public async Task Consume(ConsumeContext<OrderRejected> context)
    {
        var order = await db.Orders.FirstAsync(o => o.Id == context.Message.OrderId, context.CancellationToken);
        if (order.Status != OrderStatus.Submitted) return;

        order.Reject(context.Message.Reason);
        await db.SaveChangesAsync(context.CancellationToken);
        OrderOutcomeMetrics.Record(order, "rejected", clock);
    }
}

/// <summary>Payments confirmed the money went back: finish the refund, show it in the timeline and announce it.</summary>
public class PaymentRefundedConsumer(OrdersDbContext db, TimeProvider clock) : IConsumer<PaymentRefunded>
{
    public async Task Consume(ConsumeContext<PaymentRefunded> context)
    {
        var msg = context.Message;
        var order = await db.Orders.FirstAsync(o => o.Id == msg.OrderId, context.CancellationToken);
        if (!order.MarkRefunded(msg.Amount, msg.Reason, clock)) return;

        // A refund before shipping is a cancellation: the goods never left, so the stock is released.
        // After shipping the goods are with the customer; they were restocked when the return was registered.
        if (order.ShippedAt is null) await context.Publish(new ReleaseStock(order.Id), context.CancellationToken);

        db.Timeline.Add(new TimelineEntry { OrderId = order.Id, Step = "Refunded", Detail = $"{msg.Amount:0.00} {order.Currency}", At = clock.GetUtcNow() });
        db.AddIntegrationEvent(new OrderRefundedV1(order.Id, order.CustomerId.ToString(), msg.Amount, order.Currency,
            order.RefundedAt!.Value, msg.Reason), clock);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

internal static class OrderOutcomeMetrics
{
    public static void Record(Order order, string outcome, TimeProvider clock)
    {
        OrderflowMetrics.OrdersCompleted.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        OrderflowMetrics.OrderDuration.Record((clock.GetUtcNow() - order.CreatedAt).TotalSeconds,
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}
