using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class CashModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private CashOperacaoView? _view;

    public CashModuleView()
    {
        InitializeComponent();
        InputUxHelper.Attach(this);
        Loaded += (_, _) =>
        {
            Focus();
            RefreshView();
        };
    }

    private void RefreshView()
    {
        try
        {
            _view = CashService.GetOperacaoView();
            BindView(_view);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Caixa", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BindView(CashOperacaoView view)
    {
        if (view.IsOperational)
        {
            FechadoPanel.Visibility = Visibility.Collapsed;
            AbertoPanel.Visibility = Visibility.Visible;
            StatusBar.Background = new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
            StatusText.Text = string.IsNullOrWhiteSpace(view.StatusMessage)
                ? "Caixa aberto"
                : view.StatusMessage;

            HeaderSaldoText.Text = $"R$ {ProductPriceHelper.FormatBr(view.SaldoInicial)}";
            HeaderObsText.Text = string.IsNullOrWhiteSpace(view.OpeningObs) ? "—" : view.OpeningObs;
            HeaderAbertoText.Text = view.CarriedOver
                ? $"Aberto em {view.OpenedAtBr} às {view.OpenedTimeBr} — permanece até F3"
                : $"Aberto às {view.OpenedTimeBr} — {view.OpenedAtBr}";

            MovimentosGrid.ItemsSource = view.Rows;
            BuildResumoPanel(view);
        }
        else
        {
            FechadoPanel.Visibility = Visibility.Visible;
            AbertoPanel.Visibility = Visibility.Collapsed;
            StatusBar.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));

            if (view.IsClosed && !string.IsNullOrEmpty(view.ClosedAtBr))
            {
                StatusText.Text = $"Caixa fechado — encerrado {view.ClosedAtBr}";
                FechadoHintText.Text = $"Fechado às {view.ClosedAtBr}. Informe o troco e pressione F2 para abrir de novo.";
            }
            else
            {
                StatusText.Text = "Caixa fechado";
                FechadoHintText.Text = "Não há entradas ou saídas. (Caixa fechado).";
            }
        }
    }

    private void BuildResumoPanel(CashOperacaoView view)
    {
        ResumoPanel.Children.Clear();

        AddResumoLine(ResumoPanel, "(+) SALDO INICIAL", view.SaldoInicial);

        var vendasBlock = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        vendasBlock.Children.Add(MakeTitle("VENDAS HOJE (PDV)"));
        vendasBlock.Children.Add(MakeTotal(view.VendasDiaPdv, "#166534"));
        ResumoPanel.Children.Add(vendasBlock);

        var entradasBlock = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        entradasBlock.Children.Add(MakeTitle("(+) ENTRADAS NO CAIXA"));
        if (view.EntradasPorForma.Count == 0)
            entradasBlock.Children.Add(MakeHint("Não há registros de entrada"));
        else
        {
            foreach (var kv in view.EntradasPorForma)
                entradasBlock.Children.Add(MakeFormaLine(kv.Key, kv.Value));
        }
        entradasBlock.Children.Add(MakeTotal(view.EntradasCaixa, "#166534"));
        ResumoPanel.Children.Add(entradasBlock);

        var saidasBlock = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        saidasBlock.Children.Add(MakeTitle("(-) SAÍDAS DO CAIXA"));
        if (view.SaidasCaixa > 0)
            saidasBlock.Children.Add(MakeTotal(view.SaidasCaixa, "#DC2626"));
        else
            saidasBlock.Children.Add(MakeHint("Não há registros de saída"));
        ResumoPanel.Children.Add(saidasBlock);

        var finalBlock = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        finalBlock.Children.Add(MakeTitle("(=) SALDO FINAL"));
        finalBlock.Children.Add(MakeFormaLine("Dinheiro na gaveta", view.SaldoFinalGaveta));
        finalBlock.Children.Add(MakeFormaLine("Total geral", view.SaldoFinal, bold: true));
        finalBlock.Children.Add(MakeHint("Total geral = Saldo inicial + Entradas − Saídas"));
        ResumoPanel.Children.Add(finalBlock);
    }

    private static TextBlock MakeTitle(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x71, 0x6C)),
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static TextBlock MakeHint(string text) => new()
    {
        Text = text,
        FontSize = 10,
        Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2),
    };

    private static TextBlock MakeTotal(double value, string colorHex) => new()
    {
        Text = $"R$ {ProductPriceHelper.FormatBr(value)}",
        FontWeight = FontWeights.Bold,
        FontSize = 13,
        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)!),
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static Grid MakeFormaLine(string label, double value, bool bold = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        var right = new TextBlock
        {
            Text = $"R$ {ProductPriceHelper.FormatBr(value)}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static void AddResumoLine(Panel parent, string label, double value)
    {
        parent.Children.Add(MakeFormaLine(label, value, bold: true));
    }

    private void AbrirCaixa_Click(object sender, RoutedEventArgs e) => DoAbrirCaixa();

    private void DoAbrirCaixa()
    {
        var valor = ProductPriceHelper.ParseBr(AberturaValorBox.Text);
        if (valor < 0)
        {
            MessageBox.Show("Saldo inicial inválido.", "Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            CashService.OpenSession(valor, AberturaObsBox.Text.Trim());
            RefreshView();
        }
        catch (CashOperationException ex)
        {
            MessageBox.Show(ex.Message, "Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AdicionarLancamento_Click(object sender, RoutedEventArgs e) => ShowLancamentoChooser();

    private void ShowLancamentoChooser()
    {
        var owner = Window.GetWindow(this);
        var dlg = new CashLancamentoWindow { Owner = owner };
        if (dlg.ShowDialog() == true)
            RefreshView();
    }

    private void FecharCaixa_Click(object sender, RoutedEventArgs e) => DoFecharCaixa();

    private void DoFecharCaixa()
    {
        if (_view is null || !_view.IsOperational)
            return;

        var owner = Window.GetWindow(this);
        var dlg = new CashEncerrarWindow(_view) { Owner = owner };
        if (dlg.ShowDialog() == true)
            RefreshView();
    }

    private void ExcluirMovimento_Click(object sender, RoutedEventArgs e) => DoExcluirMovimento();

    private void DoExcluirMovimento()
    {
        if (MovimentosGrid.SelectedItem is not CashMovementRow row)
        {
            MessageBox.Show("Selecione um lançamento na lista.", "Caixa", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!row.Deletable)
        {
            MessageBox.Show("Este lançamento não pode ser excluído aqui.", "Caixa", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Excluir o lançamento \"{row.Historico}\"?",
            "Excluir lançamento",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            CashService.DeleteMovement(row.Id);
            RefreshView();
        }
        catch (CashOperationException ex)
        {
            MessageBox.Show(ex.Message, "Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PrintMov_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Impressão em desenvolvimento.", "Caixa", MessageBoxButton.OK, MessageBoxImage.Information);

    private void PrintResumo_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Impressão em desenvolvimento.", "Caixa", MessageBoxButton.OK, MessageBoxImage.Information);

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void CashModuleView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_view?.IsOperational == true)
        {
            if (e.Key == Key.F3)
            {
                DoFecharCaixa();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Delete)
            {
                DoExcluirMovimento();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F2)
        {
            DoAbrirCaixa();
            e.Handled = true;
        }
    }
}
