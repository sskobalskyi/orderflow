using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orderflow.Orders.Sagas;

namespace Orderflow.Orders.Infrastructure;

public class OrderStateMap : SagaClassMap<OrderState>
{
    protected override void Configure(EntityTypeBuilder<OrderState> e, ModelBuilder model)
    {
        e.ToTable("order_saga");
        e.Property(x => x.CurrentState).HasMaxLength(64);
        e.Property(x => x.Reason).HasMaxLength(500);
        e.Property(x => x.Total).HasPrecision(18, 2);
        // PostgreSQL system column used as the optimistic concurrency token.
        e.Property(x => x.RowVersion).HasColumnName("xmin").HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }
}
