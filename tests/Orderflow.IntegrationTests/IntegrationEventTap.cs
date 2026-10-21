using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Json.Schema;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using YamlDotNet.Serialization;

namespace Orderflow.IntegrationTests;

public record PublishedEvent(string RoutingKey, JsonObject Headers, JsonNode Payload);

/// <summary>
/// Listens on the topic exchange `integration.events` the way an outside consumer would: a queue bound
/// with `#`, reading raw AMQP messages. No MassTransit on this side, so what it sees is exactly the wire contract.
/// </summary>
public sealed class IntegrationEventTap : IDisposable
{
    private readonly ConcurrentQueue<PublishedEvent> _received = new();
    private IConnection _connection = null!;
    private IChannel _channel = null!;

    public static async Task<IntegrationEventTap> StartAsync(Uri broker)
    {
        var tap = new IntegrationEventTap();
        tap._connection = await new ConnectionFactory { Uri = broker }.CreateConnectionAsync();
        tap._channel = await tap._connection.CreateChannelAsync();

        // Same arguments MassTransit uses, so whichever side declares it first is fine.
        await tap._channel.ExchangeDeclareAsync("integration.events", ExchangeType.Topic, durable: true, autoDelete: false);
        var queue = await tap._channel.QueueDeclareAsync(exclusive: true, autoDelete: true);
        await tap._channel.QueueBindAsync(queue.QueueName, "integration.events", "#");

        var consumer = new AsyncEventingBasicConsumer(tap._channel);
        consumer.ReceivedAsync += (_, e) =>
        {
            var headers = new JsonObject();
            foreach (var (name, value) in e.BasicProperties.Headers ?? new Dictionary<string, object?>())
                headers[name] = value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();

            tap._received.Enqueue(new PublishedEvent(e.RoutingKey, headers, JsonNode.Parse(Encoding.UTF8.GetString(e.Body.ToArray()))!));
            return Task.CompletedTask;
        };
        await tap._channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer);
        return tap;
    }

    public IReadOnlyCollection<PublishedEvent> All => _received.ToArray();

    /// <summary>Waits for an event with the routing key whose payload satisfies the predicate.</summary>
    public async Task<PublishedEvent> WaitFor(string routingKey, Func<JsonNode, bool> matches, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            var hit = _received.FirstOrDefault(e => e.RoutingKey == routingKey && matches(e.Payload));
            if (hit is not null) return hit;
            await Task.Delay(100);
        }

        throw new TimeoutException($"No '{routingKey}' event matching the predicate arrived. Seen: {string.Join(", ", _received.Select(e => e.RoutingKey))}");
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}

/// <summary>Validates published messages against the AsyncAPI document that is the contract.</summary>
public static class IntegrationContract
{
    private static readonly JsonNode Document = Load();

    private static JsonNode Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "contracts", "integration-events.yaml");
        var yaml = new DeserializerBuilder().Build().Deserialize<object>(File.ReadAllText(path));
        return ToJson(yaml)!;
    }

    private static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        IDictionary<object, object> map => new JsonObject(map.Select(p => KeyValuePair.Create(p.Key.ToString()!, ToJson(p.Value)))),
        IEnumerable<object> list => new JsonArray(list.Select(ToJson).ToArray()),
        bool b => JsonValue.Create(b),
        string s when s is "true" or "false" => JsonValue.Create(s == "true"),
        string s when System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?[0-9]+([.][0-9]+)?$") => long.TryParse(s, out var n) ? JsonValue.Create(n) : JsonValue.Create(decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture)),
        string s => JsonValue.Create(s),
        _ => long.TryParse(value.ToString(), out var l) ? JsonValue.Create(l)
            : decimal.TryParse(value.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var d) ? JsonValue.Create(d)
            : JsonValue.Create(value.ToString()),
    };

    /// <summary>The names of the events the contract defines for a routing key, for example `OrderConfirmed` for `order.confirmed.v1`.</summary>
    public static string MessageFor(string routingKey) =>
        Document["components"]!["messages"]!.AsObject().First(m => m.Value!["name"]!.GetValue<string>() == routingKey).Key;

    public static IReadOnlyList<string> Validate(PublishedEvent evt)
    {
        var message = MessageFor(evt.RoutingKey);
        var errors = new List<string>();
        errors.AddRange(Check("#/components/schemas/Headers", evt.Headers, "headers"));
        errors.AddRange(Check($"#/components/messages/{message}/payload", evt.Payload, "payload"));
        return errors;
    }

    private static IEnumerable<string> Check(string pointer, JsonNode instance, string what)
    {
        // The AsyncAPI document is not itself a JSON Schema, so build one: every schema of the contract becomes a
        // `$defs` entry, `#/components/schemas/X` references point at it, and the target is the root `$ref`.
        var defs = new JsonObject();
        foreach (var (name, schema) in Document["components"]!["schemas"]!.AsObject())
            defs[name] = schema!.DeepClone();
        foreach (var (name, message) in Document["components"]!["messages"]!.AsObject())
            defs[$"message_{name}"] = message!["payload"]!.DeepClone();

        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"urn:orderflow:integration-events:{Guid.NewGuid()}", // one registry entry per evaluation
            ["$ref"] = pointer.Replace("#/components/schemas/", "#/$defs/")
                .Replace("#/components/messages/", "#/$defs/message_").Replace("/payload", ""),
            ["$defs"] = defs,
        };
        var text = root.ToJsonString().Replace("#/components/schemas/", "#/$defs/");

        var result = JsonSchema.FromText(text).Evaluate(System.Text.Json.JsonDocument.Parse(instance.ToJsonString()).RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        });

        return result.IsValid
            ? []
            : (result.Details ?? []).Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"{what} {d.InstanceLocation}: {e.Key} {e.Value}"));
    }
}
