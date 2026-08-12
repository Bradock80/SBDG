using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class ClientsModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private string _ativoFilter = "ativos";
    private string _tipoFilter = "clientes";

    public ClientsModuleView()
    {
        InitializeComponent();
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            LoadPeople();
        };
        Loaded += (_, _) =>
        {
            Focus();
            ApplyEditPermissionUi();
            LoadPeople();
        };
    }

    private void ApplyEditPermissionUi()
    {
        var canEdit = AccessControl.Can("ClientesEditar");
        BtnNovo.IsEnabled = canEdit;
        BtnAlterar.IsEnabled = canEdit;
        BtnExcluir.IsEnabled = canEdit;
        BtnNovo.Opacity = canEdit ? 1 : 0.4;
        BtnAlterar.Opacity = canEdit ? 1 : 0.4;
        BtnExcluir.Opacity = canEdit ? 1 : 0.4;
        if (!canEdit)
        {
            BtnNovo.ToolTip = "Sem permissão para cadastrar";
            BtnAlterar.ToolTip = "Sem permissão para alterar";
            BtnExcluir.ToolTip = "Sem permissão para excluir";
        }
    }

    private void LoadPeople()
    {
        PeopleGrid.ItemsSource = PersonService.List(SearchBox.Text, _ativoFilter, _tipoFilter);
    }

    private Person? SelectedPerson => PeopleGrid.SelectedItem as Person;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        _ativoFilter = FilterInativos.IsChecked == true ? "inativos"
            : FilterTodos.IsChecked == true ? "todos"
            : "ativos";
        LoadPeople();
    }

    private void Tipo_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        _tipoFilter = TipoTodos.IsChecked == true ? "todos" : "clientes";
        LoadPeople();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadPeople();

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        TipoClientes.IsChecked = true;
        FilterAtivos.IsChecked = true;
        LoadPeople();
    }

    private void NewPerson_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("ClientesEditar", "cadastrar e alterar clientes", Window.GetWindow(this)))
            return;
        OpenForm(null);
    }

    private void EditPerson_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPerson is null)
        {
            MessageBox.Show("Selecione um cliente na lista.", "Clientes", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!AccessControl.Ensure("ClientesEditar", "cadastrar e alterar clientes", Window.GetWindow(this)))
            return;
        OpenForm(SelectedPerson.Id);
    }

    private void PeopleGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        EditPerson_Click(sender, e);

    private void DeletePerson_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPerson is null)
        {
            MessageBox.Show("Selecione um cliente para excluir.", "Clientes", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!AccessControl.Ensure("ClientesEditar", "cadastrar e alterar clientes", Window.GetWindow(this)))
            return;

        var confirm = MessageBox.Show(
            $"Inativar \"{SelectedPerson.Name}\"?",
            "Excluir cliente",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            PersonService.SoftDelete(SelectedPerson.Id);
            LoadPeople();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Clientes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Impressão da lista será implementada em breve.", "Clientes",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenForm(int? personId)
    {
        var form = new PersonFormWindow(personId) { Owner = Window.GetWindow(this) };
        if (form.ShowDialog() == true)
            LoadPeople();
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void ClientsModuleView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F2:
                NewPerson_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F3:
                EditPerson_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F4:
                Print_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F5:
                Refresh_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F6:
                SearchBox.Focus();
                e.Handled = true;
                break;
            case Key.F7:
                DeletePerson_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Enter when PeopleGrid.SelectedItem is not null:
                EditPerson_Click(sender, e);
                e.Handled = true;
                break;
        }
    }
}
