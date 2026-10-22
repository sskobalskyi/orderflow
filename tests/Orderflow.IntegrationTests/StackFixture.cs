extern alias CatalogApp;
extern alias InventoryApp;
extern alias NotificationsApp;
extern alias OrdersApp;
extern alias PaymentsApp;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using CatalogClient = OrdersApp::Orderflow.Orders.Infrastructure.CatalogClient;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Orderflow.IntegrationTests;

/// <summary>
/// Real PostgreSQL, RabbitMQ and Redis in containers, with the five services running in-process
/// against them. The gateway is skipped: tests send the identity header it would set.
/// </summary>
public class StackFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    private readonly List<IDisposable> _factories = [];

    public HttpClient Orders { get; private set; } = null!;
    public HttpClient Catalog { get; private set; } = null!;
    public HttpClient Inventory { get; private set; } = null!;
    public HttpClient Notifications { get; private set; } = null!;
    public IntegrationEventTap Events { get; private set; } = null!;
    public Uri BrokerUri => new(_rabbit.GetConnectionString());

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync(), _redis.StartAsync());

        await using (var conn = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await conn.OpenAsync();
            foreach (var db in new[] { "orders", "payments", "inventory", "notifications", "catalog" })
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
            ["ConnectionStrings:Catalog"] = Db("catalog"),
            ["RabbitMq:Host"] = rabbitUri.Host,
            ["RabbitMq:Port"] = rabbitUri.Port.ToString(),
            ["RabbitMq:Username"] = Uri.UnescapeDataString(rabbitUri.UserInfo.Split(':')[0]),
            ["RabbitMq:Password"] = Uri.UnescapeDataString(rabbitUri.UserInfo.Split(':')[1]),
            ["Redis:Configuration"] = _redis.GetConnectionString(),
            ["Catalog:BaseUrl"] = "http://catalog.test",
        };

        var catalog = StartFactory<CatalogApp::Program>(settings);
        Catalog = catalog.CreateClient();

        // Orders prices lines through the catalog: route its HttpClient to the in-process Catalog.
        var orders = StartFactory<OrdersApp::Program>(settings, services =>
            services.AddHttpClient<CatalogClient>().ConfigurePrimaryHttpMessageHandler(() => catalog.Server.CreateHandler()));
        Orders = orders.CreateClient();

        StartFactory<PaymentsApp::Program>(settings).CreateClient();
        Inventory = StartFactory<InventoryApp::Program>(settings).CreateClient();
        Notifications = StartFactory<NotificationsApp::Program>(settings).CreateClient();

        Events = await IntegrationEventTap.StartAsync(rabbitUri);
    }

    private string Db(string name) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;

    private WebApplicationFactory<TProgram> StartFactory<TProgram>(Dictionary<string, string> settings, Action<IServiceCollection>? configure = null)
        where TProgram : class
    {
        var factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings) b.UseSetting(key, value);
            if (configure is not null) b.ConfigureServices(configure);
        });
        _factories.Add(factory);
        return factory;
    }

    public async Task DisposeAsync()
    {
        Events?.Dispose();
        foreach (var f in _factories) f.Dispose();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}

[CollectionDefinition("stack")]
public class StackCollection : ICollectionFixture<StackFixture>;
