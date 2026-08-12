using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Services;

namespace SGDB.Views;

public partial class PdvResumoDiaWindow : Window
{
    public PdvResumoDiaWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        var resumo = PdvQueryService.GetResumoDia();

        TitleText.Text = resumo.SessionDate.Contains(" a ", StringComparison.Ordinal)
            ? $"Resumo do turno — {resumo.SessionDate}"
            : $"Resumo do dia — {resumo.SessionDate}";

        CaixaInfoText.Text = resumo.CaixaOpen
            ? $"Caixa aberto desde {resumo.CaixaAbertoDesde}"
            : "Caixa não aberto";
        CaixaIcon.Text = resumo.CaixaOpen ? "🟢" : "⚪";
        CaixaBadge.Background = new SolidColorBrush(
            resumo.CaixaOpen ? Color.FromRgb(0xEA, 0xF4, 0xEC) : Color.FromRgb(0xF1, 0xF3, 0xF6));
        CaixaBadge.BorderBrush = new SolidColorBrush(
            resumo.CaixaOpen ? Color.FromRgb(0xBF, 0xDC, 0xC6) : Color.FromRgb(0xD5, 0xDB, 0xE2));

        CaixaInicialText.Text = resumo.EntradaCaixaDisplay;
        CaixaEntradasText.Text = resumo.EntradasCaixaDisplay;
        CaixaSaidasText.Text = resumo.SaidasCaixaDisplay;
        CaixaGavetaText.Text = resumo.SaldoGavetaDisplay;

        var periodoHint = resumo.SessionDate.Contains(" a ", StringComparison.Ordinal) ? "no turno" : "no dia";
        KpiFaturamentoText.Text = resumo.FaturamentoDisplay;
        KpiFaturamentoHint.Text = resumo.QtdVendas == 1
            ? $"1 venda {periodoHint}"
            : $"{resumo.QtdVendas} vendas {periodoHint}";
        KpiLucroText.Text = resumo.LucroRealDisplay;
        KpiMargemText.Text = $"{resumo.MargemDisplay}%";
        KpiVendasText.Text = resumo.QtdVendas.ToString();
        KpiCanceladasHint.Text = resumo.QtdCancelados switch
        {
            0 => "nenhuma cancelada",
            1 => "1 cancelada",
            _ => $"{resumo.QtdCancelados} canceladas",
        };
        KpiTicketText.Text = resumo.TicketMedioDisplay;

        KpiFiadoText.Text = resumo.FiadoTotalDisplay;
        KpiFiadoHint.Text = resumo.FiadoCount switch
        {
            0 => "nada a prazo",
            1 => "1 venda na promissória",
            _ => $"{resumo.FiadoCount} vendas na promissória",
        };
        FiadoCard.Opacity = resumo.FiadoCount == 0 ? 0.55 : 1;

        GruposGrid.ItemsSource = resumo.Grupos;
        FormasGrid.ItemsSource = resumo.Formas;
        TopGrid.ItemsSource = resumo.TopProdutos;

        MetaText.Text =
            "Lucro = vendas − custo dos produtos · Fiado não entra no caixa em dinheiro.";
        FooterHintText.Text = $"Atualizado às {DateTime.Now:HH:mm:ss}";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadData();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ConsultarVendas_Click(object sender, RoutedEventArgs e) => OpenConsultarVendas();

    private void OpenConsultarVendas()
    {
        try
        {
            var w = new PdvVendasConsultaWindow { Owner = this };
            w.ShowDialog();
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Consultar vendas", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.F9)
        {
            OpenConsultarVendas();
            e.Handled = true;
        }
        else if (e.Key == Key.F1 || e.Key == Key.F5)
        {
            LoadData();
            e.Handled = true;
        }
    }
}
