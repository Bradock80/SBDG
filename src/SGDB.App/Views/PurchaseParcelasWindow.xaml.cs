using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Views;

public sealed class PurchaseParcelaRow
{
    public string Vencimento { get; set; } = "";
    public string Tipo { get; set; } = "Boleto";
    public double Valor { get; set; }
    public string ValorDisplay
    {
        get => Valor.ToString("N2", CultureInfo.CurrentCulture);
        set => Valor = ProductPriceHelper.ParseBr(value);
    }
}

public partial class PurchaseParcelasWindow : Window
{
    private readonly double _total;
    private readonly ObservableCollection<PurchaseParcelaRow> _parcelas = [];
    private bool _uiReady;

    public PurchaseFinanceiroMeta? Result { get; private set; }

    public PurchaseParcelasWindow(double total, string? primeiroVencimentoBr = null)
    {
        _total = total;
        InitializeComponent();
        _uiReady = true;

        TotalBox.Text = ProductPriceHelper.FormatBr(total);
        EntradaBox.Text = "0,00";
        RestanteBox.Text = ProductPriceHelper.FormatBr(total);
        QtdBox.Text = "1";
        IntervaloBox.Text = "1";
        IntervaloTipoBox.SelectedIndex = 0;
        PrimeiroVencBox.Text = string.IsNullOrWhiteSpace(primeiroVencimentoBr)
            ? DateBrHelper.TodayBr()
            : primeiroVencimentoBr.Trim();

        ParcelasGrid.ItemsSource = _parcelas;
        GerarParcelas();
        EntradaBox.Focus();
    }

    private void RecalcResumo()
    {
        var entrada = ProductPriceHelper.ParseBr(EntradaBox.Text);
        var rest = Math.Max(0, _total - entrada);
        RestanteBox.Text = ProductPriceHelper.FormatBr(rest);
    }

    private void GerarParcelas()
    {
        try
        {
            RecalcResumo();
            var entrada = ProductPriceHelper.ParseBr(EntradaBox.Text);
            var qtd = ParseInt(QtdBox.Text, 1);
            var intervalo = ParseInt(IntervaloBox.Text, 1);
            var emMeses = IntervaloTipoBox.SelectedIndex != 1;

            var generated = PurchaseFinanceHelper.GenerateParcelas(
                _total, entrada, qtd, PrimeiroVencBox.Text.Trim(), intervalo, emMeses);

            _parcelas.Clear();
            foreach (var p in generated)
            {
                _parcelas.Add(new PurchaseParcelaRow
                {
                    Vencimento = p.Vencimento,
                    Tipo = PurchaseFinanceHelper.NormalizeTipoCobranca(p.Tipo),
                    Valor = p.Valor,
                });
            }
            SyncParcelasTotal();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Parcelas", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SyncParcelasTotal()
    {
        var soma = _parcelas.Sum(p => p.Valor);
        ParcelasTotalText.Text = ProductPriceHelper.FormatBr(soma);
    }

    private void SyncFromGrid()
    {
        foreach (var row in _parcelas)
            row.Tipo = PurchaseFinanceHelper.NormalizeTipoCobranca(row.Tipo);
        SyncParcelasTotal();
    }

    private static int ParseInt(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) && n > 0 ? n : fallback;
    }

    private void EntradaBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;
        EntradaBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(EntradaBox.Text));
        RecalcResumo();
    }

    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            GerarParcelas();
            e.Handled = true;
        }
    }

    private void IntervaloTipoBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _parcelas.Count == 0)
            return;
        GerarParcelas();
    }

    private void ParcelasGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(SyncFromGrid);
    }

    private void Gerar_Click(object sender, RoutedEventArgs e) => GerarParcelas();

    private void Concluir_Click(object sender, RoutedEventArgs e)
    {
        if (_parcelas.Count == 0)
            GerarParcelas();

        SyncFromGrid();

        foreach (var p in _parcelas)
        {
            if (string.IsNullOrEmpty(DateBrHelper.ToIso(p.Vencimento)))
            {
                MessageBox.Show($"Data de vencimento inválida: {p.Vencimento}", "Parcelas",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var soma = _parcelas.Sum(p => p.Valor);
        if (Math.Abs(soma - _total) > 0.02)
        {
            MessageBox.Show(
                $"Total das parcelas ({ProductPriceHelper.FormatBr(soma)}) deve ser igual ao total da nota ({ProductPriceHelper.FormatBr(_total)}).",
                "Parcelas",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Result = new PurchaseFinanceiroMeta
        {
            Entrada = ProductPriceHelper.ParseBr(EntradaBox.Text),
            Qtd = _parcelas.Count,
            Parcelas = _parcelas.Select(p => new PurchaseParcelaDraft
            {
                Vencimento = p.Vencimento,
                Tipo = p.Tipo,
                Valor = p.Valor,
            }).ToList(),
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (e.Key == Key.Escape))
        {
            Cancel_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12)
        {
            Concluir_Click(sender, e);
            e.Handled = true;
        }
    }
}
