using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;
using Orderflow.Orders.Domain;
using Orderflow.Orders.Infrastructure;

namespace Orderflow.Orders.Api;

public record SubmitOrderRequest(Guid CustomerId, List<OrderLine> Lines);

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders");

        group.MapPost("/", async (SubmitOrderRequest req, OrdersDbContext db, IPublishEndpoint bus, TimeProvider clock, CancellationToken ct) =>
        {
            var order = Order.Submit(req.CustomerId, req.Lines, clock);
            db.Orders.Add(order);
            // With the EF bus outbox this publish is persisted in the same SaveChanges transaction.
            await bus.Publish(order.ToSubmittedEvent(), ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/orders/{order.Id}", ToDto(order));
        });

        group.MapGet("/{id:guid}", async (Guid id, OrdersDbContext db, CancellationToken ct) =>
            await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct) is { } order
                ? Results.Ok(ToDto(order))
                : Results.NotFound());
    }

    private static object ToDto(Order o) => new
    {
        o.Id, o.CustomerId, Status = o.Status.ToString(), o.CreatedAt, o.Lines, o.Total,
    };
}
