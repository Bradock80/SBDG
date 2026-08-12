using SGDB.Adapters;
using SGDB.Application.OpenTabs;
using SGDB.Application.Sales;

namespace SGDB;

/// <summary>
/// Composition root simples (sem DI container) para casos de uso Application.
/// </summary>
public static class ApplicationServices
{
    private static SettleOpenTabUseCase? _settleOpenTab;
    private static FinalizeSaleUseCase? _finalizeSale;
    private static CancelSaleUseCase? _cancelSale;
    private static ChangeSalePaymentUseCase? _changeSalePayment;
    private static PreviewSwapSaleItemUseCase? _previewSwapSaleItem;
    private static SwapSaleItemUseCase? _swapSaleItem;

    public static SettleOpenTabUseCase SettleOpenTab =>
        _settleOpenTab ??= new SettleOpenTabUseCase(new OpenTabSettlementGateway());

    public static FinalizeSaleUseCase FinalizeSale =>
        _finalizeSale ??= new FinalizeSaleUseCase(new FinalizeSaleGateway());

    public static CancelSaleUseCase CancelSale =>
        _cancelSale ??= new CancelSaleUseCase(new CancelSaleGateway());

    public static ChangeSalePaymentUseCase ChangeSalePayment =>
        _changeSalePayment ??= new ChangeSalePaymentUseCase(new ChangeSalePaymentGateway());

    public static PreviewSwapSaleItemUseCase PreviewSwapSaleItem =>
        _previewSwapSaleItem ??= new PreviewSwapSaleItemUseCase(new PreviewSwapSaleItemGateway());

    public static SwapSaleItemUseCase SwapSaleItem =>
        _swapSaleItem ??= new SwapSaleItemUseCase(new SwapSaleItemGateway());
}
