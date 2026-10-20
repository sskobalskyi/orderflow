using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<NotificationsDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Notifications")));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderOutcomeNotifier>();

    x.AddEntityFrameworkOutbox<NotificationsDbContext>(o => o.UsePostgres());

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });
        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200)));
        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddHealthChecks().AddNpgSql(builder.Configuration.GetConnectionString("Notifications")!);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.EnsureCreatedAsync();

app.MapHealthChecks("/health");
app.MapGet("/notifications", async (NotificationsDbContext db) =>
    await db.Notifications.AsNoTracking().OrderByDescending(n => n.SentAt).Take(100).ToListAsync());
app.Run();

public class Notification
{
    public Guid OrderId { get; set; }
    public string Kind { get; set; } = null!;
    public string Message { get; set; } = null!;
    public DateTimeOffset SentAt { get; set; }
}

public class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("notifications");
        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(n => new { n.OrderId, n.Kind });
        });
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}

/// <summary>Sends a (logged, not real) customer message once per order and outcome.</summary>
public class OrderOutcomeNotifier(NotificationsDbContext db, ILogger<OrderOutcomeNotifier> log, TimeProvider clock)
    : IConsumer<OrderConfirmed>, IConsumer<OrderRejected>
{
    public Task Consume(ConsumeContext<OrderConfirmed> context) =>
        Send(context.Message.OrderId, "Confirmed", "Your order is confirmed.", context.CancellationToken);

    public Task Consume(ConsumeContext<OrderRejected> context) =>
        Send(context.Message.OrderId, "Rejected", $"Your order was rejected: {context.Message.Reason}", context.CancellationToken);

    private async Task Send(Guid orderId, string kind, string message, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(n => n.OrderId == orderId && n.Kind == kind, ct)) return;

        log.LogInformation("Notification for order {OrderId}: {Message}", orderId, message);
        db.Notifications.Add(new Notification { OrderId = orderId, Kind = kind, Message = message, SentAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
    }
}
