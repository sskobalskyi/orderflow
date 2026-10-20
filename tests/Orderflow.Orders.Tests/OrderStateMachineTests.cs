using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orderflow.Contracts;
using Orderflow.Orders.Sagas;

namespace Orderflow.Orders.Tests;

public class OrderStateMachineTests
{
    private static ServiceProvider Build() => new ServiceCollection()
        .AddMassTransitTestHarness(x => x.AddSagaStateMachine<OrderStateMachine, OrderState>())
        .BuildServiceProvider(true);

    private static OrderSubmitted NewOrder() =>
        new(Guid.NewGuid(), Guid.NewGuid(), [new OrderLine("A", 1, 10m)], 10m);

    [Fact]
    public async Task Happy_path_confirms_the_order()
    {
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var saga = harness.GetSagaStateMachineHarness<OrderStateMachine, OrderState>();
        var order = NewOrder();

        await harness.Bus.Publish(order);
        Assert.True(await harness.Published.Any<ReserveStock>());
        await harness.Bus.Publish(new StockReserved(order.OrderId));
        Assert.True(await harness.Published.Any<ProcessPayment>());
        await harness.Bus.Publish(new PaymentCompleted(order.OrderId));

        Assert.True(await harness.Published.Any<OrderConfirmed>());
        Assert.NotNull(await saga.Exists(order.OrderId, s => s.Confirmed));
    }

    [Fact]
    public async Task Payment_failure_releases_stock_and_rejects()
    {
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var saga = harness.GetSagaStateMachineHarness<OrderStateMachine, OrderState>();
        var order = NewOrder();

        await harness.Bus.Publish(order);
        await harness.Bus.Publish(new StockReserved(order.OrderId));
        await harness.Bus.Publish(new PaymentFailed(order.OrderId, "declined"));

        Assert.True(await harness.Published.Any<ReleaseStock>());
        Assert.True(await harness.Published.Any<OrderRejected>());
        Assert.NotNull(await saga.Exists(order.OrderId, s => s.Rejected));
    }

    [Fact]
    public async Task Out_of_stock_rejects_without_charging()
    {
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var order = NewOrder();

        await harness.Bus.Publish(order);
        await harness.Bus.Publish(new StockRejected(order.OrderId, "none left"));

        Assert.True(await harness.Published.Any<OrderRejected>());
        Assert.False(await harness.Published.Any<ProcessPayment>());
    }
}
