namespace SGDB.Models;

/// <summary>
/// Situação objetiva do giro físico (70C-B1). Não inclui Loss/AtRisk/BuyNow/Stuck.
/// Silêncio 30/60/90 é filtro derivado (HistoryDays + DaysWithoutSale), não valor deste enum.
/// Não inclui "baixo giro": isso exigiria calibração de demanda independente da cobertura.
/// </summary>
public enum InventoryTurnoverSituation
{
    Normal = 0,
    LowCoverage,
    NoTurnover,
    ZeroStock,
    NegativeStock,
    InsufficientHistory,
    NeverSold,
}

/// <summary>
/// Estado da cobertura comercial. CoverageDays só é preenchido quando Calculable.
/// </summary>
public enum InventoryCoverageState
{
    Calculable = 0,
    ZeroStock,
    NegativeStock,
    NoTurnover,
}

/// <summary>
/// Faixa derivada de cobertura (70C-B2). Independente de giro.
/// CoverageDays &gt; 15 = Normal: não significa baixo giro, excesso, sobra nem promoção.
/// </summary>
public enum InventoryCoverageBand
{
    NotCalculable = 0,
    Negative,
    Zero,
    Critical,
    Low,
    Attention,
    Normal,
}

/// <summary>
/// Linha analítica de giro físico por SKU atual (já remapado / KEEP).
/// VMV operacional = MAX(0, vendas brutas − devoluções) / denominador civil.
/// Campos 70C-B2 (CoverageBand, silêncio, Parado, anomalia local) são derivados em memória.
/// Não há classificação automática de "baixo giro".
/// </summary>
public sealed class ProductTurnoverRow
{
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";

    public double Stock { get; init; }
    public double StockFridge { get; init; }
    public double TotalStock { get; init; }

    public double Vmv7 { get; init; }
    public double Vmv30 { get; init; }
    public double Vmv90 { get; init; }

    /// <summary>Null quando não for matematicamente calculável (nunca Infinity/NaN).</summary>
    public double? CoverageDays { get; init; }
    public InventoryCoverageState CoverageState { get; init; }

    /// <summary>Faixa derivada. Zero ≠ insuficiente; Normal ≠ baixo giro.</summary>
    public InventoryCoverageBand CoverageBand { get; init; }

    /// <summary>
    /// Estoque insuficiente: TotalStock &gt; ε, cobertura calculável e ≤ 3 dias.
    /// Não é estoque zerado. Não é sugestão de compra.
    /// </summary>
    public bool IsInsufficientStock { get; init; }

    /// <summary>
    /// Estoque total ≈ 0 e VMV30 &gt; ε. Texto futuro: "Sem estoque — há giro recente".
    /// Não afirma perda de venda, ruptura comprovada nem prejuízo.
    /// </summary>
    public bool IsZeroStockWithTurnover { get; init; }

    /// <summary>
    /// Evidência física e nenhuma saída válida observada neste SKU (não kit).
    /// Não inventa LastValidSaleDate. Não se aplica a composição.
    /// </summary>
    public bool HasUnobservedSale { get; init; }

    /// <summary>
    /// Parado: evidência física, HistoryDays ≥ 90, estoque &gt; ε,
    /// e sem venda observada ou ≥ 90 dias sem venda.
    /// SKU de kit/composição não entra (demanda é dos componentes).
    /// Não é "encalhado"; não calcula prejuízo nem promoção.
    /// </summary>
    public bool IsIdle { get; init; }

    /// <summary>
    /// Depósito ou geladeira negativo mesmo quando o total não é.
    /// Não altera TotalStock nem CoverageDays.
    /// </summary>
    public bool HasLocationStockAnomaly { get; init; }

    /// <summary>
    /// SKU de composição/kit. Demanda física permanece nos componentes (70C-B1).
    /// </summary>
    public bool IsCompositionProduct { get; init; }

    /// <summary>
    /// Última data civil com saída de venda válida não cancelada.
    /// Devolução no mesmo dia ou posterior não apaga esse fato.
    /// </summary>
    public DateTime? LastValidSaleDate { get; init; }

    /// <summary>Null quando nunca houve saída válida (NuncaVendido).</summary>
    public int? DaysWithoutSale { get; init; }

    public int HistoryDays { get; init; }
    public bool IsHistoryInsufficient7 { get; init; }
    public bool IsHistoryInsufficient30 { get; init; }
    public bool IsHistoryInsufficient90 { get; init; }

    /// <summary>
    /// True se houve compra fechada com estoque, entrada física confiável ou venda válida.
    /// Cadastro isolado não conta.
    /// </summary>
    public bool HasPhysicalAvailabilityEvidence { get; init; }

    public InventoryTurnoverSituation Situation { get; init; }

    /// <summary>
    /// Filtro 30/60/90 de silêncio (70C-B1): exige evidência física e idade observável.
    /// Produto só cadastrado, sem entrada/venda, não entra no filtro.
    /// </summary>
    public bool QualifiesForDaysWithoutSaleFilter(int minDays)
    {
        if (minDays <= 0)
            return false;
        if (!HasPhysicalAvailabilityEvidence)
            return false;
        if (HistoryDays < minDays)
            return false;
        if (LastValidSaleDate is null)
            return true;
        return (DaysWithoutSale ?? 0) >= minDays;
    }

    /// <summary>
    /// Silêncio 70C-B2: mesma regra de evidência/idade, excluindo SKU de kit.
    /// Nunca vendeu com evidência suficiente conta como "sem venda observada".
    /// </summary>
    public bool QualifiesSilence(int minDays) =>
        !IsCompositionProduct && QualifiesForDaysWithoutSaleFilter(minDays);

    public bool QualifiesSilence30 => QualifiesSilence(30);
    public bool QualifiesSilence60 => QualifiesSilence(60);
    public bool QualifiesSilence90 => QualifiesSilence(90);
}

/// <summary>Resultado em lote do motor 70C-B1/B2. QueryCount é constante (sem N+1).</summary>
public sealed class InventoryIntelligenceSnapshot
{
    public DateTime Today { get; init; }
    public int QueryCount { get; init; }
    public IReadOnlyList<ProductTurnoverRow> Rows { get; init; } = [];
}
