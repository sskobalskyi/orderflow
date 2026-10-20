extern alias InventoryApp;
extern alias NotificationsApp;
extern alias OrdersApp;
extern alias PaymentsApp;

using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Orderflow.IntegrationTests;

/// <summary>
/// Real PostgreSQL, RabbitMQ and Redis in containers, with the four services running in-process
/// against them. The gateway is skipped: tests send the identity header it would set.
/// </summary>
public class StackFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    private readonly List<IDisposable> _factories = [];

    public HttpClient Orders { get; private set; } = null!;
    public HttpClient Inventory { get; private set; } = null!;
    public HttpClient Notifications { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync(), _redis.StartAsync());

        await using (var conn = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await conn.OpenAsync();
            foreach (var db in new[] { "orders", "payments", "inventory", "notifications" })
            {
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE {db}", conn);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var rabbitUri = new Uri(_rabbit.GetConnectionString());
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:Orders"] = Db("orders"),
            ["ConnectionStrings:Payments"] = Db("payments"),
            ["ConnectionStrings:Inventory"] = Db("inventory"),
            ["ConnectionStrings:Notifications"] = Db("notifications"),
            ["RabbitMq:Host"] = rabbitUri.Host,
            ["RabbitMq:Port"] = rabbitUri.Port.ToString(),
            ["RabbitMq:Username"] = Uri.UnescapeDataString(rabbitUri.UserInfo.Split(':')[0]),
            ["RabbitMq:Password"] = Uri.UnescapeDataString(rabbitUri.UserInfo.Split(':')[1]),
            ["Redis:Configuration"] = _redis.GetConnectionString(),
        };

        Orders = Start<OrdersApp::Program>(settings);
        Start<PaymentsApp::Program>(settings);
        Inventory = Start<InventoryApp::Program>(settings);
        Notifications = Start<NotificationsApp::Program>(settings);
    }

    private string Db(string name) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;

    private HttpClient Start<TProgram>(Dictionary<string, string> settings) where TProgram : class
    {
        var factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings) b.UseSetting(key, value);
        });
        _factories.Add(factory);
        return factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        foreach (var f in _factories) f.Dispose();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

[CollectionDefinition("stack")]
public class StackCollection : ICollectionFixture<StackFixture>;
