using SGDB.Utils;

namespace SGDB.Models;

public sealed class FiadoContaRow
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string Phone { get; init; } = "";
    public double TotalCharges { get; init; }
    public double TotalPaid { get; init; }
    public double TotalInterest { get; init; }
    public double Balance { get; init; }
    public int SalesCount { get; init; }
    public string LastSaleBr { get; init; } = "";
    public bool Orphan { get; init; }
    /// <summary>Chave para agrupar/vincular vendas órfãs pelo nome no cupom (quando customer_id está vazio).</summary>
    public string? OrphanPartyKey { get; init; }

    public string TotalChargesDisplay => ProductPriceHelper.MoneyBr(TotalCharges);
    public string TotalPaidDisplay => ProductPriceHelper.MoneyBr(TotalPaid);
    public string TotalInterestDisplay => ProductPriceHelper.MoneyBr(TotalInterest);
    public string BalanceDisplay => ProductPriceHelper.MoneyBr(Balance);
    public bool HasDebt => Balance > 0.005;

    /// <summary>Resumo simples para tooltip / rodapé: Vendido − Recebido = Deve.</summary>
    public string SummaryTooltip =>
        $"Vendido {TotalChargesDisplay} − Recebido {TotalPaidDisplay} = Deve {BalanceDisplay}";
}

public sealed class FiadoListResult
{
    public IReadOnlyList<FiadoContaRow> Rows { get; init; } = [];
    public int Registros { get; init; }
    public double TotalSaldo { get; init; }
    public double TotalJuros { get; init; }
}

public sealed class FiadoCustomerDetail
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string Phone { get; init; } = "";
    public double TotalCharges { get; init; }
    public double TotalPaid { get; init; }
    public double Balance { get; init; }
    public IReadOnlyList<FiadoSaleRow> Sales { get; init; } = [];
    public IReadOnlyList<FiadoPaymentRow> Payments { get; init; } = [];
}

public sealed class FiadoSaleRow
{
    public int Id { get; init; }
    public string DateBr { get; init; } = "";
    public string SessionDateBr { get; init; } = "";
    public double Total { get; init; }
    public IReadOnlyList<FiadoSaleItemRow> Items { get; init; } = [];

    public string Header => $"Venda #{Id} · {DateBr} · Fiado {ProductPriceHelper.MoneyBr(Total)}";
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
}

public sealed class FiadoSaleItemRow
{
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public double Quantity { get; init; }
    public double UnitPrice { get; init; }
    public double Subtotal { get; init; }

    public string QuantityDisplay => Quantity.ToString("N3");
    public string UnitPriceDisplay => ProductPriceHelper.MoneyBr(UnitPrice);
    public string SubtotalDisplay => ProductPriceHelper.MoneyBr(Subtotal);
}

public sealed class FiadoPaymentRow
{
    public int Id { get; init; }
    public string DateBr { get; init; } = "";
    public double Amount { get; init; }
    public double InterestAmount { get; init; }
    public double PrincipalAmount { get; init; }
    public string PaymentType { get; init; } = "";
    public bool Reversed { get; init; }
    public string Notes { get; init; } = "";

    public string AmountDisplay => ProductPriceHelper.MoneyBr(Amount);
    public string InterestDisplay => ProductPriceHelper.MoneyBr(InterestAmount);
    public string PrincipalDisplay => ProductPriceHelper.MoneyBr(PrincipalAmount);
    public string StatusDisplay => Reversed ? "Estornado" : "Ativo";
}

public sealed class FiadoReceberInput
{
    public double PrincipalAmount { get; init; }
    public double InterestAmount { get; init; }
    public double Amount { get; init; }
    public required string PaymentDate { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<FiadoReceberPart> Payments { get; init; } = [];
    /// <summary>Valor em espécie que o cliente entregou (pode ser maior que a parte em dinheiro — gera troco).</summary>
    public double CashReceived { get; init; }
}

public sealed class FiadoReceberPart
{
    public required string PaymentType { get; init; }
    public double Amount { get; init; }
}

public static class FiadoReceberFormas
{
    public static readonly string[] All = ["Dinheiro", "Pix", "Cartão Débito", "Cartão Crédito"];
}
