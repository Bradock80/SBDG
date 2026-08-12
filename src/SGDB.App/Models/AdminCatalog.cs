using SGDB.Utils;

namespace SGDB.Models;

public sealed class PaymentMethodRow
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public string ApiLabel { get; set; } = "";
    public string MovementType { get; set; } = "Entrada";
    public double FeePercent { get; set; }
    public double FeeFixed { get; set; }
    public int SettlementDays { get; set; }
    public int? BankAccountId { get; set; }
    public string BankAccountName { get; set; } = "";
    public bool Active { get; set; } = true;
    public string PdvKey { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool FeeEditable { get; set; } = true;
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; } = 100;
    /// <summary>caixa | receber | banco</summary>
    public string DestinationKind { get; set; } = "banco";

    public string FeeDisplay => FeeFixed > 0.009
        ? $"{FeePercent:N2}% + R$ {FeeFixed:N2}"
        : $"{FeePercent:N2}%";
    public string FeeFixedDisplay => FeeFixed > 0.009 ? $"R$ {FeeFixed:N2}" : "—";
    public string SettlementDisplay => SettlementDays.ToString();
    public string DestinationDisplay => DestinationKind switch
    {
        "caixa" => "Caixa físico (Gaveta)",
        "receber" => "Contas a receber",
        _ when BankAccountId is > 0 && !string.IsNullOrWhiteSpace(BankAccountName)
            => BankAccountName,
        _ when BankAccountId is > 0 => $"Conta #{BankAccountId}",
        _ => "— não vinculado",
    };
    public string StatusDisplay => Active ? "Ativo" : "Inativo";
    public string PdvKeyDisplay => string.IsNullOrWhiteSpace(PdvKey) ? "—" : PdvKey.ToUpperInvariant();
    public bool CanDelete => !IsSystem;
    public bool DestinationLocked => IsSystem && Id is "dinheiro" or "fiado";
}

public sealed class PaymentMethodInput
{
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public string ApiLabel { get; set; } = "";
    public string MovementType { get; set; } = "Entrada";
    public double FeePercent { get; set; }
    public double FeeFixed { get; set; }
    public int SettlementDays { get; set; }
    public int? BankAccountId { get; set; }
    public bool Active { get; set; } = true;
    public string PdvKey { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool FeeEditable { get; set; } = true;
    public string DestinationKind { get; set; } = "banco";
    public int? SortOrder { get; set; }
}

public sealed class Seller
{
    public int Id { get; init; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Cpf { get; set; }
    public double CommissionPercent { get; set; }
    public string? Notes { get; set; }
    public bool Active { get; set; } = true;
    public string CreatedAt { get; init; } = "";

    public string CommissionDisplay => $"{CommissionPercent:N2}%";
    public string ActiveDisplay => Active ? "Sim" : "Não";
    public string DisplayName => Id <= 0 ? Name : $"{Code} — {Name}";
}

public sealed class ContainerType
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public double SalePrice { get; set; }
    public double Stock { get; set; }
    public bool Active { get; set; } = true;
    public string? Notes { get; set; }
    public string CreatedAt { get; init; } = "";

    public string SalePriceDisplay => ProductPriceHelper.MoneyBr(SalePrice);
    public string StockDisplay => Stock.ToString("N0");
    public string ActiveDisplay => Active ? "Sim" : "Não";
    public string DisplayName => Name;
}

/// <summary>Categoria financeira (Contas a Pagar / despesas do caixa).</summary>
public sealed class ExpenseCategory
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public int SortOrder { get; set; } = 100;
    public bool IsSystem { get; set; }
    public string CreatedAt { get; init; } = "";

    public string ActiveDisplay => Active ? "Sim" : "Não";
    public string DisplayName => Name;
}

public sealed class PriceTable
{
    public int Id { get; init; }
    public string Description { get; set; } = "";
    public double SurchargePercent { get; set; }
    public double SurchargeFixed { get; set; }
    public List<string> ApplyPaymentMethods { get; set; } = [];
    public bool Active { get; set; } = true;
    public string CreatedAt { get; init; } = "";
    public int ProductCount { get; set; }

    public string DisplayName => Description;

    public string PercentDisplay => $"{SurchargePercent:N2}%";
    public string FixedDisplay => SurchargeFixed > 0.009 ? $"R$ {SurchargeFixed:N2}" : "—";
    public string ActiveDisplay => Active ? "Sim" : "Não";
    public string MethodsDisplay => ApplyPaymentMethods.Count == 0
        ? "—"
        : string.Join(", ", ApplyPaymentMethods);
}
