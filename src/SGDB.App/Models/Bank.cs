using SGDB.Utils;

namespace SGDB.Models;

public sealed class BankAccountRow
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string BankName { get; init; } = "";
    public string Agency { get; init; } = "";
    public string AccountNumber { get; init; } = "";
    public string AccountType { get; init; } = "corrente";
    public string PixKey { get; init; } = "";
    public string DefaultOperator { get; init; } = "";
    public double OpeningBalance { get; init; }
    public bool Active { get; init; } = true;
    public string Notes { get; init; } = "";
    public double Balance { get; init; }

    public string BalanceDisplay => ProductPriceHelper.MoneyBr(Balance);
    public string TypeDisplay => AccountType switch
    {
        "poupanca" => "Poupança",
        "aplicacao" => "Aplicação",
        _ => "Corrente",
    };
    public string Label => Active ? Name : $"{Name} (inativa)";
}

public sealed class BankMovementRow
{
    public int Id { get; init; }
    public int AccountId { get; init; }
    public string MovementDate { get; init; } = "";
    public string? PostedDate { get; init; }
    public string Kind { get; init; } = "";
    public string Description { get; init; } = "";
    public string PartyName { get; init; } = "";
    public string PaymentType { get; init; } = "";
    public string OperatorName { get; init; } = "";
    public string? ExternalId { get; init; }
    public double AmountIn { get; init; }
    public double AmountOut { get; init; }
    public double FeeAmount { get; init; }
    public string ReconciliationStatus { get; init; } = "pendente";
    public string? Notes { get; init; }
    public string? RefType { get; init; }
    public int? RefId { get; init; }

    public double NetAmount => ProductPriceHelper.RoundPrice(AmountIn - AmountOut);
    public string DateDisplay => FormatDate(MovementDate);
    public string PostedDisplay => string.IsNullOrEmpty(PostedDate) ? "—" : FormatDate(PostedDate);
    public string InDisplay => AmountIn > 0.009 ? ProductPriceHelper.MoneyBr(AmountIn) : "";
    public string OutDisplay => AmountOut > 0.009 ? ProductPriceHelper.MoneyBr(AmountOut) : "";
    public string FeeDisplay => FeeAmount > 0.009 ? ProductPriceHelper.MoneyBr(FeeAmount) : "—";
    public string OperatorDisplay => string.IsNullOrWhiteSpace(OperatorName) ? "—" : OperatorName;
    public string StatusDisplay => ReconciliationStatus switch
    {
        "conferido" => "Conferido",
        "divergente" => "Divergente",
        _ => "Pendente",
    };
    public string KindDisplay => Kind switch
    {
        "credito" => "Crédito",
        "debito" => "Débito",
        "tarifa" => "Tarifa",
        "transferencia" => "Transferência",
        "ajuste" => "Ajuste",
        "prevista" => "Prevista",
        _ => Kind,
    };

    private static string FormatDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (DateTime.TryParse(iso, out var dt))
            return dt.ToString("dd/MM/yyyy");
        return iso;
    }
}

public sealed class BankMovementsResult
{
    public IReadOnlyList<BankMovementRow> Rows { get; init; } = [];
    public double TotalIn { get; init; }
    public double TotalOut { get; init; }
    public double TotalFees { get; init; }
    public double PeriodBalance { get; init; }
    public int Pendentes { get; init; }
    public int Conferidos { get; init; }
}

public sealed class OfxImportResult
{
    public int Matched { get; init; }
    public int Created { get; init; }
    public int Skipped { get; init; }
    public int TotalInFile { get; init; }
}
