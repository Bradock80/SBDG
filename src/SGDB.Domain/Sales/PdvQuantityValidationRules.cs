namespace SGDB.Domain.Sales;

public enum PdvQuantityRejectReason
{
    None = 0,
    Invalid,
    LooksLikeGtin,
    AboveLineLimit,
    NonFinite,
    TotalTooHigh,
}

public readonly record struct PdvQuantityCheckResult(
    bool Allowed,
    PdvQuantityRejectReason Reason,
    string? Message)
{
    public static PdvQuantityCheckResult Ok { get; } = new(true, PdvQuantityRejectReason.None, null);

    public static PdvQuantityCheckResult Reject(PdvQuantityRejectReason reason, string message) =>
        new(false, reason, message);
}

/// <summary>
/// Barreiras contra quantidade absurda no PDV (EAN/GTIN no campo Qtd, teto por linha, total extremo).
/// Constantes centralizadas — não espalhar números mágicos na UI.
/// </summary>
public static class PdvQuantityValidationRules
{
    /// <summary>Teto operacional de unidades por linha do carrinho.</summary>
    public const double MaxQuantityPerLine = 9999;

    /// <summary>
    /// Teto extremo do total da venda. Acima disso a finalização é bloqueada.
    /// Loja de depósito: vendas comuns ficam muito abaixo; R$ 100.000 é trava de segurança, não de negócio.
    /// </summary>
    public const double ExtremeSaleTotal = 100_000;

    public const string MessageGtinInQuantity =
        "Código de barras detectado no campo Quantidade. Leia o produto novamente.";

    public const string MessageQuantityLimit =
        "Quantidade acima do limite permitido para uma única venda.";

    public const string MessageExtremeTotal =
        "Valor da venda fora do limite de segurança. Revise os itens.";

    public const string MessageInvalidQuantity = "Quantidade inválida.";

    public static bool LooksLikeGtinText(string? raw)
    {
        var digits = ExtractIntegerDigitRun(raw);
        if (digits is null)
            return false;
        return digits.Length is 8 or 12 or 13 or 14;
    }

    /// <summary>
    /// Inteiro (sem vírgula/ponto) com 8/12/13/14 dígitos, ou o texto coincide com barcode/code longo do produto.
    /// </summary>
    public static bool LooksLikeMisplacedBarcode(string? raw, string? productBarcode = null, string? productCode = null)
    {
        if (LooksLikeGtinText(raw))
            return true;
        var compact = CompactDigits(raw);
        if (compact.Length < 8)
            return false;
        return SameDigits(compact, productBarcode) || SameDigits(compact, productCode);
    }

    public static PdvQuantityCheckResult EvaluateRaw(
        string? raw, string? productBarcode = null, string? productCode = null)
    {
        if (LooksLikeMisplacedBarcode(raw, productBarcode, productCode))
            return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.LooksLikeGtin, MessageGtinInQuantity);

        if (string.IsNullOrWhiteSpace(raw))
            return EvaluateQuantity(1, productBarcode, productCode);

        if (!TryParseQuantity(raw, out var qty))
            return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.Invalid, MessageInvalidQuantity);

        return EvaluateQuantity(qty, productBarcode, productCode);
    }

    public static PdvQuantityCheckResult EvaluateQuantity(
        double qty, string? productBarcode = null, string? productCode = null)
    {
        if (double.IsNaN(qty) || double.IsInfinity(qty))
            return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.NonFinite, MessageInvalidQuantity);
        if (qty <= 0)
            return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.Invalid, MessageInvalidQuantity);

        if (IsInteger(qty))
        {
            var digits = Math.Abs(Math.Round(qty)).ToString("0");
            if (digits.Length is 8 or 12 or 13 or 14)
                return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.LooksLikeGtin, MessageGtinInQuantity);
            if (digits.Length >= 8 && (SameDigits(digits, productBarcode) || SameDigits(digits, productCode)))
                return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.LooksLikeGtin, MessageGtinInQuantity);
        }

        if (qty - MaxQuantityPerLine > 0.0000001)
            return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.AboveLineLimit, MessageQuantityLimit);

        return PdvQuantityCheckResult.Ok;
    }

    public static PdvQuantityCheckResult EvaluateLine(double qty, double unitPrice, string? barcode = null, string? code = null)
    {
        var qtyCheck = EvaluateQuantity(qty, barcode, code);
        if (!qtyCheck.Allowed)
            return qtyCheck;
        if (double.IsNaN(unitPrice) || double.IsInfinity(unitPrice) || unitPrice < 0)
            return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.Invalid, MessageInvalidQuantity);
        var sub = qty * unitPrice;
        if (double.IsNaN(sub) || double.IsInfinity(sub))
            return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.NonFinite, MessageInvalidQuantity);
        return PdvQuantityCheckResult.Ok;
    }

    public static PdvQuantityCheckResult EvaluateCartTotal(double total)
    {
        if (double.IsNaN(total) || double.IsInfinity(total) || total <= 0)
            return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.Invalid, MessageInvalidQuantity);
        if (total - ExtremeSaleTotal > 0.009)
            return PdvQuantityCheckResult.Reject(PdvQuantityRejectReason.TotalTooHigh, MessageExtremeTotal);
        return PdvQuantityCheckResult.Ok;
    }

    public static bool TryParseQuantity(string? raw, out double qty)
    {
        qty = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var normalized = raw.Trim().Replace(',', '.');
        return double.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out qty);
    }

    private static bool IsInteger(double qty) =>
        Math.Abs(qty - Math.Round(qty)) < 0.0000001;

    private static string CompactDigits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        return new string(raw.Trim().Where(char.IsDigit).ToArray());
    }

    /// <summary>
    /// Só trata como “run inteiro” se não houver separador decimal.
    /// 0,250 / 1,5 / 12.5 não são EAN.
    /// </summary>
    private static string? ExtractIntegerDigitRun(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim();
        if (t.IndexOf(',') >= 0 || t.IndexOf('.') >= 0)
            return null;
        if (t.Length == 0 || t.Any(c => !char.IsDigit(c)))
            return null;
        return t;
    }

    private static bool SameDigits(string digits, string? other)
    {
        if (string.IsNullOrWhiteSpace(other))
            return false;
        var o = new string(other.Where(char.IsDigit).ToArray());
        return o.Length >= 8 && string.Equals(digits, o, StringComparison.Ordinal);
    }
}

/// <summary>
/// Política de foco após localizar produto. Scanner no QtyBox+SelectAll foi a causa do incidente.
/// SelectAll no QtyBox só no fluxo manual (pesquisa por nome/código).
/// </summary>
public static class PdvScanFocusPolicy
{
    public static bool AutoIncludeQtyOneAfterBarcodeScan => true;
    public static bool SelectAllQuantityAfterScan => false;

    public static bool ShouldAutoInclude(bool fromBarcodeScan) =>
        fromBarcodeScan && AutoIncludeQtyOneAfterBarcodeScan;

    public static bool ShouldFocusQtyBox(bool fromBarcodeScan) => !fromBarcodeScan;

    public static bool ShouldSelectAllQty(bool fromBarcodeScan) => !fromBarcodeScan;
}

public readonly record struct PdvQtyBoxGuardResult(
    bool Accepted,
    string QtyTextAfter,
    bool FocusSearchBox,
    string? Message,
    PdvQuantityRejectReason Reason);

/// <summary>
/// Resultado da rejeição no QtyBox: resetar para 1 e devolver o foco à busca.
/// </summary>
public static class PdvQtyBoxGuard
{
    public const string ResetQtyText = "1,000";

    public static PdvQtyBoxGuardResult Evaluate(string? raw, string? barcode, string? code)
    {
        var check = PdvQuantityValidationRules.EvaluateRaw(raw, barcode, code);
        if (!check.Allowed)
            return new(false, ResetQtyText, true, check.Message, check.Reason);
        return new(true, raw ?? ResetQtyText, false, null, PdvQuantityRejectReason.None);
    }
}
