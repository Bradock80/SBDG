namespace SGDB.Models;

using SGDB.Domain.Products;
using SGDB.Utils;

public sealed class InventorySession
{
    public int Id { get; init; }
    public string Status { get; init; } = "aberta";
    public string? GroupName { get; init; }
    public string? Notes { get; init; }
    public string CreatedAt { get; init; } = "";
    public string? ClosedAt { get; init; }

    public string StatusDisplay => Status switch
    {
        "consolidada" => "Consolidada",
        "cancelada" => "Cancelada",
        _ => "Aberta",
    };
    public string GroupDisplay => string.IsNullOrWhiteSpace(GroupName) ? "Todos os produtos" : GroupName;
}

public sealed class InventoryItem
{
    public int Id { get; init; }
    public int SessionId { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductBarcode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Unit { get; init; } = "UN";
    public double TheoreticalQty { get; init; }
    public double? CountedQty { get; init; }
    public string? Notes { get; init; }

    public double? Difference => CountedQty is double c ? Math.Round(c - TheoreticalQty, 3) : null;
    public bool IsCounted => CountedQty is not null;
    public bool HasDivergence => Difference is double d && Math.Abs(d) > 0.0009;
    /// <summary>none | zero | neg | pos — para cor da coluna Dif.</summary>
    public string DiffKind => Difference switch
    {
        null => "none",
        0 => "zero",
        < 0 => "neg",
        _ => "pos",
    };

    public string TheoreticalDisplay => TheoreticalQty.ToString("N3");
    public string CountedDisplay => CountedQty?.ToString("N3") ?? "—";
    public string DifferenceDisplay => Difference is double d ? d.ToString("N3") : "—";
}

public sealed class InventoryDivergenceRow
{
    public int ItemId { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Unit { get; init; } = "UN";
    public double TheoreticalQty { get; init; }
    public double CountedQty { get; init; }
    public double Difference { get; init; }
    public double Cost { get; init; }
    public double ImpactValue => ProductPriceCalculator.RoundPrice(Difference * Cost);

    public string TheoreticalDisplay => TheoreticalQty.ToString("N3");
    public string CountedDisplay => CountedQty.ToString("N3");
    public string DifferenceDisplay => Difference.ToString("N3");
    public string ImpactDisplay => ProductPriceHelper.MoneyBr(ImpactValue);
}

public sealed class InventoryConsolidateResult
{
    public int SessionId { get; init; }
    public int AdjustedCount { get; init; }
    public double TotalPositiveQty { get; init; }
    public double TotalNegativeQty { get; init; }
}

/// <summary>Produto contado cujo estoque mudou após a abertura da sessão de inventário.</summary>
public sealed class InventoryConcurrencyConflict
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public double TheoreticalQty { get; init; }
    public double CurrentStock { get; init; }
    public bool HasMovementSinceOpen { get; init; }
}

/// <summary>
/// Consolidação bloqueada: estoque ou movement mudou durante a contagem.
/// A sessão permanece aberta; nenhum ajuste é aplicado.
/// </summary>
public sealed class InventoryConcurrencyException : InvalidOperationException
{
    public const double StockTolerance = 0.0009;

    public IReadOnlyList<InventoryConcurrencyConflict> Conflicts { get; }

    public InventoryConcurrencyException(IReadOnlyList<InventoryConcurrencyConflict> conflicts)
        : base(BuildMessage(conflicts))
    {
        Conflicts = conflicts;
    }

    public static string BuildMessage(IReadOnlyList<InventoryConcurrencyConflict> conflicts)
    {
        var lines = new List<string>
        {
            "Não foi possível consolidar o inventário.",
            "",
            "Houve movimentação de estoque durante a contagem.",
            "",
            "Produtos afetados:",
        };
        foreach (var c in conflicts)
        {
            var label = string.IsNullOrWhiteSpace(c.ProductCode)
                ? c.ProductName
                : $"{c.ProductCode} — {c.ProductName}";
            lines.Add($"- {label}");
        }
        lines.Add("");
        lines.Add("Para evitar sobrescrever vendas/compras, cancele este inventário e abra uma nova contagem.");
        return string.Join(Environment.NewLine, lines);
    }
}
