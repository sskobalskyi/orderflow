using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RabbitMQ.Client;

namespace Orderflow.IntegrationTests;

/// <summary>
/// Another system reports a device claimed. Orderflow shows the order it was sold in as in use. The test publishes the event
/// the way that system does, raw JSON with the contract headers, and checks it against the contract first.
/// </summary>
[Collection("stack")]
public class DeviceClaimedTests(StackFixture stack)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private async Task<(Guid Customer, Guid Order)> ShippedOrder()
    {
        var customer = Guid.NewGuid();
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders")
        {
            Content = JsonContent.Create(new { lines = new[] { new { sku = "LAMP-01", quantity = 1 } } }),
        };
        req.Headers.Add("X-Customer-Id", customer.ToString());
        var res = await stack.Orders.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await WaitFor(customer, id, o => o.GetProperty("status").GetString() == "Confirmed");
        (await stack.Orders.PostAsJsonAsync($"/admin/orders/{id}/ship", new { })).EnsureSuccessStatusCode();
        await WaitFor(customer, id, o => o.GetProperty("status").GetString() == "Shipped");
        return (customer, id);
    }

    private async Task<JsonElement> WaitFor(Guid customer, Guid id, Func<JsonElement, bool> done)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/orders/{id}");
            req.Headers.Add("X-Customer-Id", customer.ToString());
            var order = await (await stack.Orders.SendAsync(req)).Content.ReadFromJsonAsync<JsonElement>();
            if (done(order)) return order;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Order {id} never reached the expected state.");
    }

    private async Task<List<(string Step, string? Detail)>> Timeline(Guid customer, Guid id)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/orders/{id}/timeline");
        req.Headers.Add("X-Customer-Id", customer.ToString());
        var timeline = await (await stack.Orders.SendAsync(req)).Content.ReadFromJsonAsync<JsonElement>();
        return timeline.EnumerateArray().Select(s => (s.GetProperty("step").GetString()!, s.TryGetProperty("detail", out var d) ? d.GetString() : null)).ToList();
    }

    /// <summary>Publishes `device.claimed.v1` as an outside system would, after checking it against the contract.</summary>
    private async Task Claimed(Guid? orderId, string deviceId, Guid? messageId = null)
    {
        var payload = new JsonObject { ["deviceId"] = deviceId, ["ownerId"] = "someone", ["potId"] = Guid.NewGuid().ToString(), ["claimedAt"] = DateTimeOffset.UtcNow.ToString("O") };
        if (orderId is not null) payload["orderId"] = orderId.ToString();
        var id = messageId ?? Guid.NewGuid();
        var headers = new JsonObject { ["messageId"] = id.ToString(), ["occurredAt"] = DateTimeOffset.UtcNow.ToString("O"), ["schemaVersion"] = "1" };

        var errors = IntegrationContract.Validate(new PublishedEvent("device.claimed.v1", headers, payload));
        Assert.True(errors.Count == 0, "The test event violates the contract: " + string.Join("; ", errors));

        await using var connection = await new ConnectionFactory { Uri = stack.BrokerUri }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync("integration.events", ExchangeType.Topic, durable: true, autoDelete: false);
        var properties = new BasicProperties
        {
            MessageId = id.ToString(), ContentType = "application/json",
            Headers = new Dictionary<string, object?> { ["messageId"] = id.ToString(), ["occurredAt"] = headers["occurredAt"]!.ToString(), ["schemaVersion"] = "1" },
        };
        await channel.BasicPublishAsync("integration.events", "device.claimed.v1", false, properties, Encoding.UTF8.GetBytes(payload.ToJsonString()));
    }

    [Fact]
    public async Task A_claimed_device_marks_its_shipped_order_as_activated_once()
    {
        var (customer, id) = await ShippedOrder();
        var message = Guid.NewGuid();

        await Claimed(id, "ghx-0001", message);
        var order = await WaitFor(customer, id, o => o.TryGetProperty("activatedAt", out var a) && a.ValueKind == JsonValueKind.String);

        Assert.Equal("Shipped", order.GetProperty("status").GetString());
        var activated = Assert.Single((await Timeline(customer, id)).Where(s => s.Step == "Activated"));
        Assert.Equal("ghx-0001", activated.Detail);

        // The same message again, and a second device of the same order, change nothing.
        await Claimed(id, "ghx-0001", message);
        await Claimed(id, "ghx-0002");
        await Task.Delay(1500);
        Assert.Single((await Timeline(customer, id)).Where(s => s.Step == "Activated"));
    }

    [Fact]
    public async Task A_claim_for_a_device_that_was_not_sold_here_or_an_unknown_order_is_ignored_without_blocking_the_queue()
    {
        var (customer, id) = await ShippedOrder();

        await Claimed(null, "diy-1");
        await Claimed(Guid.NewGuid(), "ghx-unknown-order");
        await Claimed(id, "ghx-after");

        // The last one is handled, so the two before it were acknowledged rather than stuck.
        await WaitFor(customer, id, o => o.TryGetProperty("activatedAt", out var a) && a.ValueKind == JsonValueKind.String);
        Assert.Equal("ghx-after", Assert.Single((await Timeline(customer, id)).Where(s => s.Step == "Activated")).Detail);
    }

    [Fact]
    public async Task An_order_that_is_not_shipped_is_not_activated()
    {
        var customer = Guid.NewGuid();
        var req = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = JsonContent.Create(new { lines = new[] { new { sku = "MUG-01", quantity = 1 } } }) };
        req.Headers.Add("X-Customer-Id", customer.ToString());
        var id = (await (await stack.Orders.SendAsync(req)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await WaitFor(customer, id, o => o.GetProperty("status").GetString() == "Confirmed");

        await Claimed(id, "too-early");
        await Task.Delay(1500);

        var order = await WaitFor(customer, id, _ => true);
        Assert.False(order.TryGetProperty("activatedAt", out var a) && a.ValueKind == JsonValueKind.String);
        Assert.DoesNotContain(await Timeline(customer, id), s => s.Step == "Activated");
    }
}