using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;
using Orderflow.Orders.Domain;
using Orderflow.Orders.Infrastructure;
using StackExchange.Redis;
using Order = Orderflow.Orders.Domain.Order;

namespace Orderflow.Orders.Api;

public record SubmitOrderRequest(Guid CustomerId, List<OrderLine> Lines);

public static class OrderEndpoints
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders");

        // An optional Idempotency-Key header makes client retries safe: the same key returns the same order.
        group.MapPost("/", async (SubmitOrderRequest req, HttpRequest http, OrdersDbContext db, IPublishEndpoint bus,
            IConnectionMultiplexer redis, TimeProvider clock, CancellationToken ct) =>
        {
            var cache = redis.GetDatabase();
            string? key = null;
            if (http.Headers["Idempotency-Key"].FirstOrDefault() is { Length: > 0 } header)
            {
                key = $"idempotency:orders:{header}";
                if (!await cache.StringSetAsync(key, "pending", IdempotencyTtl, When.NotExists))
                {
                    var existing = (string?)await cache.StringGetAsync(key);
                    if (Guid.TryParse(existing, out var existingId)
                        && await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == existingId, ct) is { } found)
                        return Results.Ok(ToDto(found));
                    return Results.Problem("A request with this Idempotency-Key is still in progress.", statusCode: 409);
                }
            }

            try
            {
                var order = Order.Submit(req.CustomerId, req.Lines, clock);
                db.Orders.Add(order);
                // With the EF bus outbox this publish is persisted in the same SaveChanges transaction.
                await bus.Publish(order.ToSubmittedEvent(), ct);
                await db.SaveChangesAsync(ct);
                if (key is not null) await cache.StringSetAsync(key, order.Id.ToString(), IdempotencyTtl);
                return Results.Created($"/orders/{order.Id}", ToDto(order));
            }
            catch
            {
                if (key is not null) await cache.KeyDeleteAsync(key);
                throw;
            }
        });

        group.MapGet("/", async (OrdersDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync(ct)).Select(ToDto)));

        group.MapGet("/{id:guid}", async (Guid id, OrdersDbContext db, CancellationToken ct) =>
            await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct) is { } order
                ? Results.Ok(ToDto(order))
                : Results.NotFound());

        group.MapGet("/{id:guid}/timeline", async (Guid id, OrdersDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Timeline.AsNoTracking().Where(t => t.OrderId == id)
                .OrderBy(t => t.At).ThenBy(t => t.Id)
                .Select(t => new { t.Step, t.Detail, t.At }).ToListAsync(ct)));
    }

    private static object ToDto(Order o) => new
    {
        o.Id, o.CustomerId, Status = o.Status.ToString(), o.RejectionReason, o.CreatedAt, o.Lines, o.Total,
    };
}
