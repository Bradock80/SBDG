using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class SellersModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private string _ativo = "ativos";
    private int? _editingId;
    private bool _suppressSelect;

    public SellersModuleView()
    {
        InitializeComponent();
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            Reload();
        };
        Loaded += (_, _) =>
        {
            Focus();
            ClearForm(suggestCode: true);
            Reload();
        };
    }

    private Seller? Selected => SellersGrid.SelectedItem as Seller;

    private void Reload()
    {
        _suppressSelect = true;
        SellersGrid.ItemsSource = SellersService.List(SearchBox.Text, _ativo);
        _suppressSelect = false;
    }

    private void ClearForm(bool suggestCode = false)
    {
        _editingId = null;
        FormTitle.Text = "Novo vendedor";
        FormHint.Text = "Cadastro novo — informe código e nome, depois clique em Gravar (F5).";
        CodeBox.Text = suggestCode ? SuggestNextCode() : "";
        NameBox.Text = "";
        PhoneBox.Text = "";
        CpfBox.Text = "";
        CommBox.Text = "0,00";
        NotesBox.Text = "";
        ActiveBox.IsChecked = true;
        DeleteBtn.Visibility = Visibility.Collapsed;
        _suppressSelect = true;
        SellersGrid.SelectedItem = null;
        _suppressSelect = false;
        CodeBox.Focus();
    }

    private static string SuggestNextCode()
    {
        try
        {
            var nums = SellersService.List(ativo: "todos")
                .Select(s => int.TryParse(s.Code, out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();
            return (nums + 1).ToString("00");
        }
        catch
        {
            return "01";
        }
    }

    private void BindForm(Seller s)
    {
        _editingId = s.Id;
        FormTitle.Text = "Alterar vendedor";
        FormHint.Text = $"Editando {s.Code} — {s.Name}. Altere os campos e clique em Gravar (F5).";
        CodeBox.Text = s.Code;
        NameBox.Text = s.Name;
        PhoneBox.Text = s.Phone ?? "";
        CpfBox.Text = s.Cpf ?? "";
        CommBox.Text = s.CommissionPercent.ToString("N2");
        NotesBox.Text = s.Notes ?? "";
        ActiveBox.IsChecked = s.Active;
        DeleteBtn.Visibility = Visibility.Visible;
    }

    private SellerInput BuildInput() => new()
    {
        Code = CodeBox.Text,
        Name = NameBox.Text,
        Phone = PhoneBox.Text,
        Cpf = CpfBox.Text,
        CommissionPercent = ProductPriceHelper.ParseBr(CommBox.Text),
        Notes = NotesBox.Text,
        Active = ActiveBox.IsChecked == true,
    };

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _ativo = FilterInativos.IsChecked == true ? "inativos"
            : FilterTodos.IsChecked == true ? "todos" : "ativos";
        Reload();
    }

    private void SellersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelect || Selected is null) return;
        BindForm(Selected);
    }

    private void New_Click(object sender, RoutedEventArgs e) => ClearForm(suggestCode: true);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = BuildInput();
            var saved = _editingId is null
                ? SellersService.Create(input)
                : SellersService.Update(_editingId.Value, input);
            Reload();
            _suppressSelect = true;
            SellersGrid.SelectedItem = SellersService.List(SearchBox.Text, _ativo).FirstOrDefault(x => x.Id == saved.Id);
            _suppressSelect = false;
            BindForm(saved);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vendedores", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId is null)
        {
            MessageBox.Show("Selecione um vendedor.", "Vendedores", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = NameBox.Text;
        if (MessageBox.Show($"Excluir o vendedor \"{name}\"?", "Vendedores",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            SellersService.Delete(_editingId.Value);
            ClearForm(suggestCode: true);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vendedores", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { New_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F5) { Save_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F12) { SearchBox.Focus(); e.Handled = true; }
        else if (e.Key == Key.Delete && !(Keyboard.FocusedElement is TextBox) && _editingId is not null)
        {
            Delete_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
}
