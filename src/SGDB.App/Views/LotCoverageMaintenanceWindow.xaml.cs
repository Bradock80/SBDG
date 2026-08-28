using System.Windows;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class LotCoverageMaintenanceWindow : Window
{
    private readonly int _productId;
    private LotCoverageSnapshot _snap = new();
    private readonly bool _canMutate;

    /// <summary>True se alguma mutação bem-sucedida ocorreu (pai deve recarregar).</summary>
    public bool Changed { get; private set; }

    public LotCoverageMaintenanceWindow(int productId)
    {
        _productId = productId;
        _canMutate = LotCoverageUi.CanMutateUi();
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => Reload();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Reload();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Grid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateActionState();

    private void Reload()
    {
        try
        {
            _snap = LotCoverageService.GetSnapshot(_productId);
            Title = $"{LotCoverageUi.WindowTitle} — {_snap.ProductName}";
            HeaderText.Text = LotCoverageUi.FormatHeader(_snap);

            var status = _snap.ConsistencyStatus;
            StatusTitle.Text = LotCoverageUi.ConsistencyLabel(status);
            var hint = LotCoverageUi.ConsistencyHint(status);
            StatusHint.Text = hint;
            if (string.IsNullOrEmpty(hint))
            {
                StatusBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusBanner.Visibility = Visibility.Visible;
                // UnderTracked é situação comum — tom informativo; inconsistências usam alerta.
                if (status is LotCoverageConsistencyStatus.OverTracked
                    or LotCoverageConsistencyStatus.NegativeStock)
                {
                    StatusBanner.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFBEB")!);
                    StatusBanner.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FDE68A")!);
                    StatusTitle.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#92400E")!);
                    StatusHint.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#78350F")!);
                }
                else
                {
                    StatusBanner.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F0F9FF")!);
                    StatusBanner.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#BAE6FD")!);
                    StatusTitle.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#075985")!);
                    StatusHint.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0C4A6E")!);
                }
            }

            Grid.ItemsSource = LotCoverageUi.ToRows(_snap);
            FooterText.Text = _canMutate
                ? "Cobertura = rastreabilidade do estoque do depósito. Remover rastreamento não remove estoque."
                : StoreNetworkMode.IsClient
                    ? "Consulta apenas neste cliente Rede Loja. Alterações devem ser feitas na matriz."
                    : "Seu usuário pode consultar, mas não alterar cobertura de validade/lote.";

            UpdateActionState();
        }
        catch (Exception ex)
        {
            Grid.ItemsSource = null;
            HeaderText.Text = LotCoverageUi.MapError(ex);
            MessageBox.Show(LotCoverageUi.MapError(ex), LotCoverageUi.WindowTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateActionState()
    {
        var selected = Grid.SelectedItem as LotCoverageLineUi;
        var hasLine = selected is not null;
        AddBtn.IsEnabled = _canMutate;
        EditBtn.IsEnabled = _canMutate && hasLine;
        SplitBtn.IsEnabled = _canMutate && hasLine;
        QtyBtn.IsEnabled = _canMutate && hasLine;
        RemoveBtn.IsEnabled = _canMutate && hasLine;
    }

    private LotCoverageLineUi? SelectedLine() => Grid.SelectedItem as LotCoverageLineUi;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanMutate()) return;
        var dlg = new LotCoverageFormWindow(
            LotCoverageFormMode.Add,
            _snap.ProductName,
            availableUntracked: _snap.UntrackedQuantity)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        RunMutation("add", () => LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = _productId,
            Quantity = dlg.Result.Quantity,
            ExpiryDate = dlg.Result.ExpiryDate,
            LotNumber = dlg.Result.LotNumber,
            Reason = dlg.Result.Reason,
        }));
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanMutate()) return;
        var line = SelectedLine();
        if (line is null)
        {
            MessageBox.Show("Selecione uma linha na tabela.", LotCoverageUi.WindowTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new LotCoverageFormWindow(
            LotCoverageFormMode.Edit,
            _snap.ProductName,
            currentExpiry: line.ExpiryDate,
            currentLot: line.Source.LotNumber,
            isPurchase: line.IsPurchaseOrigin)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        RunMutation("edit", () => LotCoverageService.EditCoverage(new LotCoverageEditInput
        {
            ProductLotId = line.Id,
            ExpiryDate = dlg.Result.ExpiryDate,
            LotNumber = dlg.Result.LotNumber,
            Reason = dlg.Result.Reason,
        }));
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanMutate()) return;
        var line = SelectedLine();
        if (line is null)
        {
            MessageBox.Show("Selecione uma linha na tabela.", LotCoverageUi.WindowTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (line.IsPurchaseOrigin)
        {
            MessageBox.Show(LotCoverageUi.MapPurchaseProtected("split"), LotCoverageUi.WindowTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new LotCoverageFormWindow(
            LotCoverageFormMode.Split,
            _snap.ProductName,
            currentQty: line.Quantity,
            currentLot: line.Source.LotNumber)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        RunMutation("split", () => LotCoverageService.SplitCoverage(new LotCoverageSplitInput
        {
            ProductLotId = line.Id,
            DestinationQuantity = dlg.Result.Quantity,
            DestinationExpiryDate = dlg.Result.ExpiryDate,
            DestinationLotNumber = dlg.Result.LotNumber,
            Reason = dlg.Result.Reason,
        }));
    }

    private void Quantity_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanMutate()) return;
        var line = SelectedLine();
        if (line is null)
        {
            MessageBox.Show("Selecione uma linha na tabela.", LotCoverageUi.WindowTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (line.IsPurchaseOrigin)
        {
            MessageBox.Show(LotCoverageUi.MapPurchaseProtected("quantity"), LotCoverageUi.WindowTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new LotCoverageFormWindow(
            LotCoverageFormMode.Quantity,
            _snap.ProductName,
            currentQty: line.Quantity)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        RunMutation("quantity", () => LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
        {
            ProductLotId = line.Id,
            Quantity = dlg.Result.Quantity,
            Reason = dlg.Result.Reason,
        }));
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanMutate()) return;
        var line = SelectedLine();
        if (line is null)
        {
            MessageBox.Show("Selecione uma linha na tabela.", LotCoverageUi.WindowTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (line.IsPurchaseOrigin)
        {
            MessageBox.Show(LotCoverageUi.MapPurchaseProtected("remove"), LotCoverageUi.WindowTitle,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new LotCoverageFormWindow(
            LotCoverageFormMode.Remove,
            _snap.ProductName)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        RunMutation("remove", () => LotCoverageService.RemoveCoverage(new LotCoverageRemoveInput
        {
            ProductLotId = line.Id,
            Reason = dlg.Result.Reason,
        }));
    }

    private bool EnsureCanMutate()
    {
        if (_canMutate) return true;
        MessageBox.Show(
            StoreNetworkMode.IsClient
                ? LotCoverageUi.MapError(new StoreNetworkClientBlockedException("cobertura de lote"))
                : LotCoverageRules.AccessDeniedMessage,
            LotCoverageUi.WindowTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void RunMutation(string operation, Func<LotCoverageMutationResult> action)
    {
        try
        {
            action();
            Changed = true;
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                LotCoverageUi.MapError(ex, operation),
                LotCoverageUi.WindowTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Reload();
        }
    }
}
