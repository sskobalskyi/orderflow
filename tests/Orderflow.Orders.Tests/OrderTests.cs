using Orderflow.Contracts;
using Orderflow.Orders.Domain;

namespace Orderflow.Orders.Tests;

public class OrderTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Fact]
    public void Submit_computes_total_and_starts_as_submitted()
    {
        var order = Order.Submit(Guid.NewGuid(),
            [new OrderLine("A", 2, 10m), new OrderLine("B", 1, 5.5m)], Clock);

        Assert.Equal(OrderStatus.Submitted, order.Status);
        Assert.Equal(25.5m, order.Total);
        Assert.Equal(order.Total, order.ToSubmittedEvent().Total);
    }

    [Fact]
    public void Submit_rejects_empty_order() =>
        Assert.Throws<DomainException>(() => Order.Submit(Guid.NewGuid(), [], Clock));

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("A", 0, 1)]
    [InlineData("A", 1, -1)]
    public void Submit_rejects_invalid_line(string sku, int qty, decimal price) =>
        Assert.Throws<DomainException>(() =>
            Order.Submit(Guid.NewGuid(), [new OrderLine(sku, qty, price)], Clock));
}
