namespace SGDB.Application.Sales;

/// <summary>
/// Resultado mínimo após alterar pagamento.
/// A View só precisa do SaleId para refresh — não transporta PdvSaleDetail.
/// </summary>
public sealed class ChangeSalePaymentResult
{
    public int SaleId { get; init; }
}
