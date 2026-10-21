using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orderflow.Catalog;
using Orderflow.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrderflowTelemetry("catalog");

builder.Services.AddDbContext<CatalogDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Catalog")));
builder.Services.AddHealthChecks().AddNpgSql(builder.Configuration.GetConnectionString("Catalog")!);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.MigrateAsync();
    await CatalogSeeder.SeedAsync(db, builder.Configuration["Catalog:SeedFile"] ?? Path.Combine(AppContext.BaseDirectory, "Seed", "products.json"));
}

app.MapHealthChecks("/health");
app.MapProductEndpoints();
app.Run();

public partial class Program;
