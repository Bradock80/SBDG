using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class DreReportModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private DreSimplificadoResult? _last;

    public DreReportModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            ApplyEsteMes();
            LoadDre();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            LoadDre();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void EsteMes_Click(object sender, RoutedEventArgs e)
    {
        ApplyEsteMes();
        LoadDre();
    }

    private void MesAnterior_Click(object sender, RoutedEventArgs e)
    {
        var firstThis = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var lastPrev = firstThis.AddDays(-1);
        var firstPrev = new DateTime(lastPrev.Year, lastPrev.Month, 1);
        DateFrom.SetDate(firstPrev);
        DateTo.SetDate(lastPrev);
        LoadDre();
    }

    private void Ultimos30_Click(object sender, RoutedEventArgs e)
    {
        DateFrom.SetDate(DateTime.Today.AddDays(-29));
        DateTo.SetDate(DateTime.Today);
        LoadDre();
    }

    private void Pesquisar_Click(object sender, RoutedEventArgs e) => LoadDre();

    private void ApplyEsteMes()
    {
        DateFrom.SetDate(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        DateTo.SetDate(DateTime.Today);
    }

    private void LoadDre()
    {
        if (!DateFrom.TryGetDate(out var from) || !DateTo.TryGetDate(out var to))
        {
            MessageBox.Show("Selecione as datas inicial e final.", "DRE Simplificado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var r = DreService.GetDre(from, to);
            _last = r;
            BindResult(r);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "DRE Simplificado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BindResult(DreSimplificadoResult r)
    {
        PeriodoText.Text = $"Período {r.PeriodoDisplay}";
        CardReceita.Text = $"R$ {r.ReceitaLiquida:N2}";
        CardDespesas.Text = $"R$ {r.DespesasOperacionais:N2}";
        CardLucro.Text = $"R$ {r.LucroLiquido:N2}";

        if (r.LucroLiquido < -0.009)
        {
            CardLucroLabel.Text = "Prejuízo";
            CardLucroLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
            CardLucro.Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
            CardLucroBorder.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            CardLucroBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA));
        }
        else
        {
            CardLucroLabel.Text = "Lucro Líquido";
            CardLucroLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
            CardLucro.Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x53, 0x2D));
            CardLucroBorder.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4));
            CardLucroBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xF7, 0xD0));
        }

        CascadeList.ItemsSource = r.CascadeLines;
        DespesasGrid.ItemsSource = r.DespesasPorCategoria;

        MetaText.Text =
            $"{r.QtdVendas} venda(s) ativa(s)" +
            (r.QtdCanceladas > 0 ? $" · {r.QtdCanceladas} cancelada(s)" : "") +
            $" · Margem bruta {r.MargemBrutaPercent:N1}% · Margem líquida {r.MargemLiquidaPercent:N1}%." +
            " CMV usa o custo cadastrado atual dos produtos.";
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_last is null)
        {
            MessageBox.Show("Pesquise o período antes de imprimir.", "DRE Simplificado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            PrintDre(_last);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Imprimir DRE", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void PrintDre(DreSimplificadoResult r)
    {
        var company = AppSettingsService.GetNomeDeposito();
        var children = new StackPanel();
        children.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(company) ? "SGDB" : company.ToUpperInvariant(),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2),
        });
        children.Children.Add(new TextBlock
        {
            Text = "DRE Simplificado — Demonstrativo do Resultado",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        children.Children.Add(new TextBlock
        {
            Text = $"Período: {r.PeriodoDisplay}  ·  Emitido em {DateTime.Now:dd/MM/yyyy HH:mm}",
            FontSize = 12,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 16),
        });

        children.Children.Add(MakePrintLine("Receita Líquida", $"R$ {r.ReceitaLiquida:N2}", bold: true));
        children.Children.Add(MakePrintLine("Total de Despesas", $"R$ {r.DespesasOperacionais:N2}"));
        children.Children.Add(MakePrintLine(
            r.LucroLiquido >= 0 ? "Lucro Líquido" : "Prejuízo",
            $"R$ {r.LucroLiquido:N2}", bold: true));
        children.Children.Add(new TextBlock { Height = 12 });

        foreach (var line in r.CascadeLines.Where(l => !l.IsSubNote))
        {
            var prefix = string.IsNullOrEmpty(line.Sign) ? "" : $"{line.Sign} ";
            children.Children.Add(MakePrintLine($"{prefix}{line.Label}", line.AmountDisplay, line.IsTotal));
        }

        children.Children.Add(new TextBlock
        {
            Text = $"Margem Bruta: {r.MargemBrutaPercent:N1}%   ·   Margem Líquida: {r.MargemLiquidaPercent:N1}%",
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 8),
        });

        if (r.DespesasPorCategoria.Count > 0)
        {
            children.Children.Add(new TextBlock
            {
                Text = "Despesas por categoria",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 6),
            });
            foreach (var row in r.DespesasPorCategoria)
                children.Children.Add(MakePrintLine(row.Category, row.AmountDisplay));
        }

        children.Children.Add(new TextBlock
        {
            Text = "* Despesas: Contas a Pagar por vencimento (sem mercadoria). CMV pelo custo cadastrado.",
            FontSize = 10,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0),
        });

        var paper = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(28),
            Width = 560,
            Child = children,
        };

        paper.Measure(new Size(560, double.PositiveInfinity));
        paper.Arrange(new Rect(0, 0, 560, paper.DesiredSize.Height));
        paper.UpdateLayout();

        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true)
            return;
        dlg.PrintVisual(paper, $"DRE {r.PeriodoDisplay}");
    }

    private static TextBlock MakePrintLine(string label, string value, bool bold = false) =>
        new()
        {
            Text = $"{label}:  {value}",
            FontSize = 12,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 5),
            TextWrapping = TextWrapping.Wrap,
        };
}
