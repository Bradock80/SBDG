using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class UsersModuleView : UserControl
{
    public event EventHandler? CloseRequested;
    private int? _editId;
    private bool _suppress;
    private UserPermissions _permissions = UserPermissions.ForRole("vendedor");
    private readonly Dictionary<string, CheckBox> _permCheckboxes = new(StringComparer.Ordinal);

    private sealed class RoleOpt
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
    }

    public UsersModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RoleBox.ItemsSource = new[]
            {
                new RoleOpt { Id = "admin", Label = "Administrador" },
                new RoleOpt { Id = "gestor", Label = "Gestor" },
                new RoleOpt { Id = "vendedor", Label = "Vendedor" },
            };
            RoleBox.DisplayMemberPath = "Label";
            RoleBox.SelectedValuePath = "Id";
            RoleBox.SelectedValue = "vendedor";
            BuildPermChecks();
            ClearForm();
            UpdateSearchPlaceholder();
            Reload();
            Focus();
        };
    }

    private string AtivoFilter =>
        FilterInativos.IsChecked == true ? "inativos"
        : FilterTodos.IsChecked == true ? "todos"
        : "ativos";

    private void BuildPermChecks()
    {
        _permCheckboxes.Clear();
        PermChecks.Items.Clear();
        foreach (var (key, label) in UserPermissions.Catalog)
        {
            var cb = new CheckBox
            {
                Content = label,
                Tag = key,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 11,
            };
            cb.Checked += (_, _) => _permissions.Customized = true;
            cb.Unchecked += (_, _) => _permissions.Customized = true;
            _permCheckboxes[key] = cb;
            PermChecks.Items.Add(cb);
        }
    }

    private void Reload()
    {
        var selId = (UsersGrid.SelectedItem as SystemUserRow)?.Id;
        UsersGrid.ItemsSource = UsersService.List(SearchBox.Text, AtivoFilter);
        if (selId is int id)
        {
            _suppress = true;
            UsersGrid.SelectedItem = UsersService.List(SearchBox.Text, AtivoFilter)
                .FirstOrDefault(u => u.Id == id);
            _suppress = false;
        }
    }

    private void UpdateSearchPlaceholder() =>
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void ClearForm()
    {
        _editId = null;
        FormTitle.Text = "Novo usuário";
        LoginBox.Text = "";
        NomeBox.Text = "";
        RoleBox.SelectedValue = "vendedor";
        PasswordBox.Password = "";
        ConfirmPasswordBox.Password = "";
        ActiveBox.IsChecked = true;
        DeactivateBtn.Visibility = Visibility.Collapsed;
        ActivateBtn.Visibility = Visibility.Collapsed;
        ResetPasswordBtn.Visibility = Visibility.Collapsed;
        CancelBtn.Content = "Limpar";
        PasswordLabel.Text = "Senha *";
        ConfirmLabel.Text = "Confirmar senha *";
        _permissions = UserPermissions.ForRole("vendedor");
        BindPermChecks(_permissions);
        UpdatePermHint();
    }

    private void UpdatePermHint()
    {
        var role = RoleBox.SelectedValue as string ?? "vendedor";
        var (title, desc) = UsersService.RolePermissionsDetail(role);
        PermRoleTitle.Text = title;
        PermHint.Text = desc;
    }

    private void BindPermChecks(UserPermissions p)
    {
        _permissions = p;
        foreach (var (key, cb) in _permCheckboxes)
            cb.IsChecked = p.Get(key);
    }

    private UserPermissions ReadPermChecks()
    {
        foreach (var (key, cb) in _permCheckboxes)
            _permissions.Set(key, cb.IsChecked == true);
        return _permissions;
    }

    private void BindRow(SystemUserRow u)
    {
        _editId = u.Id;
        FormTitle.Text = $"Editar usuário — {u.Login}";
        LoginBox.Text = u.Login;
        NomeBox.Text = u.Nome;
        RoleBox.SelectedValue = u.Role;
        PasswordBox.Password = "";
        ConfirmPasswordBox.Password = "";
        ActiveBox.IsChecked = u.Active;
        DeactivateBtn.Visibility = u.Active ? Visibility.Visible : Visibility.Collapsed;
        ActivateBtn.Visibility = !u.Active && AppSession.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        ResetPasswordBtn.Visibility = AppSession.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.Content = "Cancelar";
        PasswordLabel.Text = "Senha (opcional)";
        ConfirmLabel.Text = "Confirmar senha (se alterar)";
        BindPermChecks(u.Permissions);
        UpdatePermHint();
    }

    private void SelectAndBind(SystemUserRow u)
    {
        _suppress = true;
        UsersGrid.SelectedItem = u;
        _suppress = false;
        BindRow(u);
        LoginBox.Focus();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        _suppress = true;
        UsersGrid.SelectedItem = null;
        _suppress = false;
        ClearForm();
        LoginBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => New_Click(sender, e);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pwd = PasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;
            var isNew = _editId is null;

            if (isNew)
            {
                if (string.IsNullOrEmpty(pwd))
                    throw new UsersException("Informe a senha para o novo usuário.");
                if (pwd != confirm)
                    throw new UsersException("Senha e confirmação não conferem.");
            }
            else if (!string.IsNullOrEmpty(pwd) && pwd != confirm)
            {
                throw new UsersException("Senha e confirmação não conferem.");
            }

            var perms = ReadPermChecks();
            perms.Customized = true;

            var id = UsersService.Save(
                _editId,
                LoginBox.Text,
                NomeBox.Text,
                RoleBox.SelectedValue as string ?? "vendedor",
                ActiveBox.IsChecked == true,
                string.IsNullOrEmpty(pwd) ? null : pwd,
                perms);

            if (AppSession.CurrentUser?.Id == id)
                AppSession.RefreshPermissions();

            Reload();
            var saved = UsersService.Get(id);
            if (saved is not null)
                SelectAndBind(saved);

            MessageBox.Show("Usuário gravado.", "Usuários", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Usuários", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeactivateUser(int id)
    {
        if (MessageBox.Show("Desativar este usuário?", "Usuários",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            UsersService.Deactivate(id);
            ClearForm();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Usuários", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (_editId is int id)
            DeactivateUser(id);
    }

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (_editId is not int id)
            return;
        if (!AppSession.IsAdmin)
        {
            MessageBox.Show("Apenas administradores podem aprovar contas.",
                "Usuários", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var u = UsersService.Get(id) ?? throw new UsersException("Usuário não encontrado.");
            UsersService.Save(u.Id, u.Login, u.Nome, u.Role, active: true, newPassword: null, u.Permissions);
            Reload();
            var saved = UsersService.Get(id);
            if (saved is not null)
                SelectAndBind(saved);
            MessageBox.Show($"Conta “{u.Login}” ativada. Já pode entrar no sistema.",
                "Usuários", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Usuários", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SystemUserRow u })
            SelectAndBind(u);
    }

    private void DeactivateRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SystemUserRow u })
            DeactivateUser(u.Id);
    }

    private void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (_editId is int id)
        {
            var row = UsersService.Get(id);
            if (row is not null)
                ResetPasswordFor(row);
        }
    }

    private void ResetPasswordRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SystemUserRow u })
            ResetPasswordFor(u);
    }

    private void ResetPasswordFor(SystemUserRow u)
    {
        if (!AppSession.IsAdmin)
        {
            MessageBox.Show("Apenas administradores podem redefinir senha de outro usuário.",
                "Usuários", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new PasswordChangeWindow(
            PasswordChangeMode.AdminReset,
            user: null,
            prefilledLogin: u.Login,
            targetUserId: u.Id,
            targetNome: u.Nome)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() == true && dlg.PasswordChanged)
        {
            MessageBox.Show(
                $"Senha de “{u.Login}” redefinida.\nPeça para a pessoa entrar com a nova senha.",
                "Usuários",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (UsersGrid.SelectedItem is SystemUserRow u)
            BindRow(u);
    }

    private void UsersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UsersGrid.SelectedItem is SystemUserRow u)
            SelectAndBind(u);
    }

    private void RoleBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdatePermHint();
        if (!_permissions.Customized || _editId is null)
        {
            var role = RoleBox.SelectedValue as string ?? "vendedor";
            BindPermChecks(UserPermissions.ForRole(role));
        }
    }

    private void ResetPerms_Click(object sender, RoutedEventArgs e)
    {
        var role = RoleBox.SelectedValue as string ?? "vendedor";
        var p = UserPermissions.ForRole(role);
        p.Customized = false;
        BindPermChecks(p);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchPlaceholder();
        Reload();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { New_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F5) { Save_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            if (_editId is not null) { Cancel_Click(sender, e); e.Handled = true; }
            else { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        }
    }
}
