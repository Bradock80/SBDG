using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using SGDB.Utils;

namespace SGDB.Models;

public sealed class MovimentacaoProdutoRow
{
    public int SaleId { get; set; }
    public string SaleDateBr { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string PaymentType { get; set; } = "";
    public double UnitCost { get; set; }
    public double Qty { get; set; }
    public double UnitSale { get; set; }
    public double Discount { get; set; }
    public double Acrescimo { get; set; }
    public double Total { get; set; }
    public double FeePercent { get; set; }
    public double TaxaValor { get; set; }
    public double TotalLiquido { get; set; }
    public double LucroBruto { get; set; }
    public double LucroLiquido { get; set; }

    [JsonIgnore] public string SaleIdDisplay => SaleId.ToString(CultureInfo.InvariantCulture);
    [JsonIgnore] public string ProductNameDisplay => MovimentacaoFormat.TitleCase(ProductName);
    [JsonIgnore] public string UnitCostDisplay => MovimentacaoFormat.Money(UnitCost);
    [JsonIgnore] public string QtyDisplay => MovimentacaoFormat.Qty(Qty);
    [JsonIgnore] public string UnitSaleDisplay => MovimentacaoFormat.Money(UnitSale);
    [JsonIgnore] public string DiscountDisplay => MovimentacaoFormat.Money(Discount);
    [JsonIgnore] public string AcrescimoDisplay => MovimentacaoFormat.Money(Acrescimo);
    [JsonIgnore] public string TotalDisplay => MovimentacaoFormat.Money(Total);
    [JsonIgnore] public string LucroBrutoDisplay => MovimentacaoFormat.Money(LucroBruto);
}

public sealed class MovimentacaoVendaRow
{
    public int SaleId { get; set; }
    public string SaleDateBr { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string SellerName { get; set; } = "";
    public string PaymentType { get; set; } = "";
    public int ItemsCount { get; set; }
    public double CostTotal { get; set; }
    public double Total { get; set; }
    public double FeePercent { get; set; }
    public double TaxaValor { get; set; }
    public double TotalLiquido { get; set; }
    public double LucroBruto { get; set; }
    public double LucroLiquido { get; set; }

    [JsonIgnore] public string SaleIdDisplay => SaleId.ToString(CultureInfo.InvariantCulture);
    [JsonIgnore] public string CustomerNameDisplay => MovimentacaoFormat.TitleCase(CustomerName);
    [JsonIgnore] public string SellerNameDisplay => MovimentacaoFormat.TitleCase(SellerName);
    [JsonIgnore] public string CostTotalDisplay => MovimentacaoFormat.Money(CostTotal);
    [JsonIgnore] public string TotalDisplay => MovimentacaoFormat.Money(Total);
    [JsonIgnore] public string FeePercentDisplay => FeePercent.ToString("N2", CultureInfo.CurrentCulture);
    [JsonIgnore] public string TaxaValorDisplay => MovimentacaoFormat.Money(TaxaValor);
    [JsonIgnore] public string TotalLiquidoDisplay => MovimentacaoFormat.Money(TotalLiquido);
    [JsonIgnore] public string LucroLiquidoDisplay => MovimentacaoFormat.Money(LucroLiquido);
}

public sealed class MovimentacaoCompraRow
{
    public int PurchaseId { get; set; }
    public string EmissionDateBr { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string Document { get; set; } = "";
    public string Status { get; set; } = "";
    public int ItemsCount { get; set; }
    public double Total { get; set; }

    [JsonIgnore] public string PurchaseIdDisplay => PurchaseId.ToString(CultureInfo.InvariantCulture);
    [JsonIgnore] public string SupplierNameDisplay => MovimentacaoFormat.TitleCase(SupplierName);
    [JsonIgnore] public string TotalDisplay => MovimentacaoFormat.Money(Total);
}

public sealed class MovimentacaoCompraItemRow
{
    public int PurchaseId { get; set; }
    public string EmissionDateBr { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public double Qty { get; set; }
    public double UnitPrice { get; set; }
    public double Total { get; set; }

    [JsonIgnore] public string PurchaseIdDisplay => PurchaseId.ToString(CultureInfo.InvariantCulture);
    [JsonIgnore] public string SupplierNameDisplay => MovimentacaoFormat.TitleCase(SupplierName);
    [JsonIgnore] public string ProductNameDisplay => MovimentacaoFormat.TitleCase(ProductName);
    [JsonIgnore] public string QtyDisplay => MovimentacaoFormat.Qty(Qty);
    [JsonIgnore] public string UnitPriceDisplay => MovimentacaoFormat.Money(UnitPrice);
    [JsonIgnore] public string TotalDisplay => MovimentacaoFormat.Money(Total);
}

public sealed class MovimentacaoResult
{
    public string Tab { get; set; } = "produtos";
    public string Tipo { get; set; } = "vendas";
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public List<MovimentacaoProdutoRow> Produtos { get; set; } = [];
    public List<MovimentacaoVendaRow> Vendas { get; set; } = [];
    public List<MovimentacaoCompraRow> Compras { get; set; } = [];
    public List<MovimentacaoCompraItemRow> CompraItens { get; set; } = [];
    public int Registros { get; set; }
    public int TotalRegistros { get; set; }
    public bool Truncated { get; set; }
    public double TotalFaturamento { get; set; }
    public int TotalVendas { get; set; }
    public double TotalCompras { get; set; }
    public int TotalComprasCount { get; set; }
    public double TotalValor { get; set; }
    public double TotalTaxa { get; set; }
    public double TotalLiquido { get; set; }
    public double TotalLucroBruto { get; set; }
    public double TotalLucro { get; set; }
    public double TotalCusto { get; set; }
}

internal static class MovimentacaoFormat
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>R$ 1.234,56</summary>
    public static string Money(double value) =>
        $"R$ {ProductPriceHelper.FormatBr(value)}";

    /// <summary>Inteiro se não houver fração; senão até 3 casas (kg/litro).</summary>
    public static string Qty(double qty)
    {
        if (Math.Abs(qty - Math.Round(qty)) < 0.0005)
            return ((long)Math.Round(qty)).ToString(PtBr);
        return qty.ToString("0.###", PtBr);
    }

    /// <summary>Capitaliza palavras; preserva tokens curtos/com números (2L, 350ml).</summary>
    public static string TitleCase(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(TitleCaseToken(parts[i]));
        }
        return sb.ToString();
    }

    private static string TitleCaseToken(string token)
    {
        if (token.Length == 0)
            return token;

        // Já tem minúsculas misturadas — mantém (ex.: Coca-Cola cadastrado assim)
        if (token.Any(char.IsLower) && token.Any(char.IsUpper))
            return token;

        // Códigos / medidas: 2L, 350ML, CX, UN
        if (token.Any(char.IsDigit) || token.Length <= 3)
        {
            if (token.All(c => char.IsLetter(c) || char.IsDigit(c)))
            {
                // 2L / 350ml → mantém letras em maiúsculo só se forem unidade curta
                if (token.Any(char.IsDigit))
                    return token.ToUpperInvariant();
                return token.ToUpperInvariant(); // CX, UN, PET
            }
        }

        // Hífen: COCA-COLA → Coca-Cola
        if (token.Contains('-'))
        {
            var segs = token.Split('-');
            return string.Join("-", segs.Select(TitleCaseSimple));
        }

        return TitleCaseSimple(token);
    }

    private static string TitleCaseSimple(string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;
        if (word.Length == 1)
            return word.ToUpper(PtBr);
        return char.ToUpper(word[0], PtBr) + word[1..].ToLower(PtBr);
    }
}
