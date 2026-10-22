extern alias InventoryApp;

using StockSeed = InventoryApp::Orderflow.Inventory.StockSeed;

namespace Orderflow.IntegrationTests;

public class StockSeedTests
{
    [Fact]
    public void Parses_a_stock_file()
    {
        var items = StockSeed.Parse("""[{ "sku": "GH-STARTER-01", "available": 50 }, { "sku": "GH-POT-KIT-01", "available": 30 }]""");

        Assert.Equal(["GH-STARTER-01", "GH-POT-KIT-01"], items.Select(i => i.Sku));
        Assert.Equal([50, 30], items.Select(i => i.Available));
    }

    [Fact]
    public void Without_a_file_the_demo_stock_is_used() =>
        Assert.Contains(StockSeed.Load(null), i => i.Sku == "LAMP-01");

    [Theory]
    [InlineData("""[{ "sku": "", "available": 1 }]""")]
    [InlineData("""[{ "sku": "A", "available": -1 }]""")]
    [InlineData("""[{ "sku": "A", "available": 1 }, { "sku": "A", "available": 2 }]""")]
    [InlineData("""{ "sku": "A" }""")]
    public void Rejects_an_invalid_file(string json) =>
        Assert.ThrowsAny<Exception>(() => StockSeed.Parse(json));

    [Fact]
    public void A_missing_file_is_an_error() =>
        Assert.Throws<FileNotFoundException>(() => StockSeed.Load(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json")));
}
