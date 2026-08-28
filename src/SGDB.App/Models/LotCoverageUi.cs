using System.Globalization;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Models;

/// <summary>
/// 70B3A-D — rótulos e mensagens da UI de manutenção de cobertura.
/// Não calcula regra de negócio; apenas apresenta o que o motor já decidiu.
/// </summary>
public static class LotCoverageUi
{
    public const string WindowTitle = "Manutenção de validade e lote";
    public const string FridgeDisclaimer =
        "A geladeira ainda não possui rastreamento por lote nesta versão.";

    public const string RemoveConfirmMessage =
        "Esta operação NÃO removerá o produto do estoque.\n\n" +
        "Ela removerá apenas a informação de validade/lote desta quantidade, " +
        "que voltará a aparecer como estoque sem rastreamento.\n\n" +
        "Deseja continuar?";

    public const string SensitiveExpiryConfirmMessage =
        "Esta alteração envolve uma validade vencida.\n\n" +
        "Confirme somente se você realizou a conferência física do produto.\n\n" +
        "Deseja continuar?";

    public const string QuantityHint =
        "Esta correção altera somente a quantidade rastreada por validade/lote. " +
        "O estoque físico do produto não será alterado.";

    public const string EditPurchaseHint =
        "Esta informação possui origem em uma compra. " +
        "A correção preservará o vínculo e o custo original.";

    public const string SelectProductHint =
        "Selecione um produto na lista para manter validade/lote.";

    public const string OriginPurchase = "Compra";
    public const string OriginManual = "Conferência manual";
    public const string EmptyLotDisplay = "—";
    public const string MissingExpiryDisplay = ProductExpiryService.UninformedDisplay;

    public static string ConsistencyLabel(LotCoverageConsistencyStatus status) =>
        status switch
        {
            LotCoverageConsistencyStatus.Consistent => "Cobertura consistente",
            LotCoverageConsistencyStatus.UnderTracked => "Há estoque sem rastreamento",
            LotCoverageConsistencyStatus.OverTracked => "Cobertura inconsistente",
            LotCoverageConsistencyStatus.NegativeStock => "Estoque negativo",
            LotCoverageConsistencyStatus.ZeroStock => "Sem estoque no depósito",
            LotCoverageConsistencyStatus.ProductNotFound => "Produto não encontrado",
            _ => "Situação da cobertura",
        };

    public static string ConsistencyHint(LotCoverageConsistencyStatus status) =>
        status switch
        {
            LotCoverageConsistencyStatus.OverTracked =>
                "A quantidade registrada nos lotes é maior que o estoque do depósito. " +
                "Faça uma conferência antes de adicionar novas informações.",
            LotCoverageConsistencyStatus.NegativeStock =>
                LotCoverageRules.NegativeStockMessage,
            LotCoverageConsistencyStatus.UnderTracked =>
                "Há quantidade no depósito ainda sem validade/lote cadastrada.",
            LotCoverageConsistencyStatus.ZeroStock =>
                "Não existe estoque físico no depósito para rastrear.",
            _ => "",
        };

    public static string TraceabilityLabel(LotCoverageTraceability value) =>
        value switch
        {
            LotCoverageTraceability.Complete => "Rastreabilidade completa",
            LotCoverageTraceability.Partial => "Rastreabilidade parcial",
            LotCoverageTraceability.UninformedExpiry => "Validade não informada",
            LotCoverageTraceability.Untracked => "Sem rastreamento",
            _ => "—",
        };

    public static string OriginLabel(int? purchaseId) =>
        purchaseId is int id && id > 0 ? OriginPurchase : OriginManual;

    public static string OriginDetail(int? purchaseId) =>
        purchaseId is int id && id > 0 ? $"{OriginPurchase} #{id}" : OriginManual;

    public static string LotDisplay(string? lotNumber) =>
        string.IsNullOrWhiteSpace(lotNumber) ? EmptyLotDisplay : lotNumber.Trim();

    public static string ExpiryDisplay(DateTime? expiry) =>
        expiry is DateTime d ? d.ToString("dd/MM/yyyy") : MissingExpiryDisplay;

    public static string QtyDisplay(double qty) => ProductLotListRow.FormatQty(qty);

    public static string CostDisplay(LotCoverageLine line)
    {
        if (line.UsedCost is double cost)
            return $"{ProductPriceHelper.MoneyBr(cost)} ({ValidityControlUi.CostSourceLabel(line.CostSource)})";
        return ValidityControlUi.CostSourceLabel(line.CostSource);
    }

    public static string FormatHeader(LotCoverageSnapshot snap) =>
        $"Produto: {snap.ProductName}\n" +
        $"Estoque no depósito: {QtyDisplay(snap.Stock)} un\n" +
        $"Rastreado: {QtyDisplay(snap.TrackedQuantity)} un\n" +
        $"Sem rastreamento: {QtyDisplay(snap.UntrackedQuantity)} un\n" +
        $"Geladeira: {QtyDisplay(snap.StockFridge)} un";

    public static string AvailableToTrackLabel(double untracked) =>
        $"Disponível para rastrear: {QtyDisplay(untracked)} un";

    public static bool CanMutateUi() =>
        AccessControl.CanMutateLotCoverage() && !StoreNetworkMode.IsClient;

    public static string MapError(Exception ex, string? operation = null)
    {
        if (ex is LotCoverageException lce)
            return MapErrorCode(lce.ErrorCode, operation) ?? lce.Message;

        if (ex is StoreNetworkClientBlockedException)
            return "Neste computador (Rede Loja · cliente) não é permitido alterar cobertura de validade/lote. Use a matriz.";

        return string.IsNullOrWhiteSpace(ex.Message)
            ? "Não foi possível concluir a operação."
            : ex.Message.Trim();
    }

    public static string? MapErrorCode(string errorCode, string? operation = null)
    {
        if (string.Equals(errorCode, LotCoverageRules.PurchaseOriginProtected, StringComparison.Ordinal))
            return MapPurchaseProtected(operation);

        return errorCode switch
        {
            LotCoverageRules.QuantityExceedsUntracked =>
                "A quantidade informada é maior que o estoque disponível sem rastreamento.",
            LotCoverageRules.KeyCollision =>
                "Já existe outra cobertura com essa combinação de lote e validade. " +
                "A operação não pode unir registros de origens diferentes.",
            LotCoverageRules.NegativeStock => LotCoverageRules.NegativeStockMessage,
            LotCoverageRules.OverTracked =>
                "A cobertura está inconsistente (mais rastreado do que o estoque). " +
                "Faça uma conferência antes de adicionar novas informações.",
            LotCoverageRules.OpenInventory =>
                "Existe um inventário em andamento. Finalize ou cancele o inventário " +
                "antes de alterar a rastreabilidade de validade/lote.",
            LotCoverageRules.AccessDenied => LotCoverageRules.AccessDeniedMessage,
            LotCoverageRules.InactiveProduct => LotCoverageRules.InactiveProductMessage,
            LotCoverageRules.AbsorbedProduct => LotCoverageRules.AbsorbedProductMessage,
            LotCoverageRules.ZeroStock => LotCoverageRules.ZeroStockMessage,
            LotCoverageRules.QuantityInvalid => LotCoverageRules.QuantityInvalidMessage,
            LotCoverageRules.ReasonRequired => LotCoverageRules.ReasonRequiredMessage,
            LotCoverageRules.ExpiryRequired => LotCoverageRules.ExpiryRequiredMessage,
            LotCoverageRules.LotNotFound => LotCoverageRules.LotNotFoundMessage,
            LotCoverageRules.SplitInvalid => LotCoverageRules.SplitInvalidMessage,
            LotCoverageRules.ProductNotFound => LotCoverageRules.ProductNotFoundMessage,
            _ => null,
        };
    }

    public static string MapPurchaseProtected(string? operation) =>
        operation switch
        {
            "split" =>
                "Esta cobertura foi criada a partir de uma compra e não pode ser dividida " +
                "manualmente nesta versão, pois o vínculo de origem precisa ser preservado.",
            "quantity" =>
                "Esta quantidade está vinculada a uma compra e não pode ser alterada " +
                "manualmente nesta versão.",
            "remove" =>
                "Esta cobertura possui origem em uma compra e não pode ser removida " +
                "manualmente nesta versão.",
            _ =>
                "Esta cobertura possui origem em uma compra e não pode ser alterada " +
                "dessa forma nesta versão.",
        };

    public static bool TryParseQty(string? raw, out double qty, out string error)
    {
        qty = 0;
        error = "";
        var text = (raw ?? "").Trim().Replace(',', '.');
        if (string.IsNullOrEmpty(text)
            || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out qty)
            || qty <= LotCoverageService.QtyEpsilon)
        {
            error = "Informe uma quantidade maior que zero.";
            return false;
        }

        qty = Math.Round(qty, 4);
        return true;
    }

    public static bool TryParseExpiry(string? raw, out DateTime date, out string error)
    {
        date = default;
        error = "";
        var text = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = LotCoverageRules.ExpiryRequiredMessage;
            return false;
        }

        if (!DateTime.TryParseExact(
                text,
                ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            error = "Informe a validade no formato dd/MM/aaaa.";
            return false;
        }

        date = date.Date;
        return true;
    }

    public static IReadOnlyList<LotCoverageLineUi> ToRows(LotCoverageSnapshot snap) =>
        snap.Lines.Select(LotCoverageLineUi.From).ToList();
}

/// <summary>Linha de exibição da manutenção — uma origem por linha (sem mesclar).</summary>
public sealed class LotCoverageLineUi
{
    public required LotCoverageLine Source { get; init; }

    public int Id => Source.Id;
    public double Quantity => Source.Quantity;
    public DateTime? ExpiryDate => Source.ExpiryDate;
    public int? PurchaseId => Source.PurchaseId;
    public bool IsPurchaseOrigin => PurchaseId is > 0;
    public bool IsExpired => Source.IsExpired;

    public string QtyDisplay => LotCoverageUi.QtyDisplay(Source.Quantity);
    public string ExpiryDisplay => LotCoverageUi.ExpiryDisplay(Source.ExpiryDate);
    public string LotDisplay => LotCoverageUi.LotDisplay(Source.LotNumber);
    public string OriginDisplay => LotCoverageUi.OriginDetail(Source.PurchaseId);
    public string CostDisplay => LotCoverageUi.CostDisplay(Source);
    public string TraceabilityDisplay => LotCoverageUi.TraceabilityLabel(Source.Traceability);

    public static LotCoverageLineUi From(LotCoverageLine line) => new() { Source = line };
}
