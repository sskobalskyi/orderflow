using Orderflow.Contracts;
using Orderflow.Orders.Domain;

namespace Orderflow.Orders.Tests;

public class OrderTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private static Order NewOrder() =>
        Order.Submit(Guid.NewGuid(), [new OrderLine("LAMP-01", 2, 10m), new OrderLine("MUG-01", 1, 5.5m)], "USD", Clock);

    private static Order ConfirmedOrder()
    {
        var order = NewOrder();
        order.Confirm(Clock);
        return order;
    }

    [Fact]
    public void Submit_computes_total_and_starts_as_submitted()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Submitted, order.Status);
        Assert.Equal("USD", order.Currency);
        Assert.Equal(25.5m, order.Total);
        Assert.Equal(order.Total, order.ToSubmittedEvent().Total);
    }

    [Fact]
    public void Submit_rejects_empty_order() =>
        Assert.Equal("order_empty", Assert.Throws<DomainException>(() => Order.Submit(Guid.NewGuid(), [], "USD", Clock)).Code);

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("A", 0, 1)]
    [InlineData("A", 1, -1)]
    public void Submit_rejects_invalid_line(string sku, int qty, decimal price) =>
        Assert.Equal("order_invalid_line", Assert.Throws<DomainException>(() =>
            Order.Submit(Guid.NewGuid(), [new OrderLine(sku, qty, price)], "USD", Clock)).Code);

    [Fact]
    public void Confirm_records_when_the_order_was_confirmed()
    {
        var order = ConfirmedOrder();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.NotNull(order.ConfirmedAt);
    }

    [Fact]
    public void A_confirmed_order_ships_and_a_shipped_order_can_be_returned()
    {
        var order = ConfirmedOrder();

        order.Ship(Clock);
        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.NotNull(order.ShippedAt);

        order.RegisterReturn(Clock);
        Assert.Equal(OrderStatus.Returned, order.Status);
        Assert.NotNull(order.ReturnedAt);
    }

    [Fact]
    public void Ship_needs_a_confirmed_order() =>
        Assert.Equal("order_not_shippable", Assert.Throws<DomainException>(() => NewOrder().Ship(Clock)).Code);

    [Fact]
    public void Return_needs_a_shipped_order() =>
        Assert.Equal("order_not_returnable", Assert.Throws<DomainException>(() => ConfirmedOrder().RegisterReturn(Clock)).Code);

    [Fact]
    public void Refund_defaults_to_the_full_total()
    {
        var order = ConfirmedOrder();

        Assert.Equal(25.5m, order.EnsureRefundable(null));
        Assert.True(order.MarkRefunded(25.5m, "damaged", Clock));
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(25.5m, order.RefundAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25.51)]
    public void Refund_amount_must_be_positive_and_within_the_total(double amount) =>
        Assert.Equal("refund_invalid_amount", Assert.Throws<DomainException>(() => ConfirmedOrder().EnsureRefundable((decimal)amount)).Code);

    [Fact]
    public void An_unpaid_order_cannot_be_refunded() =>
        Assert.Equal("order_not_refundable", Assert.Throws<DomainException>(() => NewOrder().EnsureRefundable(null)).Code);

    [Fact]
    public void A_refunded_order_cannot_be_refunded_again()
    {
        var order = ConfirmedOrder();
        order.MarkRefunded(25.5m, "x", Clock);

        Assert.Equal("order_already_refunded", Assert.Throws<DomainException>(() => order.EnsureRefundable(null)).Code);
        Assert.False(order.MarkRefunded(25.5m, "x", Clock));
    }

    private static Order ReturnedOrder()
    {
        var order = ConfirmedOrder();
        order.Ship(Clock);
        order.RegisterReturn(Clock);
        return order;
    }

    [Fact]
    public void Cancelling_a_return_puts_the_order_back_to_shipped()
    {
        var order = ReturnedOrder();

        order.CancelReturn();

        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Null(order.ReturnedAt);
    }

    [Fact]
    public void A_cancelled_return_can_be_registered_again()
    {
        var order = ReturnedOrder();
        order.CancelReturn();

        order.RegisterReturn(Clock);

        Assert.Equal(OrderStatus.Returned, order.Status);
    }

    [Fact]
    public void Only_a_registered_return_can_be_cancelled() =>
        Assert.Equal("order_return_not_cancellable", Assert.Throws<DomainException>(() => ConfirmedOrder().CancelReturn()).Code);

    [Fact]
    public void A_received_return_cannot_be_cancelled()
    {
        var order = ReturnedOrder();
        order.ReceiveReturn(Clock);

        Assert.Equal("order_return_not_cancellable", Assert.Throws<DomainException>(() => order.CancelReturn()).Code);
    }

    [Fact]
    public void A_return_is_received_once()
    {
        var order = ReturnedOrder();
        order.ReceiveReturn(Clock);

        Assert.NotNull(order.ReturnReceivedAt);
        Assert.Equal("order_return_already_received", Assert.Throws<DomainException>(() => order.ReceiveReturn(Clock)).Code);
    }

    [Fact]
    public void Only_a_registered_return_can_be_received() =>
        Assert.Equal("order_return_not_receivable", Assert.Throws<DomainException>(() => ConfirmedOrder().ReceiveReturn(Clock)).Code);
}
