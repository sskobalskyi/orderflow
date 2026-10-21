using System.Net;

namespace Orderflow.Orders.Infrastructure;

public record CatalogProduct(string Sku, string Name, decimal Price, string Currency, bool Active);

/// <summary>Prices come from the catalog, never from the client.</summary>
public class CatalogClient(HttpClient http)
{
    public async Task<CatalogProduct?> GetAsync(string sku, CancellationToken ct)
    {
        var response = await http.GetAsync($"/products/{Uri.EscapeDataString(sku)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CatalogProduct>(ct);
    }
}
