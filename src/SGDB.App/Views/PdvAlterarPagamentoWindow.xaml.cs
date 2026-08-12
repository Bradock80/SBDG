using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PdvAlterarPagamentoWindow : Window
{
    private readonly double _total;
    private readonly List<MethodLine> _lines = new();
    private int? _customerId;
    private int _focusIdx;

    public IReadOnlyList<PdvPaymentPart> Payments { get; private set; } = [];
    public double CashReceived { get; private set; }
    public int? CustomerPersonId { get; private set; }
    public bool Confirmed { get; private set; }

    public PdvAlterarPagamentoWindow(PdvSaleDetail sale, string? differenceNote = null)
    {
        _total = sale.Total;
        InitializeComponent();
        AtualText.Text = $"Venda #{sale.Id} — atual: {sale.PaymentLabel}";
        if (!string.IsNullOrWhiteSpace(differenceNote))
            AtualText.Text += "\n" + differenceNote.Trim();
        TotalText.Text = $"Total da venda: R$ {_total:N2}";

        CreateLine("A", "Dinheiro");
        CreateLine("B", "Cartão Débito");
        CreateLine("C", "Cartão Crédito");
        CreateLine("D", "Pix");
        CreateLine("E", "Fiado");

        SeedFromSale(sale);
        _customerId = sale.CustomerPersonId;
        if (_customerId is > 0 && !string.IsNullOrWhiteSpace(sale.CustomerName))
        {
            ClienteNomeText.Text = sale.CustomerName;
            ClienteBuscaBox.Text = sale.CustomerName;
        }

        UpdateTotals();
        Loaded += (_, _) => FocusLine(FirstPositiveIndex());
    }

    private void CreateLine(string tecla, string nome)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

        var teclaTb = new TextBlock
        {
            Text = tecla,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var nomeTb = new TextBlock
        {
            Text = nome,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var valorBox = new TextBox
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(8, 6, 8, 6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            BorderThickness = new Thickness(1),
        };
        valorBox.TextChanged += (_, _) => UpdateTotals();
        valorBox.GotKeyboardFocus += (_, _) =>
        {
            _focusIdx = _lines.FindIndex(l => l.ValorBox == valorBox);
            valorBox.SelectAll();
        };

        Grid.SetColumn(teclaTb, 0);
        Grid.SetColumn(nomeTb, 1);
        Grid.SetColumn(valorBox, 2);
        row.Children.Add(teclaTb);
        row.Children.Add(nomeTb);
        row.Children.Add(valorBox);
        MethodsPanel.Children.Add(row);

        _lines.Add(new MethodLine(tecla, nome, valorBox));
    }

    private void SeedFromSale(PdvSaleDetail sale)
    {
        foreach (var line in _lines)
            line.ValorBox.Text = "";

        var seeded = false;
        foreach (var p in sale.Payments)
        {
            var line = FindLine(p.PaymentType);
            if (line is null)
                continue;
            line.ValorBox.Text = Currency(p.Amount);
            seeded = true;
        }

        if (!seeded)
        {
            var line = FindLine(sale.PaymentType) ?? _lines[0];
            if (sale.PaymentType.Contains('+'))
                line = _lines[0];
            line.ValorBox.Text = Currency(_total);
        }

        if (sale.CashReceived is > 0)
            RecebidoBox.Text = Currency(sale.CashReceived.Value);
    }

    private MethodLine? FindLine(string paymentType)
    {
        var n = paymentType.Trim();
        return _lines.FirstOrDefault(l => l.Nome.Equals(n, StringComparison.OrdinalIgnoreCase))
            ?? n.ToLowerInvariant() switch
            {
                "din" or "dinheiro" => _lines[0],
                "deb" or "débito" or "debito" or "cartão débito" or "cartao debito" => _lines[1],
                "créd" or "cred" or "crédito" or "credito" or "cartão crédito" or "cartao credito" => _lines[2],
                "pix" => _lines[3],
                "fiado" => _lines[4],
                _ => null,
            };
    }

    private static string Currency(double v) => $"R$ {ProductPriceHelper.FormatBr(v)}";

    private double AmountOf(MethodLine line) => ProductPriceHelper.ParseBr(line.ValorBox.Text);

    private int FirstPositiveIndex()
    {
        var idx = _lines.FindIndex(l => AmountOf(l) > 0.009);
        return idx >= 0 ? idx : 0;
    }

    private List<PdvPaymentPart> BuildParts()
    {
        var parts = new List<PdvPaymentPart>();
        foreach (var line in _lines)
        {
            var amt = ProductPriceHelper.RoundPrice(AmountOf(line));
            if (amt > 0.009)
                parts.Add(new PdvPaymentPart { PaymentType = line.Nome, Amount = amt });
        }
        return parts;
    }

    private void UpdateTotals()
    {
        var parts = BuildParts();
        var sum = ProductPriceHelper.RoundPrice(parts.Sum(p => p.Amount));
        var restante = ProductPriceHelper.RoundPrice(_total - sum);
        var restanteTxt = restante > 0.009
            ? $"Restante R$ {restante:N2}"
            : restante < -0.009
                ? $"Excesso R$ {Math.Abs(restante):N2}"
                : "Fechado ✓";
        RestanteText.Text = $"Alocado R$ {sum:N2} · {restanteTxt}";
        RestanteText.Foreground = Math.Abs(restante) <= 0.02
            ? new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D))
            : new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));

        ClientePanel.Visibility = parts.Any(p => p.PaymentType == "Fiado")
            ? Visibility.Visible
            : Visibility.Collapsed;

        var dinheiro = parts.Where(p => p.PaymentType == "Dinheiro").Sum(p => p.Amount);
        var recv = ProductPriceHelper.ParseBr(RecebidoBox.Text);
        TrocoText.Text = dinheiro > 0.009 && recv > dinheiro + 0.009
            ? $"Troco R$ {ProductPriceHelper.RoundPrice(recv - dinheiro):N2}"
            : "";
    }

    private void FocusLine(int idx)
    {
        if (idx < 0 || idx >= _lines.Count)
            return;
        _focusIdx = idx;
        var box = _lines[idx].ValorBox;
        box.Focus();
        box.SelectAll();
    }

    /// <summary>A–E: foca a forma e preenche só o restante (não apaga as outras).</summary>
    private void SelectFillRemaining(int idx)
    {
        if (idx < 0 || idx >= _lines.Count)
            return;
        var line = _lines[idx];
        var others = ProductPriceHelper.RoundPrice(
            _lines.Where(l => l != line).Sum(AmountOf));
        var restante = ProductPriceHelper.RoundPrice(Math.Max(0, _total - others));
        if (restante > 0.009)
            line.ValorBox.Text = Currency(restante);
        FocusLine(idx);
        UpdateTotals();
    }

    /// <summary>F2: coloca o total inteiro nesta forma e zera as demais.</summary>
    private void SelectOnlyThis(int idx)
    {
        if (idx < 0 || idx >= _lines.Count)
            return;
        for (var i = 0; i < _lines.Count; i++)
            _lines[i].ValorBox.Text = i == idx ? Currency(_total) : "";
        FocusLine(idx);
        UpdateTotals();
    }

    private void RecebidoBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateTotals();

    private void ClienteBusca_TextChanged(object sender, TextChangedEventArgs e)
    {
        var term = ClienteBuscaBox.Text.Trim();
        if (term.Length < 1)
        {
            ClienteLookupGrid.Visibility = Visibility.Collapsed;
            return;
        }
        var list = PersonService.List(term, tipo: "clientes");
        ClienteLookupGrid.ItemsSource = list;
        ClienteLookupGrid.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (list.Count > 0)
            ClienteLookupGrid.SelectedIndex = 0;
    }

    private void ClienteLookup_MouseDoubleClick(object sender, MouseButtonEventArgs e) => PickCliente();

    private void PickCliente()
    {
        if (ClienteLookupGrid.SelectedItem is not Person p)
            return;
        _customerId = p.Id;
        ClienteNomeText.Text = p.Name;
        ClienteBuscaBox.Text = p.Name;
        ClienteLookupGrid.Visibility = Visibility.Collapsed;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => TryConfirm();
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TryConfirm()
    {
        var parts = BuildParts();
        if (parts.Count == 0)
        {
            MessageBox.Show("Informe ao menos uma forma de pagamento.", "Alterar pagamento",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var sum = ProductPriceHelper.RoundPrice(parts.Sum(p => p.Amount));
        if (Math.Abs(sum - _total) > 0.02)
        {
            MessageBox.Show(
                $"Soma (R$ {sum:N2}) difere do total (R$ {_total:N2}).\n" +
                "Ajuste os valores ou use A–E para preencher o restante.",
                "Alterar pagamento", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (parts.Any(p => p.PaymentType == "Fiado") && _customerId is null or <= 0)
        {
            MessageBox.Show("Selecione o cliente para fiado.", "Alterar pagamento",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ClienteBuscaBox.Focus();
            return;
        }

        Payments = parts;
        CashReceived = ProductPriceHelper.ParseBr(RecebidoBox.Text);
        CustomerPersonId = parts.Any(p => p.PaymentType == "Fiado") ? _customerId : null;
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            SelectOnlyThis(_focusIdx);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (ClienteLookupGrid.Visibility == Visibility.Visible && ClienteLookupGrid.SelectedItem is not null)
            {
                PickCliente();
                e.Handled = true;
                return;
            }

            // Enter na linha: se ainda falta valor, vai para a próxima vazia com o restante.
            var parts = BuildParts();
            var sum = ProductPriceHelper.RoundPrice(parts.Sum(p => p.Amount));
            if (Math.Abs(sum - _total) > 0.02)
            {
                var next = -1;
                for (var step = 1; step < _lines.Count; step++)
                {
                    var i = (_focusIdx + step) % _lines.Count;
                    if (AmountOf(_lines[i]) < 0.009)
                    {
                        next = i;
                        break;
                    }
                }
                if (next >= 0)
                {
                    SelectFillRemaining(next);
                    e.Handled = true;
                    return;
                }
            }

            TryConfirm();
            e.Handled = true;
            return;
        }

        if (e.Key is >= Key.A and <= Key.E)
        {
            // Não intercepta se estiver digitando em campo que não é valor das formas
            // (ex.: busca cliente). Nos campos de valor A–E escolhe a forma.
            if (Keyboard.FocusedElement == ClienteBuscaBox || ClienteLookupGrid.IsKeyboardFocusWithin)
                return;

            SelectFillRemaining(e.Key - Key.A);
            e.Handled = true;
        }
    }

    private sealed record MethodLine(string Tecla, string Nome, TextBox ValorBox);
}
