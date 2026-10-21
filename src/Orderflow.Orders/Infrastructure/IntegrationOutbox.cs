using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orderflow.Contracts.Integration;
using RabbitMQ.Client;

namespace Orderflow.Orders.Infrastructure;

/// <summary>An integration event waiting to be published, stored in the same transaction as the change it announces.</summary>
public class IntegrationOutboxMessage
{
    /// <summary>Doubles as the `messageId` header: consumers deduplicate on it.</summary>
    public Guid Id { get; set; }
    public string RoutingKey { get; set; } = null!;
    public string Body { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string? Traceparent { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public static class IntegrationOutbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<Type, string> Addresses = new()
    {
        [typeof(OrderConfirmedV1)] = IntegrationAddress.OrderConfirmed,
        [typeof(OrderShippedV1)] = IntegrationAddress.OrderShipped,
        [typeof(OrderReturnedV1)] = IntegrationAddress.OrderReturned,
        [typeof(OrderRefundedV1)] = IntegrationAddress.OrderRefunded,
    };

    /// <summary>
    /// Queues an integration event. It is written by the same SaveChanges as the order change, so the event
    /// exists if and only if the change committed. <see cref="IntegrationOutboxPublisher"/> sends it afterwards.
    /// </summary>
    public static void AddIntegrationEvent<T>(this OrdersDbContext db, T message, TimeProvider clock) where T : class =>
        db.IntegrationOutbox.Add(new IntegrationOutboxMessage
        {
            Id = Guid.NewGuid(),
            RoutingKey = Addresses[typeof(T)],
            Body = JsonSerializer.Serialize(message, Json),
            OccurredAt = clock.GetUtcNow(),
            Traceparent = Activity.Current?.Id,
        });
}

/// <summary>
/// Relays the integration outbox to the topic exchange `integration.events` as raw JSON with the contract
/// headers as AMQP headers. At-least-once: a crash between publish and marking can send an event twice, which
/// is why consumers deduplicate on `messageId`. MassTransit's outbox is not used here because it wraps
/// messages in its own envelope, and these events are a wire contract for consumers that may not use MassTransit.
/// </summary>
public sealed class IntegrationOutboxPublisher(IServiceScopeFactory scopes, IConfiguration config, TimeProvider clock,
    ILogger<IntegrationOutboxPublisher> log) : BackgroundService
{
    private const int BatchSize = 50;

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

                await using var channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    stoppingToken);
                await channel.ExchangeDeclareAsync(IntegrationAddress.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
                    cancellationToken: stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    if (await PublishBatchAsync(channel, stoppingToken) == 0)
                        await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Integration outbox publisher lost its connection, retrying");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(IChannel channel, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        // SKIP LOCKED lets several Orders instances share the outbox without sending the same row twice.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.IntegrationOutbox
            .FromSqlRaw($"SELECT * FROM {OrdersDbContext.Schema}.integration_outbox WHERE \"PublishedAt\" IS NULL ORDER BY \"OccurredAt\" LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            var headers = new Dictionary<string, object?>
            {
                ["messageId"] = message.Id.ToString(),
                ["occurredAt"] = message.OccurredAt.ToString("O"),
                ["schemaVersion"] = "1",
            };
            if (message.Traceparent is not null) headers["traceparent"] = message.Traceparent;

            var properties = new BasicProperties
            {
                MessageId = message.Id.ToString(),
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Headers = headers,
            };
            await channel.BasicPublishAsync(IntegrationAddress.Exchange, message.RoutingKey, mandatory: false, properties,
                System.Text.Encoding.UTF8.GetBytes(message.Body), ct);
            message.PublishedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }
}
