using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Orderflow.Catalog;

public record ProductText(string Name, string Description);

public class Product
{
    public string Sku { get; set; } = null!;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Active { get; set; } = true;

    /// <summary>Texts by locale tag (`en`, `uk`, ...). Stored as JSON so a new locale needs no migration.</summary>
    public Dictionary<string, ProductText> Translations { get; set; } = [];

    public ProductText TextFor(string locale) =>
        Translations.GetValueOrDefault(locale) ?? Translations.GetValueOrDefault("en") ?? Translations.Values.FirstOrDefault() ?? new(Sku, "");
}

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("app");
        b.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(p => p.Sku);
            e.Property(p => p.Sku).HasMaxLength(64);
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.Property(p => p.Currency).HasMaxLength(3);
            e.Property(p => p.Translations).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, ProductText>>(v, (JsonSerializerOptions?)null) ?? new(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Dictionary<string, ProductText>>(
                    (a, c) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => JsonSerializer.Deserialize<Dictionary<string, ProductText>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
        });
    }
}

public static class CatalogSeeder
{
    private record SeedProduct(string Sku, decimal Price, string Currency, Dictionary<string, ProductText> Translations);

    /// <summary>Loads the products file once, into an empty catalog. A deployment swaps the file to sell something else.</summary>
    public static async Task SeedAsync(CatalogDbContext db, string path)
    {
        if (await db.Products.AnyAsync() || !File.Exists(path)) return;

        var items = JsonSerializer.Deserialize<List<SeedProduct>>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        db.Products.AddRange(items.Select(i => new Product { Sku = i.Sku, Price = i.Price, Currency = i.Currency, Translations = i.Translations }));
        await db.SaveChangesAsync();
    }
}

public record UpsertProductRequest(decimal Price, string Currency, bool Active, Dictionary<string, ProductText> Translations);

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/products", async (HttpRequest http, CatalogDbContext db, CancellationToken ct) =>
        {
            var locale = Locale(http);
            var products = await db.Products.AsNoTracking().Where(p => p.Active).OrderBy(p => p.Sku).ToListAsync(ct);
            return Results.Ok(products.Select(p => ToDto(p, locale)));
        });

        app.MapGet("/products/{sku}", async (string sku, HttpRequest http, CatalogDbContext db, CancellationToken ct) =>
            await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Sku == sku, ct) is { } product
                ? Results.Ok(ToDto(product, Locale(http)))
                : Results.NotFound());

        // Gateway-protected: only admins reach this route.
        app.MapPut("/admin/products/{sku}", async (string sku, UpsertProductRequest req, CatalogDbContext db, CancellationToken ct) =>
        {
            if (req.Price < 0 || req.Currency is not { Length: 3 } || !req.Translations.ContainsKey("en"))
                return Results.Problem("A product needs a non-negative price, a 3-letter currency and an English text.",
                    statusCode: 422, extensions: new Dictionary<string, object?> { ["code"] = "product_invalid" });

            var product = await db.Products.FirstOrDefaultAsync(p => p.Sku == sku, ct);
            if (product is null) db.Products.Add(product = new Product { Sku = sku });
            product.Price = req.Price;
            product.Currency = req.Currency.ToUpperInvariant();
            product.Active = req.Active;
            product.Translations = req.Translations;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(product, "en"));
        });
    }

    /// <summary>`?lang=` wins, then the first Accept-Language tag, then English.</summary>
    private static string Locale(HttpRequest http)
    {
        var requested = http.Query["lang"].FirstOrDefault() ?? http.Headers.AcceptLanguage.FirstOrDefault()?.Split(',')[0].Split(';')[0].Split('-')[0];
        return string.IsNullOrWhiteSpace(requested) ? "en" : requested.ToLowerInvariant();
    }

    private static object ToDto(Product p, string locale)
    {
        var text = p.TextFor(locale);
        return new { p.Sku, text.Name, text.Description, p.Price, p.Currency, p.Active };
    }
}
