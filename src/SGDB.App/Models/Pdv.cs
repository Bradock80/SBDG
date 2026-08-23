namespace SGDB.Models;

using SGDB.Utils;

/// <summary>Resultado do bipe no PDV: avulso (1 un) ou maço/CX.</summary>
public sealed class PdvScanResult
{
    public required Product Product { get; init; }
    /// <summary>Qtd na tela do PDV (maço = 1).</summary>
    public double Quantity { get; init; } = 1;
    public double UnitPrice { get; init; }
    public bool IsPackSale { get; init; }
    public string? ModeLabel { get; init; }
    /// <summary>Cigarros/unidades que saem do estoque por 1 na qtd (maço → 20).</summary>
    public double StockUnitsPerSale { get; init; } = 1;
}

public class PdvCartLine
{
    public int LineNum { get; set; }
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "UN";
    public double Quantity { get; set; }
    public double UnitPrice { get; set; }
    /// <summary>Multiplicador de estoque (1 maço cigarro = 20).</summary>
    public double StockUnitsPerSale { get; set; } = 1;
    public double StockQuantity =>
        StockUnitsPerSale > 1.0001
            ? Math.Round(Quantity * StockUnitsPerSale, 4)
            : Quantity;
    public double Subtotal => Math.Round(Quantity * UnitPrice, 2, MidpointRounding.AwayFromZero);

    public string QuantityDisplay => Quantity.ToString("N3");
    public string UnitPriceDisplay => ProductPriceHelper.MoneyBr(UnitPrice);
    public string SubtotalDisplay => ProductPriceHelper.MoneyBr(Subtotal);
}

public class PdvPaymentPart
{
    public string PaymentType { get; set; } = "";
    public double Amount { get; set; }
}

public class PdvFinalizeRequest
{
    public required List<PdvCartLine> Items { get; init; }
    public string PaymentType { get; init; } = "Dinheiro";
    public IReadOnlyList<PdvPaymentPart>? Payments { get; init; }
    public double Discount { get; init; }
    public double Surcharge { get; init; }
    public double CashReceived { get; init; }
    public int? CustomerPersonId { get; init; }
    public int? SellerId { get; init; }
}

public class PdvFinalizeResult
{
    public int SaleId { get; init; }
    public double Total { get; init; }
    public double ChangeAmount { get; init; }
    public double CashReceived { get; init; }
}

public class PdvSaleListRow
{
    public int Id { get; set; }
    public string SessionDate { get; set; } = "";
    public double Total { get; set; }
    public string PaymentType { get; set; } = "";
    public string? CustomerName { get; set; }
    public string? SellerName { get; set; }
    public bool Cancelled { get; set; }
    public string CreatedAtBr { get; set; } = "";
    public int ItemsCount { get; set; }
    public string PaymentLabel { get; set; } = "";
    public string? PixIntentStatus { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string CustomerDisplay => string.IsNullOrWhiteSpace(CustomerName) ? "—" : CustomerName!;
    [System.Text.Json.Serialization.JsonIgnore] public string SellerDisplay => string.IsNullOrWhiteSpace(SellerName) ? "—" : SellerName!;
    [System.Text.Json.Serialization.JsonIgnore]
    public string FormaDisplay =>
        string.Equals(PixIntentStatus, "refund_pending", StringComparison.OrdinalIgnoreCase)
            ? "PIX — estorno pendente"
            : string.IsNullOrWhiteSpace(PaymentType) ? "—" : PaymentType;
    [System.Text.Json.Serialization.JsonIgnore] public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusDisplay =>
        string.Equals(PixIntentStatus, "refund_pending", StringComparison.OrdinalIgnoreCase)
            ? "PIX pend."
            : Cancelled ? "Canc." : "OK";
    [System.Text.Json.Serialization.JsonIgnore] public string StatusKey => Cancelled ? "cancelled" : "ok";
    [System.Text.Json.Serialization.JsonIgnore]
    public string SessionDateBr
    {
        get
        {
            if (DateTime.TryParse(SessionDate, out var d))
                return d.ToString("dd/MM/yyyy");
            return SessionDate;
        }
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public string TimeDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CreatedAtBr))
                return "—";
            var parts = CreatedAtBr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[^1] : CreatedAtBr;
        }
    }
}

public class PdvSaleItemRow
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "UN";
    public double Quantity { get; set; }
    public double UnitPrice { get; set; }
    public double Subtotal { get; set; }
    public double? CostAtSale { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string QuantityDisplay => Quantity.ToString("N3");
    [System.Text.Json.Serialization.JsonIgnore] public string UnitPriceDisplay => ProductPriceHelper.MoneyBr(UnitPrice);
    [System.Text.Json.Serialization.JsonIgnore] public string SubtotalDisplay => ProductPriceHelper.MoneyBr(Subtotal);
}

public class PdvSaleDetail
{
    public int Id { get; set; }
    public string SessionDate { get; set; } = "";
    public double Total { get; set; }
    public string PaymentType { get; set; } = "";
    public string PaymentLabel { get; set; } = "";
    public string? CustomerName { get; set; }
    public string? SellerName { get; set; }
    public bool Cancelled { get; set; }
    public string CreatedAtBr { get; set; } = "";
    public double? CashReceived { get; set; }
    public double? ChangeAmount { get; set; }
    public int? CustomerPersonId { get; set; }
    public List<PdvPaymentPart> Payments { get; set; } = [];
    public List<PdvSaleItemRow> Items { get; set; } = [];
}

public class PdvSwapItemResult
{
    public PdvSaleDetail Sale { get; init; } = null!;
    public double? RefundHint { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Impacto do SwapSaleItem calculado sem gravar.
/// Política híbrida: fiado puro auto-ajusta; demais formas exigem confirmação se o total mudar.
/// </summary>
public class PdvSwapItemPreview
{
    public int SaleId { get; init; }
    public double OldTotal { get; init; }
    public double OldGross { get; init; }
    public double OriginalAdjustment { get; init; }
    public double NewGross { get; init; }
    public double NewTotal { get; init; }
    public double Difference { get; init; }
    public string PaymentType { get; init; } = "";
    public IReadOnlyList<PdvPaymentPart> CurrentPayments { get; init; } = [];
    public int? CustomerPersonId { get; init; }
    public bool IsPureFiado { get; init; }
    /// <summary>True quando o total muda e o pagamento não é fiado puro — operador deve confirmar.</summary>
    public bool RequiresPaymentConfirmation { get; init; }
    /// <summary>Informativo (não-fiado) quando newTotal &lt; oldTotal.</summary>
    public double? RefundHint { get; init; }
}

public class PdvResumoGrupoRow
{
    public string GroupName { get; set; } = "";
    public double Total { get; set; }
    public double Lucro { get; set; }
    public double Qty { get; set; }
    public double MargemPercent { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
    [System.Text.Json.Serialization.JsonIgnore] public string LucroDisplay => ProductPriceHelper.MoneyBr(Lucro);
    [System.Text.Json.Serialization.JsonIgnore] public string QtyDisplay => PdvQtyFormat.Short(Qty);
    [System.Text.Json.Serialization.JsonIgnore] public string MargemDisplay => $"{MargemPercent:N2}%";
}

/// <summary>Quantidade sem zeros à direita: 17 em vez de 17,000; 1,5 em vez de 1,500.</summary>
public static class PdvQtyFormat
{
    public static string Short(double qty)
    {
        if (Math.Abs(qty - Math.Round(qty)) < 0.0005)
            return Math.Round(qty).ToString("N0");
        return qty.ToString("0.###");
    }
}

public class PdvResumoFormaRow
{
    public string Forma { get; set; } = "";
    public double Total { get; set; }
    public int Count { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
}

public class PdvResumoTopRow
{
    public string ProductName { get; set; } = "";
    public double Qty { get; set; }
    public double Total { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string QtyDisplay => PdvQtyFormat.Short(Qty);
    [System.Text.Json.Serialization.JsonIgnore] public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
}

public class PdvResumoDia
{
    public string SessionDate { get; set; } = "";
    public string CaixaInfo { get; set; } = "";
    public bool CaixaOpen { get; set; }
    public string CaixaAbertoDesde { get; set; } = "";
    public double EntradaCaixa { get; set; }
    public double EntradasCaixa { get; set; }
    public double SaidasCaixa { get; set; }
    public double SaldoGaveta { get; set; }
    public double Faturamento { get; set; }
    public double LucroReal { get; set; }
    public double MargemPercent { get; set; }
    public bool HasEstimatedLegacyCost { get; set; }
    public bool CmvUsesHistoricalSnapshot { get; set; }
    public bool ProfitIsEstimated { get; set; }
    public bool MarginIsEstimated { get; set; }
    public string? CmvReliabilityNote { get; set; }
    public int QtdVendas { get; set; }
    public double TicketMedio { get; set; }
    public int QtdCancelados { get; set; }
    public double FiadoTotal { get; set; }
    public int FiadoCount { get; set; }
    public List<PdvResumoGrupoRow> Grupos { get; set; } = [];
    public List<PdvResumoFormaRow> Formas { get; set; } = [];
    public List<PdvResumoTopRow> TopProdutos { get; set; } = [];
    public string EntradaCaixaDisplay => ProductPriceHelper.MoneyBr(EntradaCaixa);
    public string EntradasCaixaDisplay => ProductPriceHelper.MoneyBr(EntradasCaixa);
    public string SaidasCaixaDisplay => ProductPriceHelper.MoneyBr(SaidasCaixa);
    public string SaldoGavetaDisplay => ProductPriceHelper.MoneyBr(SaldoGaveta);
    public string FaturamentoDisplay => ProductPriceHelper.MoneyBr(Faturamento);
    public string LucroRealDisplay => ProductPriceHelper.MoneyBr(LucroReal);
    public string MargemDisplay => MargemPercent.ToString("N2");
    public string TicketMedioDisplay => ProductPriceHelper.MoneyBr(TicketMedio);
    public string FiadoTotalDisplay => ProductPriceHelper.MoneyBr(FiadoTotal);
}
