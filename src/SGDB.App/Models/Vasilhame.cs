using SGDB.Utils;

namespace SGDB.Models;

public sealed class VasilhameSaldoRow
{
    public int? CustomerId { get; init; }
    public string BorrowerName { get; init; } = "";
    public string BorrowerPhone { get; init; } = "";
    public int ContainerTypeId { get; init; }
    public string ContainerTypeName { get; init; } = "";
    public double Balance { get; init; }
    public double TotalLoaned { get; init; }
    public double TotalReturned { get; init; }
    public double UnitCautionPrice { get; init; }
    public string? DueDate { get; init; }
    public string? FirstLoanDate { get; init; }
    public bool IsOverdue { get; init; }

    public bool IsPartialReturn =>
        Balance > 0.009 && TotalReturned > 0.009 && TotalLoaned > Balance + 0.009;

    public string StatusKind =>
        IsOverdue ? "vencido"
        : IsPartialReturn ? "parcial"
        : Balance > 0.009 ? "em_dia"
        : "zerado";

    public string StatusBadge => StatusKind switch
    {
        "vencido" => "⚠️ Vencido",
        "parcial" => "Parcial",
        "em_dia" => "Em dia",
        _ => "—",
    };

    public string BalanceDisplay => ProductPriceHelper.RoundPrice(Balance).ToString("0.###");
    public string DueDateDisplay => string.IsNullOrEmpty(DueDate) ? "—"
        : DateTime.TryParse(DueDate, out var d) ? d.ToString("dd/MM/yyyy") : DueDate;
    public string FirstLoanDisplay => string.IsNullOrEmpty(FirstLoanDate) ? "—"
        : DateTime.TryParse(FirstLoanDate, out var d) ? d.ToString("dd/MM/yyyy") : FirstLoanDate;
    public string CautionDisplay =>
        UnitCautionPrice <= 0.009 ? "—"
        : ProductPriceHelper.MoneyBr(UnitCautionPrice * Balance);
    public double CautionTotal => ProductPriceHelper.RoundPrice(UnitCautionPrice * Math.Max(0, Balance));
    public bool HasPhone => DigitsOnly(BorrowerPhone).Length >= 10;
    public string Key => $"{CustomerId ?? 0}|{BorrowerName}|{ContainerTypeId}";

    public static string DigitsOnly(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        return new string(phone.Where(char.IsDigit).ToArray());
    }
}

public sealed class VasilhameMovementRow
{
    public int Id { get; init; }
    public int? CustomerId { get; init; }
    public string BorrowerName { get; init; } = "";
    public string BorrowerPhone { get; init; } = "";
    public int ContainerTypeId { get; init; }
    public string ContainerTypeName { get; init; } = "";
    public string Kind { get; init; } = "";
    public double Quantity { get; init; }
    public double UnitPrice { get; init; }
    public string? DueDate { get; init; }
    public string? Notes { get; init; }
    public string CreatedAt { get; init; } = "";

    public bool IsLoan => Kind is "saida" or "emprestimo";
    public string KindDisplay => Kind switch
    {
        "saida" or "emprestimo" => "Saída / Empréstimo",
        "entrada" or "devolucao" => "Entrada / Devolução",
        _ => Kind,
    };
    public string QtyDisplay => ProductPriceHelper.RoundPrice(Quantity).ToString("0.###");
    public string DueDateDisplay => string.IsNullOrEmpty(DueDate) ? "—"
        : DateTime.TryParse(DueDate, out var d) ? d.ToString("dd/MM/yyyy") : DueDate;
    public string CreatedDisplay =>
        DateTime.TryParse(CreatedAt, out var dt) ? dt.ToString("dd/MM/yyyy HH:mm") : CreatedAt;
}

public sealed class VasilhameTypeSummary
{
    public string TypeName { get; init; } = "";
    public double Quantity { get; init; }
    public string Display => $"{Quantity:0.###} {TypeName.ToUpperInvariant()}";
}

public sealed class VasilhameListResult
{
    public IReadOnlyList<VasilhameSaldoRow> Saldos { get; init; } = [];
    public IReadOnlyList<VasilhameMovementRow> Movimentos { get; init; } = [];
    public IReadOnlyList<VasilhameTypeSummary> ResumoPorTipo { get; init; } = [];
    public int Registros { get; init; }
    public double TotalItens { get; init; }
    public int Vencidos { get; init; }
    public double TotalCaucao { get; init; }
}
