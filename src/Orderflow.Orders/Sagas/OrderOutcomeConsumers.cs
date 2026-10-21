using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;
using Orderflow.Observability;
using Orderflow.Orders.Domain;
using Orderflow.Orders.Infrastructure;

namespace Orderflow.Orders.Sagas;

/// <summary>Applies the saga outcome to the order aggregate. Idempotent: the order only moves out of Submitted once.</summary>
public class OrderConfirmedConsumer(OrdersDbContext db, TimeProvider clock) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var order = await db.Orders.FirstAsync(o => o.Id == context.Message.OrderId, context.CancellationToken);
        if (order.Status != OrderStatus.Submitted) return;

        order.Confirm();
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

internal static class OrderOutcomeMetrics
{
    public static void Record(Order order, string outcome, TimeProvider clock)
    {
        OrderflowMetrics.OrdersCompleted.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        OrderflowMetrics.OrderDuration.Record((clock.GetUtcNow() - order.CreatedAt).TotalSeconds,
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}
