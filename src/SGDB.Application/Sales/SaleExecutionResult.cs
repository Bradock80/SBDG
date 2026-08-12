namespace SGDB.Application.Sales;

/// <summary>Resultado mínimo após execução de venda (PDV ou fechamento de deck).</summary>
public sealed class SaleExecutionResult
{
    public int SaleId { get; init; }
    public double Total { get; init; }
    public double ChangeAmount { get; init; }
    public double CashReceived { get; init; }
}
