using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;
using Orderflow.Orders.Domain;
using Orderflow.Orders.Infrastructure;

namespace Orderflow.Orders.Sagas;

/// <summary>
/// CQRS read side: projects every saga step into a flat timeline the UI can query without touching
/// the saga or the aggregate. Idempotent per (order, step).
/// </summary>
public class OrderTimelineProjector(OrdersDbContext db, TimeProvider clock) :
    IConsumer<OrderSubmitted>, IConsumer<StockReserved>, IConsumer<StockRejected>,
    IConsumer<PaymentCompleted>, IConsumer<PaymentFailed>, IConsumer<OrderConfirmed>, IConsumer<OrderRejected>
{
    public Task Consume(ConsumeContext<OrderSubmitted> c) => Add(c.Message.OrderId, "Submitted", null, c);
    public Task Consume(ConsumeContext<StockReserved> c) => Add(c.Message.OrderId, "StockReserved", null, c);
    public Task Consume(ConsumeContext<StockRejected> c) => Add(c.Message.OrderId, "StockRejected", c.Message.Reason, c);
    public Task Consume(ConsumeContext<PaymentCompleted> c) => Add(c.Message.OrderId, "PaymentCompleted", null, c);
    public Task Consume(ConsumeContext<PaymentFailed> c) => Add(c.Message.OrderId, "PaymentFailed", c.Message.Reason, c);
    public Task Consume(ConsumeContext<OrderConfirmed> c) => Add(c.Message.OrderId, "Confirmed", null, c);
    public Task Consume(ConsumeContext<OrderRejected> c) => Add(c.Message.OrderId, "Rejected", c.Message.Reason, c);

    private async Task Add(Guid orderId, string step, string? detail, ConsumeContext context)
    {
        if (await db.Timeline.AnyAsync(t => t.OrderId == orderId && t.Step == step, context.CancellationToken)) return;

        db.Timeline.Add(new TimelineEntry { OrderId = orderId, Step = step, Detail = detail, At = clock.GetUtcNow() });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
