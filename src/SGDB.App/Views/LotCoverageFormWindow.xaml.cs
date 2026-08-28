using System.Windows;
using System.Windows.Input;
using SGDB.Models;

namespace SGDB.Views;

public enum LotCoverageFormMode
{
    Add,
    Edit,
    Split,
    Quantity,
    Remove,
}

public sealed class LotCoverageFormResult
{
    public double Quantity { get; init; }
    public DateTime ExpiryDate { get; init; }
    public string LotNumber { get; init; } = "";
    public string Reason { get; init; } = "";
}

public partial class LotCoverageFormWindow : Window
{
    private readonly LotCoverageFormMode _mode;
    private readonly double _availableUntracked;
    private readonly double _currentQty;
    private readonly bool _isPurchase;
    private readonly DateTime? _currentExpiry;
    private readonly string _currentLot;

    public LotCoverageFormResult? Result { get; private set; }

    public LotCoverageFormWindow(
        LotCoverageFormMode mode,
        string productName,
        double availableUntracked = 0,
        double currentQty = 0,
        DateTime? currentExpiry = null,
        string? currentLot = null,
        bool isPurchase = false)
    {
        _mode = mode;
        _availableUntracked = availableUntracked;
        _currentQty = currentQty;
        _isPurchase = isPurchase;
        _currentExpiry = currentExpiry;
        _currentLot = (currentLot ?? "").Trim();
        InitializeComponent();
        Configure(productName);
    }

    private void Configure(string productName)
    {
        switch (_mode)
        {
            case LotCoverageFormMode.Add:
                Title = "Adicionar validade/lote";
                TitleText.Text = "Adicionar validade/lote";
                HintText.Text = $"Produto: {productName}";
                AvailableText.Text = LotCoverageUi.AvailableToTrackLabel(_availableUntracked);
                ReasonLabel.Text = "Motivo (opcional — padrão: Conferência física)";
                break;

            case LotCoverageFormMode.Edit:
                Title = "Editar validade/lote";
                TitleText.Text = "Editar validade/lote";
                HintText.Text = _isPurchase
                    ? LotCoverageUi.EditPurchaseHint
                    : $"Produto: {productName}";
                QtyPanel.Visibility = Visibility.Collapsed;
                ExpiryBox.Text = _currentExpiry?.ToString("dd/MM/yyyy") ?? "";
                LotBox.Text = _currentLot;
                ReasonLabel.Text = "Motivo *";
                break;

            case LotCoverageFormMode.Split:
                Title = "Dividir cobertura";
                TitleText.Text = "Dividir cobertura";
                HintText.Text =
                    $"Separar parte da linha atual ({LotCoverageUi.QtyDisplay(_currentQty)} un). " +
                    "A quantidade restante permanece na validade/lote original.";
                QtyLabel.Text = "Quantidade a separar *";
                AvailableText.Text = $"Quantidade atual da linha: {LotCoverageUi.QtyDisplay(_currentQty)} un";
                LotBox.Text = _currentLot;
                ReasonLabel.Text = "Motivo *";
                break;

            case LotCoverageFormMode.Quantity:
                Title = "Corrigir quantidade rastreada";
                TitleText.Text = "Corrigir quantidade rastreada";
                HintText.Text = LotCoverageUi.QuantityHint;
                QtyLabel.Text = "Nova quantidade *";
                QtyBox.Text = LotCoverageUi.QtyDisplay(_currentQty).Replace('.', ',');
                AvailableText.Text = $"Quantidade atual: {LotCoverageUi.QtyDisplay(_currentQty)} un";
                ExpiryPanel.Visibility = Visibility.Collapsed;
                LotPanel.Visibility = Visibility.Collapsed;
                ReasonLabel.Text = "Motivo *";
                break;

            case LotCoverageFormMode.Remove:
                Title = "Remover rastreamento";
                TitleText.Text = "Remover rastreamento";
                HintText.Text =
                    "O estoque físico do produto NÃO será alterado. " +
                    "Somente a informação de validade/lote desta quantidade será removida.";
                QtyPanel.Visibility = Visibility.Collapsed;
                ExpiryPanel.Visibility = Visibility.Collapsed;
                LotPanel.Visibility = Visibility.Collapsed;
                ReasonLabel.Text = "Motivo *";
                OkButton.Content = "Remover";
                break;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var reason = ReasonBox.Text?.Trim() ?? "";

        if (_mode is LotCoverageFormMode.Edit or LotCoverageFormMode.Split
            or LotCoverageFormMode.Quantity or LotCoverageFormMode.Remove)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                ShowError("Informe o motivo desta correção.");
                return;
            }
        }

        double qty = 0;
        var expiry = DateTime.Today;
        var lot = LotBox.Text?.Trim() ?? "";

        if (_mode is LotCoverageFormMode.Add or LotCoverageFormMode.Split or LotCoverageFormMode.Quantity)
        {
            if (!LotCoverageUi.TryParseQty(QtyBox.Text, out qty, out var qtyErr))
            {
                ShowError(qtyErr);
                return;
            }
        }

        if (_mode is LotCoverageFormMode.Add or LotCoverageFormMode.Edit or LotCoverageFormMode.Split)
        {
            if (!LotCoverageUi.TryParseExpiry(ExpiryBox.Text, out expiry, out var expErr))
            {
                ShowError(expErr);
                return;
            }
        }

        if (_mode == LotCoverageFormMode.Edit)
        {
            var involvesExpired =
                (_currentExpiry is DateTime cur && cur.Date < DateTime.Today)
                || expiry.Date < DateTime.Today;
            if (involvesExpired)
            {
                var confirm = MessageBox.Show(
                    LotCoverageUi.SensitiveExpiryConfirmMessage,
                    Title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                    return;
            }
        }

        if (_mode == LotCoverageFormMode.Remove)
        {
            var confirm = MessageBox.Show(
                LotCoverageUi.RemoveConfirmMessage,
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;
        }

        Result = new LotCoverageFormResult
        {
            Quantity = qty,
            ExpiryDate = expiry,
            LotNumber = lot,
            Reason = reason,
        };
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
