using System.Globalization;
using System.Windows;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class InventoryComboApprovalWindow : Window
{
    readonly InventoryComboApprovalDraft _draft;
    public InventoryComboCampaign? Confirmed { get; private set; }

    public InventoryComboApprovalWindow(InventoryComboApprovalDraft draft)
    {
        InitializeComponent();
        _draft = draft;
        PdvWarningText.Text = InventoryComboLifecycleUi.PdvWarning;
        NameBox.Text = draft.CommercialName;
        PriceBox.Text = draft.SuggestedPrice.ToString("N2", ProductPriceHelper.Br);
        MaxQtyBox.Text = draft.MaxSafeQuantity.ToString("0.##", ProductPriceHelper.Br);
        StartBox.Text = draft.StartDate.ToString("dd/MM/yyyy");
        EndBox.Text = draft.EndDate.ToString("dd/MM/yyyy");
        SummaryText.Text =
            $"Alvo: {draft.TargetName} ({draft.TargetQty:0.##}) · estoque {draft.TargetStock:0.##}\n" +
            $"Acompanhante: {draft.AnchorName} ({draft.AnchorQty:0.##}) · estoque {draft.AnchorStock:0.##}\n" +
            $"Preço separado: {Money(draft.SeparatePrice)} · sugerido: {Money(draft.SuggestedPrice)}\n" +
            $"Desconto: {Money(draft.DiscountAmount)} ({draft.DiscountPercent:0.#}%)\n" +
            $"Custo: {Money(draft.Cost)} · lucro bruto: {Money(draft.GrossProfit)} · margem: {draft.MarginPercent:0.#}%\n" +
            $"Qtd. máxima segura: {draft.MaxSafeQuantity:0.##}\n" +
            $"Motivo: {draft.Reason} · confiança: {draft.Confidence}\n" +
            (string.IsNullOrWhiteSpace(draft.Limitations) ? "" : draft.Limitations);
        BlockText.Text = draft.BlockReason ?? "";
        BtnConfirm.IsEnabled = draft.HasSafePrice && string.IsNullOrWhiteSpace(draft.BlockReason);
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("ProdutosEditar", "aprovar combo", this))
            return;

        _draft.CommercialName = NameBox.Text?.Trim() ?? "";
        if (!TryMoney(PriceBox.Text, out var price))
        {
            MessageBox.Show("Informe um preço válido.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryQty(MaxQtyBox.Text, out var maxQty))
        {
            MessageBox.Show("Informe a quantidade máxima.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryDate(StartBox.Text, out var start) || !TryDate(EndBox.Text, out var end))
        {
            MessageBox.Show("Informe as datas no formato dd/MM/aaaa.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _draft.SuggestedPrice = price;
        _draft.MaxSafeQuantity = maxQty;
        _draft.StartDate = start;
        _draft.EndDate = end;

        var fresh = InventoryComboApprovalService.ReloadDraft(_draft.TargetProductId, _draft.AnchorProductId);
        fresh.CommercialName = _draft.CommercialName;
        fresh.SuggestedPrice = price;
        fresh.MaxSafeQuantity = maxQty;
        fresh.StartDate = start;
        fresh.EndDate = end;
        var result = InventoryComboApprovalService.Confirm(fresh);
        if (!result.Ok)
        {
            MessageBox.Show(result.Error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Confirmed = result.Campaign;
        DialogResult = true;
        Close();
    }

    static string Money(double value) =>
        value.ToString("C2", ProductPriceHelper.Br);

    static bool TryMoney(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Number, ProductPriceHelper.Br, out value);

    static bool TryQty(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Number, ProductPriceHelper.Br, out value);

    static bool TryDate(string? text, out DateOnly value) =>
        DateOnly.TryParse(text, ProductPriceHelper.Br, DateTimeStyles.None, out value);
}
