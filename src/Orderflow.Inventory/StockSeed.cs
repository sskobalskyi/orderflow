using System.Text.Json;

namespace Orderflow.Inventory;

public record SeedStock(string Sku, int Available);

/// <summary>
/// Initial stock for a deployment, as a JSON array of `{ "sku": "...", "available": 50 }`. Inventory loads it into
/// an empty stock table, next to the catalog seed (`Catalog:SeedFile`), so a deployment sells its own products
/// by swapping two files and Inventory knows nothing about what the SKUs are.
/// </summary>
public static class StockSeed
{
    public static readonly IReadOnlyList<SeedStock> Demo =
        [new("LAMP-01", 100), new("MUG-01", 50), new("DESK-01", 5)];

    public static IReadOnlyList<SeedStock> Parse(string json)
    {
        var items = JsonSerializer.Deserialize<List<SeedStock>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidDataException("The stock seed must be a JSON array.");

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
                throw new InvalidDataException("Every stock seed entry needs a SKU.");
            if (item.Available < 0)
                throw new InvalidDataException($"Stock for '{item.Sku}' cannot be negative.");
        }

        var duplicate = items.GroupBy(i => i.Sku).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"SKU '{duplicate.Key}' appears more than once in the stock seed.");

        return items;
    }

    /// <summary>Reads `Inventory:SeedFile` when configured, otherwise the demo stock.</summary>
    public static IReadOnlyList<SeedStock> Load(string? path) =>
        path is null ? Demo : Parse(File.ReadAllText(path));
}
