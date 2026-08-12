namespace SGDB.Models;

using SGDB.Utils;

public enum CashSessionStatus
{
    Aberta,
    Fechada,
}

public enum CashMovementKind
{
    Abertura,
    Fechamento,
    Venda,
    VendaFiado,
    RecebimentoFiado,
    Compra,
    Sangria,
    Suprimento,
    Troca,
}

public class CashMovementRow
{
    public int Id { get; set; }
    public string DateTimeDisplay { get; set; } = "";
    public string Historico { get; set; } = "";
    public string EntradaDisplay { get; set; } = "";
    public string SaidaDisplay { get; set; } = "";
    public string FormaPagto { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Deletable { get; set; }
    public string RefType { get; set; } = "";
    public int RefId { get; set; }
}

public class CashOperacaoView
{
    public bool IsOperational { get; set; }
    public bool IsClosed { get; set; }
    public bool NeedsOpening { get; set; }
    public bool CarriedOver { get; set; }
    public int? SessionId { get; set; }
    public string SessionDateBr { get; set; } = "";
    public string StatusMessage { get; set; } = "";
    public double SaldoInicial { get; set; }
    public double EntradasCaixa { get; set; }
    public double SaidasCaixa { get; set; }
    public double SaldoFinal { get; set; }
    public double SaldoFinalGaveta { get; set; }
    public double VendasDiaPdv { get; set; }
    public string OpeningObs { get; set; } = "";
    public string OpenedAtBr { get; set; } = "";
    public string OpenedTimeBr { get; set; } = "";
    public string ClosedAtBr { get; set; } = "";
    public string AvisoCicloAnterior { get; set; } = "";
    public Dictionary<string, double> EntradasPorForma { get; set; } = new();
    public List<CashMovementRow> Rows { get; set; } = new();
}

/// <summary>Valor aguardando depósito (conferência simples: dia + valor).</summary>
public class CashDepositRow
{
    public int Id { get; init; }
    public string DepositDate { get; init; } = "";
    public double Amount { get; init; }
    public string Status { get; init; } = "pendente";
    public double? ConfirmedAmount { get; init; }
    public string? ConfirmedAt { get; init; }
    public string? Notes { get; init; }

    public string DepositDateBr =>
        DateTime.TryParse(DepositDate, out var d) ? d.ToString("dd/MM/yyyy") : DepositDate;

    public string AmountDisplay => ProductPriceHelper.MoneyBr(Amount);
    public string ConfirmedDisplay => ConfirmedAmount is double v
        ? ProductPriceHelper.MoneyBr(v)
        : "—";

    public string StatusLabel => Status switch
    {
        "depositado" => "Depositado",
        "divergente" => "Divergente",
        _ => "Aguardando",
    };

    public string ConfirmedAtBr
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ConfirmedAt))
                return "—";
            if (DateTime.TryParse(ConfirmedAt, out var dt))
                return dt.ToString("dd/MM/yyyy HH:mm");
            return ConfirmedAt;
        }
    }
}

