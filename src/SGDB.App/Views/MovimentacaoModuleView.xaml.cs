using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Services;

namespace SGDB.Views;

public partial class MovimentacaoModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private string _tab = "produtos";
    private string _tipo = "todas"; // todas | vendas | compras
    private bool _loadedOnce;
    private bool _suppressTipoChange;

    private static readonly (string Id, string Label)[] TipoOptions =
    [
        ("todas", "Todas as Movimentações"),
        ("vendas", "Apenas Vendas"),
        ("compras", "Apenas Compras"),
    ];

    public MovimentacaoModuleView(string? initialTipo = null)
    {
        InitializeComponent();
        _tipo = NormalizeTipo(initialTipo);
        Loaded += (_, _) =>
        {
            if (_loadedOnce) return;
            _loadedOnce = true;
            InitFilters();
            ApplyTipoUi(reload: false);
            // Já busca ao abrir — evita tela vazia sem o usuário perceber
            LoadData();
            Focus();
        };
    }

    private static string NormalizeTipo(string? tipo)
    {
        var t = (tipo ?? "todas").Trim().ToLowerInvariant();
        return t is "vendas" or "compras" ? t : "todas";
    }

    private void InitFilters()
    {
        // Padrão: hoje (venda do dia aparece na hora)
        DateFromBox.SelectedDate = DateTime.Today;
        DateToBox.SelectedDate = DateTime.Today;
        UpdatePeriodHint();

        _suppressTipoChange = true;
        TipoBox.ItemsSource = TipoOptions.Select(t => t.Label).ToList();
        TipoBox.SelectedIndex = Array.FindIndex(TipoOptions, t => t.Id == _tipo);
        if (TipoBox.SelectedIndex < 0) TipoBox.SelectedIndex = 0;
        _suppressTipoChange = false;

        var forms = new List<string> { "TODAS" };
        forms.AddRange(PaymentMethodsService.List()
            .Where(m => m.Active)
            .Select(m => m.ApiLabel));
        PaymentBox.ItemsSource = forms;
        PaymentBox.SelectedIndex = 0;
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void TipoBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTipoChange || !_loadedOnce)
            return;
        var idx = TipoBox.SelectedIndex;
        if (idx < 0 || idx >= TipoOptions.Length)
            return;
        _tipo = TipoOptions[idx].Id;
        ApplyTipoUi(reload: true);
    }

    private void ApplyTipoUi(bool reload)
    {
        var showVendas = _tipo is "todas" or "vendas";
        var showCompras = _tipo is "todas" or "compras";

        TabProdutos.Visibility = showVendas ? Visibility.Visible : Visibility.Collapsed;
        TabVendas.Visibility = showVendas ? Visibility.Visible : Visibility.Collapsed;
        TabCompras.Visibility = showCompras ? Visibility.Visible : Visibility.Collapsed;
        PaymentLabel.Visibility = showVendas ? Visibility.Visible : Visibility.Collapsed;
        PaymentBox.Visibility = showVendas ? Visibility.Visible : Visibility.Collapsed;

        if (_tipo == "compras")
            SetTab("compras");
        else if (_tab == "compras" && !showCompras)
            SetTab("produtos");
        else if ((_tab is "produtos" or "vendas") && !showVendas)
            SetTab("compras");
        else
            SetTab(_tab);

        if (reload)
            LoadData();
    }

    private void Hoje_Click(object sender, RoutedEventArgs e)
    {
        DateFromBox.SelectedDate = DateTime.Today;
        DateToBox.SelectedDate = DateTime.Today;
        UpdatePeriodHint();
        LoadData();
    }

    private void Ontem_Click(object sender, RoutedEventArgs e)
    {
        var y = DateTime.Today.AddDays(-1);
        DateFromBox.SelectedDate = y;
        DateToBox.SelectedDate = y;
        UpdatePeriodHint();
        LoadData();
    }

    private void Buscar_Click(object sender, RoutedEventArgs e) => LoadData();

    private void Limpar_Click(object sender, RoutedEventArgs e)
    {
        DateFromBox.SelectedDate = DateTime.Today;
        DateToBox.SelectedDate = DateTime.Today;
        PaymentBox.SelectedIndex = 0;
        ProdutosGrid.ItemsSource = null;
        VendasGrid.ItemsSource = null;
        ComprasGrid.ItemsSource = null;
        TotalVendidoText.Text = "—";
        TotalLucroText.Text = "—";
        TotalCustoText.Text = "—";
        FootText.Text = "Informe o período e pressione F7.";
        UpdatePeriodHint();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender == TabProdutos)
            SetTab("produtos");
        else if (sender == TabVendas)
            SetTab("vendas");
        else if (sender == TabCompras)
            SetTab("compras");
        LoadData();
    }

    private void SetTab(string tab)
    {
        _tab = tab;
        TabProdutos.Tag = tab == "produtos" ? "active" : "";
        TabVendas.Tag = tab == "vendas" ? "active" : "";
        TabCompras.Tag = tab == "compras" ? "active" : "";

        ProdutosGrid.Visibility = tab == "produtos" ? Visibility.Visible : Visibility.Collapsed;
        VendasGrid.Visibility = tab == "vendas" ? Visibility.Visible : Visibility.Collapsed;
        ComprasGrid.Visibility = tab == "compras" ? Visibility.Visible : Visibility.Collapsed;

        if (tab == "compras")
        {
            TotalVendidoLabel.Text = "Total compras";
            TotalLucroLabel.Text = "Qtd. compras";
        }
        else if (tab == "vendas")
        {
            TotalVendidoLabel.Text = "Total vendido (faturamento)";
            TotalLucroLabel.Text = "Total lucro líquido";
        }
        else
        {
            TotalVendidoLabel.Text = "Total vendido (faturamento)";
            TotalLucroLabel.Text = "Total lucro bruto";
        }
    }

    private void LoadData()
    {
        UpdatePeriodHint();
        if (!DateFromBox.TryGetDate(out var from) || !DateToBox.TryGetDate(out var to))
        {
            MessageBox.Show("Informe a data inicial e a data final.", "Consultar Movimentação",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var payment = PaymentBox.SelectedItem as string;
        if (string.Equals(payment, "TODAS", StringComparison.OrdinalIgnoreCase))
            payment = null;

        FootText.Text = StoreNetworkMode.IsClient
            ? $"Consultando loja {StoreNetworkMode.GetClientHost()}:{StoreNetworkMode.GetPort()}…"
            : "Consultando banco local…";

        try
        {
            if (_tab == "compras")
            {
                var result = MovimentacaoService.ListCompras(from, to);
                ComprasGrid.ItemsSource = result.Compras;
                ApplyTotals(result);
                if (result.Registros > 0 && (result.Compras is null || result.Compras.Count == 0))
                    FootText.Text += " · atualize o SGDB na loja (lista vazia)";
            }
            else if (_tab == "vendas")
            {
                var result = MovimentacaoService.ListVendas(from, to, payment);
                VendasGrid.ItemsSource = result.Vendas;
                ApplyTotals(result);
                if (result.Registros > 0 && (result.Vendas is null || result.Vendas.Count == 0))
                    FootText.Text += " · atualize o SGDB na loja (lista vazia)";
            }
            else
            {
                var result = MovimentacaoService.ListProdutos(from, to, payment);
                ProdutosGrid.ItemsSource = result.Produtos;
                ApplyTotals(result);
                // Bug antigo: totais vinham, mas a lista de produtos ficava vazia
                if (result.TotalVendas > 0 && (result.Produtos is null || result.Produtos.Count == 0))
                {
                    FootText.Text += " · atualize o SGDB na LOJA e religue o servidor";
                    MessageBox.Show(
                        "Os totais chegaram da loja, mas a lista de produtos veio vazia.\n\n" +
                        "Atualize o SGDB no PC da LOJA com a pasta nova, ligue de novo Rede Loja → Servidor, " +
                        "e clique em Buscar (F7) aqui.",
                        "Consultar Movimentação",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            ProdutosGrid.ItemsSource = null;
            VendasGrid.ItemsSource = null;
            ComprasGrid.ItemsSource = null;
            FootText.Text = StoreNetworkMode.IsClient
                ? "Falha ao consultar a loja — veja o aviso."
                : "Falha na consulta.";
            MessageBox.Show(
                ex.Message + (StoreNetworkMode.IsClient
                    ? "\n\nConfira: título com [Cliente], servidor Ligado na loja, e a mesma versão do SGDB nos 2 PCs."
                    : "\n\nSe este for o notebook, configure Sistema → Rede Loja → Cliente."),
                "Consultar Movimentação",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyTotals(Models.MovimentacaoResult result)
    {
        if (_tab == "compras")
        {
            TotalVendidoText.Text = $"R$ {result.TotalCompras:N2}";
            TotalLucroText.Text = $"{result.TotalComprasCount}";
            TotalCustoText.Text = "—";
            var foot = $"{result.Registros} compra(s)";
            if (result.Truncated)
                foot += $" de {result.TotalRegistros}";
            if (StoreNetworkMode.IsClient)
                foot += " · dados do PC da loja";
            FootText.Text = foot;
            return;
        }

        TotalVendidoText.Text = $"R$ {result.TotalFaturamento:N2}";
        TotalLucroText.Text = _tab == "vendas"
            ? $"R$ {result.TotalLucro:N2}"
            : $"R$ {result.TotalLucroBruto:N2}";
        TotalCustoText.Text = $"R$ {result.TotalCusto:N2}";

        var footV = $"{result.Registros} registro(s)";
        if (result.Truncated)
            footV += $" de {result.TotalRegistros}";
        footV += $" · {result.TotalVendas} venda(s)";
        if (result.TotalTaxa > 0.009)
            footV += $" · Taxas R$ {result.TotalTaxa:N2}";
        if (StoreNetworkMode.IsClient)
            footV += " · dados do PC da loja";
        FootText.Text = footV;
    }

    private void UpdatePeriodHint()
    {
        var from = DateFromBox.SelectedDate?.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) ?? "—";
        var to = DateToBox.SelectedDate?.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) ?? "—";
        var tipoLabel = TipoOptions.FirstOrDefault(t => t.Id == _tipo).Label ?? "Todas";
        var rede = StoreNetworkMode.IsClient
            ? $" · Rede: loja {StoreNetworkMode.GetClientHost()}:{StoreNetworkMode.GetPort()}"
            : " · Banco local deste PC";
        PeriodHintText.Text = $"Período: {from} até {to} · {tipoLabel}{rede}";
    }

    private void MovimentacaoModuleView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F7)
        {
            LoadData();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F12)
        {
            TipoBox.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
