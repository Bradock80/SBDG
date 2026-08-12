using SGDB.Application.Sales;

namespace SGDB.Application.OpenTabs;

/// <summary>
/// Contrato mínimo para fechar um deck com uma venda (efeitos/persistência no App).
/// </summary>
public interface IOpenTabSettlementGateway
{
    SaleExecutionResult Settle(SettleOpenTabCommand command);
}
