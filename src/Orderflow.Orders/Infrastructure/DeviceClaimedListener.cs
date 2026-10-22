using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts.Integration;
using Orderflow.Orders.Domain;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Orderflow.Orders.Infrastructure;

/// <summary>A message an inbound consumer has already handled. Redeliveries find the row and are skipped.</summary>
public class IntegrationInboxMessage
{
    public Guid MessageId { get; set; }
    public string Consumer { get; set; } = null!;
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>
/// Listens for `device.claimed.v1` on the integration exchange and marks the order the device was sold in as activated.
/// The message is raw JSON from outside, so this reads it with the raw client like the publisher writes it. Orderflow
/// only reads the order id, the device id and the time: what the device is, and who owns it, is not its business.
/// A message without an order (a device that was not sold here) is acknowledged and ignored. A message that cannot be
/// handled is retried once and then dead-lettered, so it never blocks the queue.
/// </summary>
public sealed class DeviceClaimedListener(IServiceScopeFactory scopes, IConfiguration config, ILogger<DeviceClaimedListener> log) : BackgroundService
{
    public const string Queue = "orders.device-claimed";
    private const string Consumer = "orders-device-claimed";
    private const string DeadLetterExchange = "orders.integration.dlx";
    private static readonly ActivitySource Source = new("Orderflow.Orders.Integration");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await new ConnectionFactory
                {
                    HostName = config["RabbitMq:Host"] ?? "localhost",
                    Port = config.GetValue("RabbitMq:Port", 5672),
                    UserName = config["RabbitMq:Username"] ?? "guest",
                    Password = config["RabbitMq:Password"] ?? "guest",
                }.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                await channel.ExchangeDeclareAsync(IntegrationAddress.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(Queue + ".dead", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(Queue + ".dead", DeadLetterExchange, "", cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange }, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(Queue, IntegrationAddress.Exchange, IntegrationAddress.DeviceClaimed, cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, 10, false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => OnMessage(channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Device-claimed listener lost its connection, retrying");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task OnMessage(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var traceparent = Text(delivery.BasicProperties.Headers, "traceparent");
        ActivityContext.TryParse(traceparent, null, out var parent);
        using var activity = Source.StartActivity($"{delivery.RoutingKey} process", ActivityKind.Consumer, parent);

        try
        {
            var messageId = Guid.TryParse(Text(delivery.BasicProperties.Headers, "messageId") ?? delivery.BasicProperties.MessageId, out var id)
                ? id : throw new InvalidDataException("The message has no message id");
            using var body = JsonDocument.Parse(delivery.Body);
            var root = body.RootElement;

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

            if (!await db.IntegrationInbox.AnyAsync(m => m.MessageId == messageId && m.Consumer == Consumer, ct))
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await ApplyAsync(db, root, clock, ct);
                db.IntegrationInbox.Add(new IntegrationInboxMessage { MessageId = messageId, Consumer = Consumer, ProcessedAt = clock.GetUtcNow() });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A duplicate that raced past the check collides on the inbox key: it was handled.
            if (ex is DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } } && ex.ToString().Contains("integration_inbox"))
            {
                await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
                return;
            }
            var retry = !delivery.Redelivered && ex is not (InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException);
            log.LogWarning(ex, "Could not handle {Key}, {Outcome}", delivery.RoutingKey, retry ? "retrying once" : "dead-lettering");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: retry, ct);
        }
    }

    private static async Task ApplyAsync(OrdersDbContext db, JsonElement root, TimeProvider clock, CancellationToken ct)
    {
        // A device that was not sold through this shop names no order.
        if (!root.TryGetProperty("orderId", out var orderProperty) || orderProperty.ValueKind != JsonValueKind.String) return;
        var orderId = orderProperty.GetGuid();
        var deviceId = root.GetProperty("deviceId").GetString()!;
        var claimedAt = root.GetProperty("claimedAt").GetDateTimeOffset();

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || !order.MarkActivated(claimedAt)) return;
        db.Timeline.Add(new TimelineEntry { OrderId = order.Id, Step = "Activated", Detail = deviceId, At = clock.GetUtcNow() });
    }

    private static string? Text(IDictionary<string, object?>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var v) ? v switch { byte[] b => System.Text.Encoding.UTF8.GetString(b), _ => v?.ToString() } : null;
}