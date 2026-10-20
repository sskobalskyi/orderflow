using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts;
using Orderflow.Orders.Domain;

namespace Orderflow.Orders.Infrastructure;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : SagaDbContext(options)
{
    // Every service owns a separate database, so the schema name can be uniform. MassTransit caches its
    // outbox SQL statically per process, which also makes in-process integration tests work.
    public const string Schema = "app";

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<TimelineEntry> Timeline => Set<TimelineEntry>();

    protected override IEnumerable<ISagaClassMap> Configurations => [new OrderStateMap()];

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);

        b.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.HasKey(o => o.Id);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.Ignore(o => o.Total);
            e.OwnsMany(o => o.Lines, l =>
            {
                l.ToTable("order_lines");
                l.WithOwner().HasForeignKey("OrderId");
                l.Property<int>("Id");
                l.HasKey("Id");
                l.Property(x => x.Sku).HasMaxLength(64);
                l.Property(x => x.UnitPrice).HasPrecision(18, 2);
            });
        });

        b.Entity<TimelineEntry>(e =>
        {
            e.ToTable("order_timeline");
            e.HasIndex(t => new { t.OrderId, t.Step }).IsUnique();
            e.Property(t => t.Step).HasMaxLength(40);
            e.Property(t => t.Detail).HasMaxLength(500);
        });

        // Transactional outbox tables: events are stored in the same transaction as the order.
        b.AddInboxStateEntity();
        b.AddOutboxMessageEntity();
        b.AddOutboxStateEntity();
    }
}
