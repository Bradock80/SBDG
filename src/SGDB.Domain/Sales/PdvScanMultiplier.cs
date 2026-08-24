using System.Text.RegularExpressions;

namespace SGDB.Domain.Sales;

public enum PdvScanMultiplierKind
{
    None = 0,
    Armed,
    Combined,
}

public readonly record struct PdvScanMultiplierParseResult(
    PdvScanMultiplierKind Kind,
    double Quantity,
    string Remainder,
    PdvQuantityCheckResult Check)
{
    public static PdvScanMultiplierParseResult None(string remainder) =>
        new(PdvScanMultiplierKind.None, 1, remainder, PdvQuantityCheckResult.Ok);

    public bool IsExplicit => Kind is PdvScanMultiplierKind.Armed or PdvScanMultiplierKind.Combined;
}

/// <summary>
/// Multiplicador explícito no SearchBox: 10* ou 10x, opcionalmente seguido do código.
/// Não interpreta barcode puro como quantidade.
/// </summary>
public static class PdvScanMultiplierParser
{
    static readonly Regex Pattern = new(
        @"^\s*(?<qty>-?[0-9]+(?:[.,][0-9]+)?)\s*[xX*]\s*(?<rest>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static PdvScanMultiplierParseResult Parse(string? raw)
    {
        var term = (raw ?? "").Trim();
        if (term.Length == 0)
            return PdvScanMultiplierParseResult.None("");

        var m = Pattern.Match(term);
        if (!m.Success)
            return PdvScanMultiplierParseResult.None(term);

        var qtyRaw = m.Groups["qty"].Value;
        var rest = m.Groups["rest"].Value.Trim();

        if (PdvQuantityValidationRules.LooksLikeGtinText(qtyRaw)
            || PdvQuantityValidationRules.LooksLikeMisplacedBarcode(qtyRaw))
        {
            return new(
                PdvScanMultiplierKind.None,
                1,
                term,
                PdvQuantityCheckResult.Reject(
                    PdvQuantityRejectReason.LooksLikeGtin,
                    PdvQuantityValidationRules.MessageGtinInQuantity));
        }

        if (!PdvQuantityValidationRules.TryParseQuantity(qtyRaw, out var qty))
        {
            return new(
                PdvScanMultiplierKind.None,
                1,
                term,
                PdvQuantityCheckResult.Reject(
                    PdvQuantityRejectReason.Invalid,
                    PdvQuantityValidationRules.MessageInvalidQuantity));
        }

        var check = PdvQuantityValidationRules.EvaluateQuantity(qty);
        if (!check.Allowed)
            return new(PdvScanMultiplierKind.None, 1, term, check);

        var kind = rest.Length == 0 ? PdvScanMultiplierKind.Armed : PdvScanMultiplierKind.Combined;
        return new(kind, qty, rest, PdvQuantityCheckResult.Ok);
    }
}

/// <summary>Quantidade armada para a próxima leitura de código. One-shot.</summary>
public sealed class PdvScanMultiplierState
{
    public bool IsArmed { get; private set; }
    public double Quantity { get; private set; } = 1;

    public PdvQuantityCheckResult TryArm(double qty)
    {
        var check = PdvQuantityValidationRules.EvaluateQuantity(qty);
        if (!check.Allowed)
        {
            Clear();
            return check;
        }

        IsArmed = true;
        Quantity = qty;
        return check;
    }

    public double Consume(double fallback = 1)
    {
        if (!IsArmed)
            return fallback;
        var qty = Quantity;
        Clear();
        return qty;
    }

    public void Clear()
    {
        IsArmed = false;
        Quantity = 1;
    }
}

public enum PdvF6Mode
{
    Off = 0,
    Editing,
}

/// <summary>
/// Modo F6: entrada explícita da quantidade da próxima leitura.
/// Não usa a QtyBox do item pendente. Confirmação arma o multiplicador one-shot.
/// </summary>
public sealed class PdvF6QuantitySession
{
    public PdvF6Mode Mode { get; private set; }
    public bool IsEditing => Mode == PdvF6Mode.Editing;

    public void Enter() => Mode = PdvF6Mode.Editing;

    public void Cancel() => Mode = PdvF6Mode.Off;

    public PdvQuantityCheckResult Confirm(string? raw, PdvScanMultiplierState multiplier)
    {
        ArgumentNullException.ThrowIfNull(multiplier);

        if (string.IsNullOrWhiteSpace(raw))
        {
            Mode = PdvF6Mode.Off;
            multiplier.Clear();
            return PdvQuantityCheckResult.Ok;
        }

        var check = PdvQuantityValidationRules.EvaluateRaw(raw);
        if (!check.Allowed)
        {
            Mode = PdvF6Mode.Off;
            multiplier.Clear();
            return check;
        }

        if (!PdvQuantityValidationRules.TryParseQuantity(raw, out var qty))
        {
            Mode = PdvF6Mode.Off;
            multiplier.Clear();
            return PdvQuantityCheckResult.Reject(
                PdvQuantityRejectReason.Invalid,
                PdvQuantityValidationRules.MessageInvalidQuantity);
        }

        Mode = PdvF6Mode.Off;
        return multiplier.TryArm(qty);
    }
}
