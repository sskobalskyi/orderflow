using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orderflow.IntegrationTests;

/// <summary>
/// The integration events other projects consume. Each test listens on the exchange like an outside
/// consumer and validates the raw message against the AsyncAPI contract.
/// </summary>
[Collection("stack")]
public class MarketplaceEventTests(StackFixture stack)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private async Task<(Guid Customer, Guid Order)> ConfirmedOrder()
    {
        var customer = Guid.NewGuid();
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new { lines = new[] { new { sku = "LAMP-01", quantity = 2 }, new { sku = "MUG-01", quantity = 1 } } }),
        };
        req.Headers.Add("X-Customer-Id", customer.ToString());
        var res = await stack.Orders.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await WaitForStatus(customer, id, "Confirmed");
        return (customer, id);
    }

    private async Task<JsonElement> WaitForStatus(Guid customer, Guid id, string status)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/orders/{id}");
            req.Headers.Add("X-Customer-Id", customer.ToString());
            var order = await (await stack.Orders.SendAsync(req)).Content.ReadFromJsonAsync<JsonElement>();
            if (order.GetProperty("status").GetString() == status) return order;
            await Task.Delay(200);
        }

        throw new TimeoutException($"Order {id} never reached {status}.");
    }

    private Task<HttpResponseMessage> Admin(Guid order, string action, object? body = null) =>
        stack.Orders.PostAsJsonAsync($"/admin/orders/{order}/{action}", body ?? new { });

    private Task<HttpResponseMessage> Return(Guid customer, Guid order)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/orders/{order}/return");
        req.Headers.Add("X-Customer-Id", customer.ToString());
        return stack.Orders.SendAsync(req);
    }

    private async Task<List<string>> Steps(Guid customer, Guid id)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/orders/{id}/timeline");
        req.Headers.Add("X-Customer-Id", customer.ToString());
        var timeline = await (await stack.Orders.SendAsync(req)).Content.ReadFromJsonAsync<JsonElement>();
        return timeline.EnumerateArray().Select(s => s.GetProperty("step").GetString()!).ToList();
    }

    private static void AssertConforms(PublishedEvent evt)
    {
        var errors = IntegrationContract.Validate(evt);
        Assert.True(errors.Count == 0, $"{evt.RoutingKey} violates the contract:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}{Environment.NewLine}{evt.Payload}{Environment.NewLine}{evt.Headers}");
    }

    [Fact]
    public async Task Confirming_an_order_publishes_order_confirmed_v1_with_customer_lines_and_total()
    {
        var (customer, id) = await ConfirmedOrder();

        var evt = await stack.Events.WaitFor("order.confirmed.v1", p => p["orderId"]!.GetValue<string>() == id.ToString());

        AssertConforms(evt);
        Assert.Equal(customer.ToString(), evt.Payload["customerId"]!.GetValue<string>());
        Assert.Equal(62.5m, evt.Payload["total"]!.GetValue<decimal>());
        Assert.Equal("USD", evt.Payload["currency"]!.GetValue<string>());
        Assert.Equal(["LAMP-01", "MUG-01"], evt.Payload["lines"]!.AsArray().Select(l => l!["sku"]!.GetValue<string>()).Order());
    }

    [Fact]
    public async Task Published_events_carry_the_contract_headers()
    {
        var (_, id) = await ConfirmedOrder();

        var evt = await stack.Events.WaitFor("order.confirmed.v1", p => p["orderId"]!.GetValue<string>() == id.ToString());

        Assert.True(Guid.TryParse(evt.Headers["messageId"]?.GetValue<string>(), out _));
        Assert.Equal("1", evt.Headers["schemaVersion"]!.GetValue<string>());
        Assert.NotNull(evt.Headers["occurredAt"]);
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$", evt.Headers["traceparent"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_rejected_order_publishes_no_integration_event()
    {
        var customer = Guid.NewGuid();
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new { lines = new[] { new { sku = "DESK-01", quantity = 1 } } }),
        };
        req.Headers.Add("X-Customer-Id", customer.ToString());
        var id = (await (await stack.Orders.SendAsync(req)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await WaitForStatus(customer, id, "Rejected");
        await Task.Delay(1500);

        Assert.DoesNotContain(stack.Events.All, e => e.Payload["orderId"]?.GetValue<string>() == id.ToString());
    }

    [Fact]
    public async Task Shipping_publishes_order_shipped_v1_and_shows_in_the_timeline()
    {
        var (customer, id) = await ConfirmedOrder();

        var res = await Admin(id, "ship");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var evt = await stack.Events.WaitFor("order.shipped.v1", p => p["orderId"]!.GetValue<string>() == id.ToString());
        AssertConforms(evt);
        Assert.Equal(customer.ToString(), evt.Payload["customerId"]!.GetValue<string>());
        Assert.Contains("Shipped", await Steps(customer, id));
        await WaitForStatus(customer, id, "Shipped");
    }

    [Fact]
    public async Task Only_a_confirmed_order_can_be_shipped()
    {
        var customer = Guid.NewGuid();
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new { lines = new[] { new { sku = "DESK-01", quantity = 1 } } }),
        };
        req.Headers.Add("X-Customer-Id", customer.ToString());
        var id = (await (await stack.Orders.SendAsync(req)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await WaitForStatus(customer, id, "Rejected");

        var res = await Admin(id, "ship");

        Assert.Equal((HttpStatusCode)422, res.StatusCode);
        Assert.Equal("order_not_shippable", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Returning_a_shipped_order_publishes_order_returned_v1()
    {
        var (customer, id) = await ConfirmedOrder();
        (await Admin(id, "ship")).EnsureSuccessStatusCode();

        var res = await Return(customer, id);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var evt = await stack.Events.WaitFor("order.returned.v1", p => p["orderId"]!.GetValue<string>() == id.ToString());
        AssertConforms(evt);
        Assert.Equal(2, evt.Payload["lines"]!.AsArray().Count);
        Assert.Contains("Returned", await Steps(customer, id));
    }

    [Fact]
    public async Task An_order_that_was_not_shipped_cannot_be_returned()
    {
        var (customer, id) = await ConfirmedOrder();

        var res = await Return(customer, id);

        Assert.Equal((HttpStatusCode)422, res.StatusCode);
        Assert.Equal("order_not_returnable", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Customers_cannot_return_someone_elses_order()
    {
        var (_, id) = await ConfirmedOrder();
        (await Admin(id, "ship")).EnsureSuccessStatusCode();

        var res = await Return(Guid.NewGuid(), id);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Refunding_returns_the_money_through_payments_and_publishes_order_refunded_v1()
    {
        var (customer, id) = await ConfirmedOrder();
        (await Admin(id, "ship")).EnsureSuccessStatusCode();
        (await Return(customer, id)).EnsureSuccessStatusCode();

        var res = await Admin(id, "refund", new { reason = "damaged in transit" });

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var evt = await stack.Events.WaitFor("order.refunded.v1", p => p["orderId"]!.GetValue<string>() == id.ToString());
        AssertConforms(evt);
        Assert.Equal(62.5m, evt.Payload["amount"]!.GetValue<decimal>());
        Assert.Equal("damaged in transit", evt.Payload["reason"]!.GetValue<string>());
        await WaitForStatus(customer, id, "Refunded");
        Assert.Equal(["Submitted", "StockReserved", "PaymentCompleted", "Confirmed", "Shipped", "Returned", "Refunded"],
            await Steps(customer, id));
    }

    [Fact]
    public async Task A_partial_refund_publishes_the_refunded_amount()
    {
        var (customer, id) = await ConfirmedOrder();

        (await Admin(id, "refund", new { amount = 10m, reason = "goodwill" })).EnsureSuccessStatusCode();

        var evt = await stack.Events.WaitFor("order.refunded.v1", p => p["orderId"]!.GetValue<string>() == id.ToString());
        AssertConforms(evt);
        Assert.Equal(10m, evt.Payload["amount"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Refund_amount_cannot_exceed_the_total_and_an_order_is_refunded_once()
    {
        var (customer, id) = await ConfirmedOrder();

        var tooMuch = await Admin(id, "refund", new { amount = 1000m });
        Assert.Equal((HttpStatusCode)422, tooMuch.StatusCode);
        Assert.Equal("refund_invalid_amount", (await tooMuch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        (await Admin(id, "refund")).EnsureSuccessStatusCode();
        await WaitForStatus(customer, id, "Refunded");

        var again = await Admin(id, "refund");
        Assert.Equal((HttpStatusCode)422, again.StatusCode);
        Assert.Equal("order_already_refunded", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await stack.Events.WaitFor("order.refunded.v1", p => p["orderId"]!.GetValue<string>() == id.ToString());
        await Task.Delay(1000);
        Assert.Single(stack.Events.All, e => e.RoutingKey == "order.refunded.v1" && e.Payload["orderId"]!.GetValue<string>() == id.ToString());
    }

    [Fact]
    public async Task The_contract_validator_rejects_a_payload_that_breaks_the_contract()
    {
        // Guards the guard: a conformance test that never fails would prove nothing.
        var broken = new PublishedEvent("order.confirmed.v1", new JsonObject { ["messageId"] = "not-a-uuid" },
            new JsonObject { ["orderId"] = "x", ["total"] = "free" });

        var errors = IntegrationContract.Validate(broken);

        Assert.NotEmpty(errors);
        await Task.CompletedTask;
    }
}
