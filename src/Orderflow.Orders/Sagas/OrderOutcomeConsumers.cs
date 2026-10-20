using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;
using Orderflow.Orders.Infrastructure;

namespace Orderflow.Orders.Sagas;

/// <summary>Applies the saga outcome to the order aggregate. Idempotent: the order only moves out of Submitted once.</summary>
public class OrderConfirmedConsumer(OrdersDbContext db) : IConsumer<OrderConfirmed>
{
    public async Task Consume(ConsumeContext<OrderConfirmed> context)
    {
        var order = await db.Orders.FirstAsync(o => o.Id == context.Message.OrderId, context.CancellationToken);
        order.Confirm();
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

public class OrderRejectedConsumer(OrdersDbContext db) : IConsumer<OrderRejected>
{
    public async Task Consume(ConsumeContext<OrderRejected> context)
    {
        var order = await db.Orders.FirstAsync(o => o.Id == context.Message.OrderId, context.CancellationToken);
        order.Reject(context.Message.Reason);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
