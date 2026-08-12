using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class ExpenseCategoriesModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private string _ativo = "ativos";
    private int? _editingId;
    private bool _suppressSelect;

    public ExpenseCategoriesModuleView()
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
            ClearForm();
            Reload();
        };
    }

    private ExpenseCategory? Selected => CategoriesGrid.SelectedItem as ExpenseCategory;

    private void Reload()
    {
        _suppressSelect = true;
        CategoriesGrid.ItemsSource = ExpenseCategoriesService.List(SearchBox.Text, _ativo);
        _suppressSelect = false;
    }

    private void ClearForm()
    {
        _editingId = null;
        FormTitle.Text = "Nova categoria";
        FormHint.Text = "Cadastro novo — informe o nome e clique em Gravar (F5).";
        NameBox.Text = "";
        OrderBox.Text = SuggestNextOrder().ToString();
        ActiveBox.IsChecked = true;
        DeleteBtn.Visibility = Visibility.Collapsed;
        _suppressSelect = true;
        CategoriesGrid.SelectedItem = null;
        _suppressSelect = false;
        NameBox.Focus();
    }

    private static int SuggestNextOrder()
    {
        try
        {
            var max = ExpenseCategoriesService.List(ativo: "todos")
                .Select(c => c.SortOrder)
                .DefaultIfEmpty(0)
                .Max();
            return max + 10;
        }
        catch
        {
            return 100;
        }
    }

    private void BindForm(ExpenseCategory c)
    {
        _editingId = c.Id;
        FormTitle.Text = "Alterar categoria";
        FormHint.Text = $"Editando \"{c.Name}\". Altere os campos e clique em Gravar (F5).";
        NameBox.Text = c.Name;
        OrderBox.Text = c.SortOrder.ToString();
        ActiveBox.IsChecked = c.Active;
        DeleteBtn.Visibility = Visibility.Visible;
    }

    private ExpenseCategoryInput BuildInput()
    {
        _ = int.TryParse(OrderBox.Text.Trim(), out var ord);
        return new ExpenseCategoryInput
        {
            Name = NameBox.Text,
            Active = ActiveBox.IsChecked == true,
            SortOrder = ord <= 0 ? 100 : ord,
        };
    }

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

    private void CategoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelect || Selected is null) return;
        BindForm(Selected);
    }

    private void New_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = BuildInput();
            var saved = _editingId is null
                ? ExpenseCategoriesService.Create(input)
                : ExpenseCategoriesService.Update(_editingId.Value, input);
            Reload();
            _suppressSelect = true;
            CategoriesGrid.SelectedItem = ExpenseCategoriesService.List(SearchBox.Text, _ativo)
                .FirstOrDefault(x => x.Id == saved.Id);
            _suppressSelect = false;
            BindForm(saved);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Categorias financeiras", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId is null)
        {
            MessageBox.Show("Selecione uma categoria.", "Categorias financeiras",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = NameBox.Text;
        if (MessageBox.Show(
                $"Excluir a categoria \"{name}\"?\n\nTítulos já gravados mantêm o texto da categoria.",
                "Categorias financeiras",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            ExpenseCategoriesService.Delete(_editingId.Value);
            ClearForm();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Categorias financeiras", MessageBoxButton.OK, MessageBoxImage.Warning);
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
