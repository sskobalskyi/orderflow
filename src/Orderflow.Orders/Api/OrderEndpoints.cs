using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;
using Orderflow.Orders.Domain;
using Orderflow.Orders.Infrastructure;
using StackExchange.Redis;
using Order = Orderflow.Orders.Domain.Order;

namespace Orderflow.Orders.Api;

public record SubmitOrderRequest(List<OrderLine> Lines);

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
            if (CustomerId(http) is not { } customerId)
                return Results.Problem("Missing customer identity.", statusCode: 401);

            var cache = redis.GetDatabase();
            string? key = null;
            if (http.Headers["Idempotency-Key"].FirstOrDefault() is { Length: > 0 } header)
            {
                key = $"idempotency:orders:{customerId}:{header}";
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
                var order = Order.Submit(customerId, req.Lines, clock);
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

        group.MapGet("/", async (HttpRequest http, OrdersDbContext db, CancellationToken ct) =>
            CustomerId(http) is not { } customerId
                ? Results.Problem("Missing customer identity.", statusCode: 401)
                : Results.Ok((await db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId)
                    .OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync(ct)).Select(ToDto)));

        group.MapGet("/{id:guid}", async (Guid id, HttpRequest http, OrdersDbContext db, CancellationToken ct) =>
            CustomerId(http) is not { } customerId
                ? Results.Problem("Missing customer identity.", statusCode: 401)
                : await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId, ct) is { } order
                    ? Results.Ok(ToDto(order))
                    : Results.NotFound());

        group.MapGet("/{id:guid}/timeline", async (Guid id, HttpRequest http, OrdersDbContext db, CancellationToken ct) =>
            CustomerId(http) is not { } customerId
                ? Results.Problem("Missing customer identity.", statusCode: 401)
                : !await db.Orders.AnyAsync(o => o.Id == id && o.CustomerId == customerId, ct)
                    ? Results.NotFound()
                    : Results.Ok(await db.Timeline.AsNoTracking().Where(t => t.OrderId == id)
                        .OrderBy(t => t.At).ThenBy(t => t.Id)
                        .Select(t => new { t.Step, t.Detail, t.At }).ToListAsync(ct)));
    }

    /// <summary>Identity set by the gateway from the validated JWT. The Orders port is not published outside the compose network.</summary>
    private static Guid? CustomerId(HttpRequest http) =>
        Guid.TryParse(http.Headers["X-Customer-Id"].FirstOrDefault(), out var id) ? id : null;

    private static object ToDto(Order o) => new
    {
        o.Id, o.CustomerId, Status = o.Status.ToString(), o.RejectionReason, o.CreatedAt, o.Lines, o.Total,
    };
}
