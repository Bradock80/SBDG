using System.Text.Json;
using System.Text.Json.Serialization;

namespace SGDB.Models;

public sealed class PurchaseParcelaDraft
{
    [JsonPropertyName("vencimento")]
    public string Vencimento { get; set; } = "";

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "Boleto";

    [JsonPropertyName("valor")]
    public double Valor { get; set; }
}

public sealed class PurchaseFinanceiroMeta
{
    [JsonPropertyName("entrada")]
    public double Entrada { get; set; }

    [JsonPropertyName("parcelas")]
    public List<PurchaseParcelaDraft> Parcelas { get; set; } = [];

    [JsonPropertyName("qtd")]
    public int Qtd { get; set; }
}
