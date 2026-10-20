using MassTransit;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Orderflow.Orders.Api;
using Orderflow.Orders.Domain;
using Orderflow.Orders.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<OrdersDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Orders"),
        n => n.MigrationsHistoryTable("__ef_migrations", OrdersDbContext.Schema)));

builder.Services.AddMassTransit(x =>
{
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
        await Results.Problem(ex.Message, statusCode: 422, title: "Invalid order").ExecuteAsync(ctx);
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
