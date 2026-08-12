using System.Text.Json.Serialization;

namespace SGDB.Models;

public sealed class ProductExtra
{
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "00-MERCADORIA PARA REVENDA";

    [JsonPropertyName("marca")]
    public string? Marca { get; set; }

    [JsonPropertyName("cod_balanca")]
    public string? CodBalanca { get; set; }

    [JsonPropertyName("info_complementar")]
    public string? InfoComplementar { get; set; }

    [JsonPropertyName("preco_compra")]
    public double PrecoCompra { get; set; }

    [JsonPropertyName("qtd_atacado")]
    public double QtdAtacado { get; set; }

    [JsonPropertyName("preco_atacado")]
    public double PrecoAtacado { get; set; }

    [JsonPropertyName("desconto_percent")]
    public double DescontoPercent { get; set; }

    [JsonPropertyName("custos_percent")]
    public double CustosPercent { get; set; }

    [JsonPropertyName("lucro_percent")]
    public double LucroPercent { get; set; }

    [JsonPropertyName("peso_bruto_kg")]
    public double PesoBrutoKg { get; set; }

    [JsonPropertyName("peso_liquido_kg")]
    public double PesoLiquidoKg { get; set; }

    [JsonPropertyName("validade_balanca")]
    public double ValidadeBalanca { get; set; }

    [JsonPropertyName("data_validade")]
    public string? DataValidade { get; set; }

    /// <summary>
    /// Controle de validade/lote (FEFO) na entrada de NF-e.
    /// null = ainda não definido no cadastro → inferir pela categoria/nome.
    /// </summary>
    [JsonPropertyName("controle_validade")]
    public bool? ControleValidade { get; set; }

    [JsonPropertyName("permite_venda")]
    public bool PermiteVenda { get; set; } = true;

    [JsonPropertyName("composicao")]
    public bool Composicao { get; set; }

    [JsonPropertyName("composicao_itens")]
    public List<ProductCompositionItem> ComposicaoItens { get; set; } = [];

    [JsonPropertyName("fabricado")]
    public bool Fabricado { get; set; }

    [JsonPropertyName("pesavel")]
    public bool Pesavel { get; set; }

    [JsonPropertyName("preco_promocional")]
    public double PrecoPromocional { get; set; }

    [JsonPropertyName("promo_inicio")]
    public string? PromoInicio { get; set; }

    [JsonPropertyName("promo_fim")]
    public string? PromoFim { get; set; }

    [JsonPropertyName("price_table_id")]
    public int? PriceTableId { get; set; }

    [JsonPropertyName("vasilhame_tipo_id")]
    public int? VasilhameTipoId { get; set; }

    [JsonPropertyName("vasilhame_qty")]
    public double VasilhameQty { get; set; } = 1;

    /// <summary>Quantas unidades de venda (lata/UN) cabem em 1 fardo/CX/EB na compra.</summary>
    [JsonPropertyName("fator_embalagem")]
    public double FatorEmbalagem { get; set; } = 1;

    /// <summary>Código de barras do fardo/caixa (quando diferente da unidade).</summary>
    [JsonPropertyName("barcode_embalagem")]
    public string? BarcodeEmbalagem { get; set; }

    public static ProductExtra Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ProductExtra();

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ProductExtra>(json) ?? new ProductExtra();
        }
        catch
        {
            return new ProductExtra();
        }
    }

    public string ToJson() =>
        System.Text.Json.JsonSerializer.Serialize(this);
}
