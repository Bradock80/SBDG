namespace SGDB.Models;

/// <summary>
/// Consistência cobertura (product_lots) × estoque físico canônico (products.stock).
/// Não corrige automaticamente OverTracked nem estoque negativo.
/// </summary>
public enum LotCoverageConsistencyStatus
{
    ProductNotFound = 0,
    NegativeStock,
    ZeroStock,
    OverTracked,
    UnderTracked,
    Consistent,
}

/// <summary>
/// Rastreabilidade derivada — sem coluna nova e sem lote artificial (SEMLOTE/0000/N/A).
/// Lote não identificado no banco atual = lot_number = ''.
/// </summary>
public enum LotCoverageTraceability
{
    Complete = 0,
    Partial,
    UninformedExpiry,
    Untracked,
}

public sealed class LotCoverageException : InvalidOperationException
{
    public string ErrorCode { get; }

    public LotCoverageException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

public sealed class LotCoverageLine
{
    public int Id { get; init; }
    public string LotNumber { get; init; } = "";
    public DateTime? ExpiryDate { get; init; }
    public double Quantity { get; init; }
    public double UnitCost { get; init; }
    public int? PurchaseId { get; init; }
    public LotCoverageTraceability Traceability { get; init; }
    public LotCostSource CostSource { get; init; }
    public double? UsedCost { get; init; }
    public bool IsExpired { get; init; }
}

public sealed class LotCoverageSnapshot
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public bool ProductActive { get; init; }
    public double Stock { get; init; }
    public double StockFridge { get; init; }
    public double TrackedQuantity { get; init; }
    public double UntrackedQuantity { get; init; }
    public double OverCoverage { get; init; }
    public double CostPrice { get; init; }
    public LotCoverageConsistencyStatus ConsistencyStatus { get; init; }
    public IReadOnlyList<LotCoverageLine> Lines { get; init; } = [];
}

public sealed class LotCoverageMutationResult
{
    public bool Ok { get; init; } = true;
    public int ProductId { get; init; }
    public int? ProductLotId { get; init; }
    public int? DestinationLotId { get; init; }
    public bool SensitiveExpiryCorrection { get; init; }
    public LotCoverageSnapshot Snapshot { get; init; } = new();
}

public sealed class LotCoverageAddInput
{
    public int ProductId { get; init; }
    public double Quantity { get; init; }
    public DateTime ExpiryDate { get; init; }
    public string? LotNumber { get; init; }
    public string? Reason { get; init; }
    public string? Origin { get; init; }
}

public sealed class LotCoverageEditInput
{
    public int ProductLotId { get; init; }
    public DateTime ExpiryDate { get; init; }
    public string? LotNumber { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class LotCoverageQuantityInput
{
    public int ProductLotId { get; init; }
    public double Quantity { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class LotCoverageSplitInput
{
    public int ProductLotId { get; init; }
    public double DestinationQuantity { get; init; }
    public DateTime DestinationExpiryDate { get; init; }
    public string? DestinationLotNumber { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class LotCoverageRemoveInput
{
    public int ProductLotId { get; init; }
    public string Reason { get; init; } = "";
}
