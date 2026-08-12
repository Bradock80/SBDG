namespace SGDB.Domain.Sales;

/// <summary>
/// Dinheiro recebido e troco calculados para a venda.
/// </summary>
public readonly struct CashChangeResult
{
    public double? CashReceived { get; init; }
    public double ChangeAmount { get; init; }

    public CashChangeResult(double? cashReceived, double changeAmount)
    {
        CashReceived = cashReceived;
        ChangeAmount = changeAmount;
    }
}
