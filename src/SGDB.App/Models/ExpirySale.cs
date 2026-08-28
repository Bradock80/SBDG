namespace SGDB.Models;

/// <summary>
/// 70I — decisão calculada (não persistida) sobre quantidade vendável no depósito
/// frente a cobertura de validade em product_lots.
/// </summary>
public sealed class ExpirySaleDecision
{
    public int ProductId { get; init; }
    public double WarehouseStock { get; init; }
    public double FridgeStock { get; init; }

    public double TrackedQty { get; init; }
    public double ExpiredQty { get; init; }
    public double ValidQty { get; init; }
    public double UninformedQty { get; init; }
    public double UntrackedQty { get; init; }

    public double RequestedWarehouseQty { get; init; }
    /// <summary>
    /// Capacidade física conhecida não vencida: MIN(MAX(stock,0), valid+uninformed+untracked).
    /// Não é o critério de bloqueio quando requested &gt; stock.
    /// </summary>
    public double SellableWarehouseQty { get; init; }
    /// <summary>
    /// Quando <see cref="IsBlocked"/>, excesso da PARTE FÍSICA sobre a capacidade
    /// física não vencida. Não inclui quantidade que só iria a saldo negativo.
    /// </summary>
    public double BlockedQty { get; init; }

    public bool HasExpiredStock { get; init; }
    public bool HasUntrackedStock { get; init; }
    public bool HasUninformedExpiry { get; init; }

    /// <summary>
    /// True somente quando a PARTE FÍSICA da saída de depósito ultrapassa o que
    /// pode ser explicado como não vencido. requested &gt; stock, zero/negativo
    /// e overtracked com cobertura não vencida ≥ stock NÃO geram bloqueio 70I.
    /// </summary>
    public bool IsBlocked { get; init; }

    public string Reason { get; init; } = "";
    public string ErrorCode { get; init; } = "";
}

/// <summary>70I — violação da barreira de validade na saída de depósito.</summary>
public sealed class ExpirySaleException : InvalidOperationException
{
    public string ErrorCode { get; }
    public ExpirySaleDecision Decision { get; }

    public ExpirySaleException(string errorCode, string message, ExpirySaleDecision decision)
        : base(message)
    {
        ErrorCode = errorCode;
        Decision = decision;
    }
}
