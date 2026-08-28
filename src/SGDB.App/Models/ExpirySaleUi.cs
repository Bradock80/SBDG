using SGDB.Services;

namespace SGDB.Models;

/// <summary>
/// 70I-B2 — tradução de <see cref="ExpirySaleException"/> para a UI.
/// Não decide bloqueio nem recalcula validade; só apresenta o que o motor já emitiu.
/// </summary>
public static class ExpirySaleUi
{
    public enum Operation
    {
        Sale,
        Deck,
        Swap,
        Exchange,
        Transfer,
    }

    public sealed class Content
    {
        public string Title { get; init; } = "";
        public string Body { get; init; } = "";
        public string ProductName { get; init; } = "";
    }

    public const string TitleSale = "Venda não realizada";
    public const string TitleSwap = "Troca não realizada";
    public const string TitleExchange = "Troca / Devolução não realizada";
    public const string TitleTransfer = "Transferência não realizada";

    public const string GuidanceMaintain =
        "Confira os lotes em Estoque → Controle de Validades.";

    public const string GuidanceAskAdmin =
        "Peça a um administrador ou gestor para conferir a validade/lote deste produto.";

    public static Content Format(
        ExpirySaleException ex,
        Operation operation,
        Func<int, string?>? resolveProductName = null,
        bool? canMaintainLots = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var d = ex.Decision;
        var productId = d.ProductId;
        var name = ResolveProductName(productId, resolveProductName);
        var canMaintain = canMaintainLots ?? AccessControl.CanMutateLotCoverage();
        var guidance = canMaintain ? GuidanceMaintain : GuidanceAskAdmin;

        var requested = Qty(d.RequestedWarehouseQty);
        var sellable = Qty(d.SellableWarehouseQty);
        var expired = Qty(d.ExpiredQty);

        var (title, reason, closing) = operation switch
        {
            Operation.Transfer => (
                TitleTransfer,
                "A quantidade da transferência exigiria utilizar unidades comprovadamente vencidas no depósito.",
                "Nenhuma transferência foi realizada."),
            Operation.Swap => (
                TitleSwap,
                "A quantidade necessária no depósito exigiria utilizar unidades comprovadamente vencidas.",
                "A troca não foi aplicada. O item anterior permanece."),
            Operation.Exchange => (
                TitleExchange,
                "A quantidade necessária no depósito exigiria utilizar unidades comprovadamente vencidas.",
                "Nenhuma baixa desta operação foi realizada."),
            Operation.Deck => (
                TitleSale,
                "A quantidade necessária no depósito exigiria utilizar unidades comprovadamente vencidas.",
                "Nenhuma baixa desta operação foi realizada. A comanda permanece aberta."),
            _ => (
                TitleSale,
                "A quantidade necessária no depósito exigiria utilizar unidades comprovadamente vencidas.",
                "Nenhuma baixa desta operação foi realizada."),
        };

        var body =
            $"Produto: {name}\n\n" +
            $"{reason}\n\n" +
            $"Quantidade solicitada no depósito: {requested}\n" +
            $"Disponível no depósito sem utilizar unidades vencidas: {sellable}\n" +
            $"Unidades vencidas identificadas: {expired}\n\n" +
            $"{closing}\n\n" +
            guidance;

        return new Content
        {
            Title = title,
            Body = body,
            ProductName = name,
        };
    }

    /// <summary>
    /// Falhas de finalização PDV/Deck que devem usar
    /// <see cref="PdvPostPaymentRecovery"/> (incluindo PIX), nunca só o dispatcher.
    /// </summary>
    public static bool UsesPdvPostPaymentRecovery(Exception ex) =>
        ex is ExpirySaleException or PdvException or CashOperationException or OpenTabException;

    public static string FallbackProductName(int productId) => $"Produto #{productId}";

    private static string ResolveProductName(int productId, Func<int, string?>? resolver)
    {
        try
        {
            var raw = resolver is not null
                ? resolver(productId)
                : ProductService.GetById(productId)?.Name;
            if (!string.IsNullOrWhiteSpace(raw))
                return raw.Trim();
        }
        catch
        {
            // fallback abaixo
        }

        return FallbackProductName(productId);
    }

    private static string Qty(double value) => ProductLotListRow.FormatQty(value);
}
