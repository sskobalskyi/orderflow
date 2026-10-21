using Orderflow.Observability;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrderflowTelemetry("inventory");

builder.Services.AddDbContext<InventoryDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Inventory")));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ReserveStockConsumer>();
    x.AddConsumer<ReleaseStockConsumer>();

    x.AddEntityFrameworkOutbox<InventoryDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    // Consumers and sagas publish through the outbox too: messages leave only after the handler's
    // transaction commits, so a reply can never overtake the state it replies to.
    x.AddConfigureEndpointsCallback((ctx, _, endpoint) => endpoint.UseEntityFrameworkOutbox<InventoryDbContext>(ctx));

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", builder.Configuration.GetValue("RabbitMq:Port", (ushort)5672), "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });
        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200)));
        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddHealthChecks().AddNpgSql(builder.Configuration.GetConnectionString("Inventory")!);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.MigrateAsync();
    if (!await db.Stock.AnyAsync())
    {
        db.Stock.AddRange(new StockItem("A", 100), new StockItem("B", 50), new StockItem("C", 5));
        await db.SaveChangesAsync();
    }
}

app.MapHealthChecks("/health");
app.MapGet("/stock", async (InventoryDbContext db) => await db.Stock.AsNoTracking().OrderBy(s => s.Sku).ToListAsync());
app.Run();

public class StockItem(string sku, int available)
{
    public string Sku { get; set; } = sku;
    public int Available { get; set; } = available;
    public uint RowVersion { get; set; }
}

public class Reservation
{
    public Guid OrderId { get; set; }
    public string Sku { get; set; } = null!;
    public int Quantity { get; set; }
    public bool Released { get; set; }
}

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("app");
        b.Entity<StockItem>(e =>
        {
            e.ToTable("stock");
            e.HasKey(s => s.Sku);
            e.Property(s => s.RowVersion).HasColumnName("xmin").HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        });
        b.Entity<Reservation>(e =>
        {
            e.ToTable("reservations");
            e.HasKey(r => new { r.OrderId, r.Sku });
        });
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}

/// <summary>All-or-nothing reservation. Idempotent: an order that already holds a reservation is acknowledged again.</summary>
public class ReserveStockConsumer(InventoryDbContext db) : IConsumer<ReserveStock>
{
    public async Task Consume(ConsumeContext<ReserveStock> context)
    {
        var msg = context.Message;

        var existing = await db.Reservations.Where(r => r.OrderId == msg.OrderId).ToListAsync(context.CancellationToken);
        if (existing.Count > 0)
        {
            // Redelivery: re-acknowledge a live reservation, but never re-reserve stock that was already released.
            if (existing.All(r => !r.Released))
                await context.Publish(new StockReserved(msg.OrderId));
            return;
        }

        var wanted = msg.Lines.GroupBy(l => l.Sku).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var items = await db.Stock.Where(s => wanted.Keys.Contains(s.Sku)).ToDictionaryAsync(s => s.Sku, context.CancellationToken);

        var missing = wanted.Where(w => !items.TryGetValue(w.Key, out var i) || i.Available < w.Value).Select(w => w.Key).ToList();
        if (missing.Count > 0)
        {
            OrderflowMetrics.StockReservations.Add(1, new KeyValuePair<string, object?>("result", "rejected"));
            await context.Publish(new StockRejected(msg.OrderId, $"Insufficient stock for: {string.Join(", ", missing)}"));
            return;
        }

        foreach (var (sku, qty) in wanted)
        {
            items[sku].Available -= qty;
            db.Reservations.Add(new Reservation { OrderId = msg.OrderId, Sku = sku, Quantity = qty });
        }

        OrderflowMetrics.StockReservations.Add(1, new KeyValuePair<string, object?>("result", "reserved"));
        await context.Publish(new StockReserved(msg.OrderId));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

/// <summary>Compensation: returns reserved quantities. Idempotent: reservations are kept and flagged as released.</summary>
public class ReleaseStockConsumer(InventoryDbContext db) : IConsumer<ReleaseStock>
{
    public async Task Consume(ConsumeContext<ReleaseStock> context)
    {
        var reservations = await db.Reservations
            .Where(r => r.OrderId == context.Message.OrderId && !r.Released).ToListAsync(context.CancellationToken);
        foreach (var r in reservations)
        {
            (await db.Stock.FindAsync([r.Sku], context.CancellationToken))!.Available += r.Quantity;
            r.Released = true;
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}

public partial class Program;
