using System.Text.Json.Serialization;
using SGDB.Utils;

namespace SGDB.Models;

public sealed class ProductCompositionItem
{
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [JsonPropertyName("quantity")]
    public double Quantity { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "UN";

    [JsonPropertyName("cost")]
    public double Cost { get; set; }

    public string QtyDisplay => Quantity.ToString("N3");
    public string CostDisplay => ProductPriceHelper.MoneyBr(Cost);
}
