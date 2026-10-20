using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orderflow.IntegrationTests;

[Collection("stack")]
public class OrderFlowTests(StackFixture stack)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static HttpRequestMessage Submit(Guid customer, string sku, int quantity, decimal price, string? idempotencyKey = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new { lines = new[] { new { sku, quantity, unitPrice = price } } }),
        };
        req.Headers.Add("X-Customer-Id", customer.ToString());
        if (idempotencyKey is not null) req.Headers.Add("Idempotency-Key", idempotencyKey);
        return req;
    }

    private static HttpRequestMessage Get(Guid customer, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Customer-Id", customer.ToString());
        return req;
    }

    private async Task<JsonElement> PlaceOrder(Guid customer, string sku, int quantity, decimal price, string? key = null)
    {
        var res = await stack.Orders.SendAsync(Submit(customer, sku, quantity, price, key));
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> WaitForFinalStatus(Guid customer, Guid orderId)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var order = await (await stack.Orders.SendAsync(Get(customer, $"/orders/{orderId}"))).Content.ReadFromJsonAsync<JsonElement>();
            if (order.GetProperty("status").GetString() != "Submitted") return order;
            await Task.Delay(200);
        }

        throw new TimeoutException($"Order {orderId} did not leave Submitted within {Timeout}.");
    }

    private async Task<int> Available(string sku)
    {
        var stock = await stack.Inventory.GetFromJsonAsync<JsonElement>("/stock");
        return stock.EnumerateArray().First(s => s.GetProperty("sku").GetString() == sku).GetProperty("available").GetInt32();
    }

    private async Task<List<string>> Steps(Guid customer, Guid orderId)
    {
        var timeline = await (await stack.Orders.SendAsync(Get(customer, $"/orders/{orderId}/timeline"))).Content.ReadFromJsonAsync<JsonElement>();
        return timeline.EnumerateArray().Select(s => s.GetProperty("step").GetString()!).ToList();
    }

    [Fact]
    public async Task Order_is_confirmed_and_stock_is_reserved()
    {
        var customer = Guid.NewGuid();
        var before = await Available("A");

        var created = await PlaceOrder(customer, "A", 2, 10m);
        var id = created.GetProperty("id").GetGuid();
        var order = await WaitForFinalStatus(customer, id);

        Assert.Equal("Confirmed", order.GetProperty("status").GetString());
        Assert.Equal(before - 2, await Available("A"));
        Assert.Contains("Confirmed", await Steps(customer, id));
    }

    [Fact]
    public async Task Declined_payment_rejects_the_order_and_releases_stock()
    {
        var customer = Guid.NewGuid();
        var before = await Available("B");

        var created = await PlaceOrder(customer, "B", 1, 5000m);
        var id = created.GetProperty("id").GetGuid();
        var order = await WaitForFinalStatus(customer, id);

        Assert.Equal("Rejected", order.GetProperty("status").GetString());
        Assert.Contains("declined", order.GetProperty("rejectionReason").GetString());
        Assert.Equal(["Submitted", "StockReserved", "PaymentFailed", "Rejected"], await Steps(customer, id));

        // The compensation arrives asynchronously after the order is already rejected.
        await Eventually(async () => await Available("B") == before);
    }

    [Fact]
    public async Task Insufficient_stock_rejects_without_charging()
    {
        var customer = Guid.NewGuid();
        var created = await PlaceOrder(customer, "C", 999, 1m);
        var id = created.GetProperty("id").GetGuid();
        var order = await WaitForFinalStatus(customer, id);

        Assert.Equal("Rejected", order.GetProperty("status").GetString());
        Assert.DoesNotContain("PaymentFailed", await Steps(customer, id));
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_order()
    {
        var customer = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();

        var first = await PlaceOrder(customer, "A", 1, 1m, key);
        var second = await PlaceOrder(customer, "A", 1, 1m, key);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        var list = await (await stack.Orders.SendAsync(Get(customer, "/orders"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetArrayLength());
    }

    [Fact]
    public async Task Customers_cannot_see_each_others_orders()
    {
        var owner = Guid.NewGuid();
        var created = await PlaceOrder(owner, "A", 1, 1m);
        var id = created.GetProperty("id").GetGuid();

        var res = await stack.Orders.SendAsync(Get(Guid.NewGuid(), $"/orders/{id}"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Requests_without_identity_are_rejected()
    {
        var res = await stack.Orders.GetAsync("/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Invalid_order_returns_a_localizable_error_code()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = JsonContent.Create(new { lines = Array.Empty<object>() }) };
        req.Headers.Add("X-Customer-Id", Guid.NewGuid().ToString());

        var res = await stack.Orders.SendAsync(req);

        Assert.Equal((HttpStatusCode)422, res.StatusCode);
        Assert.Equal("order_empty", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Customer_is_notified_once_per_outcome()
    {
        var customer = Guid.NewGuid();
        var created = await PlaceOrder(customer, "A", 1, 1m);
        var id = created.GetProperty("id").GetGuid();
        await WaitForFinalStatus(customer, id);

        await Eventually(async () =>
        {
            var list = await stack.Notifications.GetFromJsonAsync<JsonElement>("/notifications");
            return list.EnumerateArray().Count(n => n.GetProperty("orderId").GetGuid() == id) == 1;
        });
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(200);
        }

        throw new TimeoutException("Condition was not met in time.");
    }
}
