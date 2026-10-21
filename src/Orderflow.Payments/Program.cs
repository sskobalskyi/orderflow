using Orderflow.Observability;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrderflowTelemetry("payments");

builder.Services.AddDbContext<PaymentsDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Payments")));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ProcessPaymentConsumer>();

    x.AddEntityFrameworkOutbox<PaymentsDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    // Consumers and sagas publish through the outbox too: messages leave only after the handler's
    // transaction commits, so a reply can never overtake the state it replies to.
    x.AddConfigureEndpointsCallback((ctx, _, endpoint) => endpoint.UseEntityFrameworkOutbox<PaymentsDbContext>(ctx));

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

builder.Services.AddHealthChecks().AddNpgSql(builder.Configuration.GetConnectionString("Payments")!);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();

app.MapHealthChecks("/health");
app.Run();

public class Payment
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
}

public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("app");
        b.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.HasKey(p => p.OrderId);
            e.Property(p => p.Amount).HasPrecision(18, 2);
        });
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}

/// <summary>
/// Stand-in payment gateway: orders above the configured limit are declined, which makes the
/// compensation path easy to demo. Idempotent per order: a redelivered command repeats the stored result.
/// </summary>
public class ProcessPaymentConsumer(PaymentsDbContext db, IConfiguration config) : IConsumer<ProcessPayment>
{
    public async Task Consume(ConsumeContext<ProcessPayment> context)
    {
        var msg = context.Message;
        var payment = await db.Payments.FindAsync([msg.OrderId], context.CancellationToken);

        if (payment is null)
        {
            var limit = config.GetValue("Payments:Limit", 1000m);
            var ok = msg.Amount <= limit;
            payment = new Payment
            {
                OrderId = msg.OrderId,
                Amount = msg.Amount,
                Succeeded = ok,
                FailureReason = ok ? null : $"Card declined: amount exceeds {limit}",
            };
            db.Payments.Add(payment);
            OrderflowMetrics.PaymentsProcessed.Add(1, new KeyValuePair<string, object?>("result", ok ? "completed" : "declined"));
        }

        if (payment.Succeeded)
            await context.Publish(new PaymentCompleted(msg.OrderId));
        else
            await context.Publish(new PaymentFailed(msg.OrderId, payment.FailureReason!));

        await db.SaveChangesAsync(context.CancellationToken);
    }
}

public partial class Program;
