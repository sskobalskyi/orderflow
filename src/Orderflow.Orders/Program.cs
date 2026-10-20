using MassTransit;
using StackExchange.Redis;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Orderflow.Orders.Api;
using Orderflow.Orders.Domain;
using Orderflow.Orders.Infrastructure;
using Orderflow.Orders.Sagas;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:Configuration"] ?? "localhost:6379"));
builder.Services.AddDbContext<OrdersDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Orders"),
        n => n.MigrationsHistoryTable("__ef_migrations", OrdersDbContext.Schema)));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderConfirmedConsumer>();
    x.AddConsumer<OrderRejectedConsumer>();
    x.AddConsumer<OrderTimelineProjector>();
    x.AddSagaStateMachine<OrderStateMachine, OrderState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
            r.ExistingDbContext<OrdersDbContext>();
            r.UsePostgres();
        });

    x.AddEntityFrameworkOutbox<OrdersDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });
        cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200)));
        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Orders")!);

var app = builder.Build();

app.UseExceptionHandler(errors => errors.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (ex is DomainException)
    {
        ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await Results.Problem(ex.Message, statusCode: 422, title: "Invalid order",
            extensions: new Dictionary<string, object?> { ["code"] = ((DomainException)ex).Code }).ExecuteAsync(ctx);
    }
    else
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(statusCode: 500).ExecuteAsync(ctx);
    }
}));

// Demo-grade: create the schema on startup. A migration bundle is the production-grade path.
if (app.Configuration.GetValue("Database:EnsureCreated", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreatedAsync();
}

app.MapHealthChecks("/health");
app.MapOrderEndpoints();
app.Run();

public partial class Program;
