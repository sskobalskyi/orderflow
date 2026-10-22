using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;
using Orderflow.Contracts.Integration;
using Orderflow.Observability;
using Orderflow.Orders.Domain;
using Orderflow.Orders.Infrastructure;
using StackExchange.Redis;
using Order = Orderflow.Orders.Domain.Order;

namespace Orderflow.Orders.Api;

public record OrderLineRequest(string Sku, int Quantity);

public record SubmitOrderRequest(List<OrderLineRequest> Lines);

public record RefundRequest(decimal? Amount, string? Reason);

public static class OrderEndpoints
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders");

        // An optional Idempotency-Key header makes client retries safe: the same key returns the same order.
        group.MapPost("/", async (SubmitOrderRequest req, HttpRequest http, OrdersDbContext db, IPublishEndpoint bus,
            CatalogClient catalog, IConnectionMultiplexer redis, TimeProvider clock, CancellationToken ct) =>
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
                var (lines, currency) = await PriceLines(req.Lines, catalog, ct);
                var order = Order.Submit(customerId, lines, currency, clock);
                db.Orders.Add(order);
                OrderflowMetrics.OrdersSubmitted.Add(1);
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

        // The customer sends a shipped order back. The integration event tells other systems, for example
        // a billing service that grants benefits for purchases.
        group.MapPost("/{id:guid}/return", async (Guid id, HttpRequest http, OrdersDbContext db,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (CustomerId(http) is not { } customerId)
                return Results.Problem("Missing customer identity.", statusCode: 401);
            if (await db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId, ct) is not { } order)
                return Results.NotFound();

            order.RegisterReturn(clock);
            AddTimeline(db, order.Id, "Returned", null, clock);
            db.AddIntegrationEvent(new OrderReturnedV1(order.Id, order.CustomerId.ToString(), order.ReturnedAt!.Value,
                ToIntegrationLines(order)), clock);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(order));
        });

        // Everything below is reachable only through the gateway's admin route.
        var admin = app.MapGroup("/admin/orders");

        admin.MapGet("/", async (OrdersDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(ct)).Select(ToDto)));

        admin.MapPost("/{id:guid}/ship", async (Guid id, OrdersDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct) is not { } order)
                return Results.NotFound();

            order.Ship(clock);
            AddTimeline(db, order.Id, "Shipped", null, clock);
            db.AddIntegrationEvent(new OrderShippedV1(order.Id, order.CustomerId.ToString(), order.ShippedAt!.Value), clock);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(order));
        });

        // Asks Payments to refund. The order becomes Refunded, and order.refunded.v1 goes out, once Payments confirms.
        admin.MapPost("/{id:guid}/refund", async (Guid id, RefundRequest? req, OrdersDbContext db, IPublishEndpoint bus, CancellationToken ct) =>
        {
            if (await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct) is not { } order)
                return Results.NotFound();

            var amount = order.EnsureRefundable(req?.Amount);
            await bus.Publish(new RefundPayment(order.Id, amount, req?.Reason ?? "refund"), ct);
            await db.SaveChangesAsync(ct);
            return Results.Accepted($"/orders/{order.Id}", new { order.Id, Status = "RefundRequested", Amount = amount });
        });
    }

    /// <summary>Resolves every line against the catalog. Unknown or inactive products and mixed currencies are rejected.</summary>
    private static async Task<(List<OrderLine> Lines, string Currency)> PriceLines(
        List<OrderLineRequest>? requested, CatalogClient catalog, CancellationToken ct)
    {
        if (requested is not { Count: > 0 })
            throw new DomainException("order_empty", "An order needs at least one line.");

        var lines = new List<OrderLine>();
        string? currency = null;
        foreach (var line in requested)
        {
            if (string.IsNullOrWhiteSpace(line.Sku) || line.Quantity <= 0)
                throw new DomainException("order_invalid_line", "Every line needs a SKU and a positive quantity.");

            var product = await catalog.GetAsync(line.Sku, ct);
            if (product is not { Active: true })
                throw new DomainException("order_unknown_product", $"Product '{line.Sku}' is not available.");

            currency ??= product.Currency;
            if (product.Currency != currency)
                throw new DomainException("order_mixed_currency", "All lines of an order must use the same currency.");

            lines.Add(new OrderLine(product.Sku, line.Quantity, product.Price));
        }

        return (lines, currency!);
    }

    private static void AddTimeline(OrdersDbContext db, Guid orderId, string step, string? detail, TimeProvider clock) =>
        db.Timeline.Add(new TimelineEntry { OrderId = orderId, Step = step, Detail = detail, At = clock.GetUtcNow() });

    public static List<IntegrationOrderLine> ToIntegrationLines(Order o) =>
        o.Lines.Select(l => new IntegrationOrderLine(l.Sku, l.Quantity, l.UnitPrice)).ToList();

    /// <summary>Identity set by the gateway from the validated JWT. The Orders port is not published outside the compose network.</summary>
    private static Guid? CustomerId(HttpRequest http) =>
        Guid.TryParse(http.Headers["X-Customer-Id"].FirstOrDefault(), out var id) ? id : null;

    private static object ToDto(Order o) => new
    {
        o.Id, o.CustomerId, Status = o.Status.ToString(), o.RejectionReason, o.Currency, o.CreatedAt,
        o.ConfirmedAt, o.ShippedAt, o.ActivatedAt, o.ReturnedAt, o.RefundedAt, o.RefundAmount, o.Lines, o.Total,
    };
}
