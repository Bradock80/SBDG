namespace SGDB.Domain.Sales;

/// <summary>
/// Termo com formato de código de barras (EAN/GTIN ou run numérico de 4+ dígitos).
/// </summary>
public static class PdvBarcodeTerm
{
    public const string MessageNotFound = "Código de barras não encontrado";

    public static bool LooksLike(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return false;
        var digits = DigitsOnly(term);
        if (digits.Length >= 8)
            return true;
        return digits.Length >= 4 && digits.Length == term.Trim().Length;
    }

    public static string DigitsOnly(string term) =>
        new(term.Where(char.IsDigit).ToArray());
}

public enum PdvF6Route
{
    FocusConfirmedQtyBox = 1,
    QuantityFirstDiscardPreview,
}

/// <summary>
/// 69R — identidade do produto acima da velocidade. Preview ≠ confirmado.
/// Barcode armado nunca perde para lookup/SearchFirst.
/// </summary>
public static class PdvProductIdentityPolicy
{
    public static bool PreviewCanBeIncluded => false;

    public static PdvF6Route RouteF6(bool pendingConfirmed) =>
        pendingConfirmed
            ? PdvF6Route.FocusConfirmedQtyBox
            : PdvF6Route.QuantityFirstDiscardPreview;

    public static bool BarcodeBeatsLookup(string? term) =>
        PdvBarcodeTerm.LooksLike(term);

    public static bool AllowLookupEnter(string? term, bool multiplierArmed) =>
        !multiplierArmed && !PdvBarcodeTerm.LooksLike(term);

    public static bool RequireExactBarcode(bool multiplierArmed) =>
        multiplierArmed;

    /// <summary>
    /// Auto-include só após barcode exato inequívoco e sem quantidade especial armada.
    /// Com 2*/10x o operador precisa ver o nome + QTD e confirmar com Enter.
    /// </summary>
    public static bool AllowAutoInclude(bool exactBarcodeResolved, bool multiplierWasArmed) =>
        exactBarcodeResolved && !multiplierWasArmed;

    public const string ResidualQtyText = "1,000";
    public const string ResidualMoneyText = "0,00";

    public static string ArmedHint(string qtyText) =>
        $"Próxima quantidade: {qtyText} — aguardando leitura";

    public static string SearchLabel(string productName, string? modeLabel, double qty)
    {
        var name = string.IsNullOrWhiteSpace(modeLabel)
            ? productName
            : $"{productName} [{modeLabel}]";
        if (Math.Abs(qty - 1) < 0.0001)
            return name;
        var qtyText = qty.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        return $"{name} — QTD {qtyText}";
    }
}
