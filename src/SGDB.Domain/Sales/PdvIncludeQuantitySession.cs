namespace SGDB.Domain.Sales;

/// <summary>
/// Wiring da quantidade efetiva no PDV (69P-B).
/// Um único Consume() por produto pendente / inclusão.
/// Multiplicador × quantidade-base do scan (1 na unidade, N no fardo/CX, 1 no maço).
/// QtyBox editada após o pendente prevalece e cancela o multiplicador residual.
/// F6 aplica-se à próxima inclusão, one-shot — não a vários itens.
/// </summary>
public sealed class PdvIncludeQuantitySession
{
    public PdvScanMultiplierState Multiplier { get; } = new();
    public PdvF6QuantitySession F6 { get; } = new();

    public double BaseQty { get; private set; } = 1;
    public bool QtyBoxEdited { get; private set; }

    public bool IsArmed => Multiplier.IsArmed;
    public double ArmedQuantity => Multiplier.Quantity;
    public bool IsF6Editing => F6.IsEditing;

    public void MarkQtyBoxEdited() => QtyBoxEdited = true;

    public void Cancel()
    {
        F6.Cancel();
        Multiplier.Clear();
        QtyBoxEdited = false;
    }

    public void ResetForNewSale()
    {
        Cancel();
        BaseQty = 1;
    }

    public PdvQuantityCheckResult ConfirmF6(string? raw)
    {
        QtyBoxEdited = false;
        return F6.Confirm(raw, Multiplier);
    }

    public PdvQuantityCheckResult ArmExplicit(double qty)
    {
        QtyBoxEdited = false;
        return Multiplier.TryArm(qty);
    }

    /// <summary>
    /// Produto confirmado (scan, lookup, código). Consome o multiplicador se armado.
    /// Não usar no preview da lista de busca.
    /// </summary>
    public double OnProductPending(double scanQuantity)
    {
        BaseQty = scanQuantity > 0 ? scanQuantity : 1;
        QtyBoxEdited = false;
        return ApplyArmedOrFallback(BaseQty);
    }

    /// <summary>
    /// Quantidade que seria incluída, sem consumir. Usar antes das barreiras 69G.
    /// </summary>
    public double PreviewInclude(double qtyBoxParsed)
    {
        if (QtyBoxEdited || !Multiplier.IsArmed)
            return qtyBoxParsed;
        return Multiplier.Quantity * BaseQty;
    }

    /// <summary>
    /// Inclusão efetiva. Se a QtyBox foi editada, a digitação prevalece e o armado é cancelado.
    /// Se F6/10x ainda está armado (produto já pendente), consome one-shot × BaseQty.
    /// </summary>
    public double CommitInclude(double qtyBoxParsed)
    {
        if (QtyBoxEdited)
        {
            Multiplier.Clear();
            QtyBoxEdited = false;
            return qtyBoxParsed;
        }

        return ApplyArmedOrFallback(qtyBoxParsed);
    }

    private double ApplyArmedOrFallback(double fallback)
    {
        if (!Multiplier.IsArmed)
            return fallback;
        return Multiplier.Consume() * BaseQty;
    }
}

/// <summary>
/// O Enter de confirmação do F6 não pode vazar para a QtyBox:
/// ao colapsar F6QtyBox o WPF transfere o foco no mesmo evento.
/// Capture no PreviewKeyDown; Release no Dispatcher após o foco voltar à busca.
/// </summary>
public sealed class PdvF6EnterLeakGuard
{
    public bool SuppressQtyBoxEnter { get; private set; }

    public void CaptureF6Enter() => SuppressQtyBoxEnter = true;

    public void Release() => SuppressQtyBoxEnter = false;

    public bool AllowQtyBoxInclude => !SuppressQtyBoxEnter;
}
