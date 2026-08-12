using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PdvVendasConsultaWindow : Window
{
    private bool _caixaOpen;
    private PdvSaleDetail? _currentDetail;

    public PdvVendasConsultaWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshList();
    }

    /// <summary>
    /// DataGrid engole / falha a bolinha do mouse; força o scroll interno.
    /// </summary>
    private void Grid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;
        if (Keyboard.Modifiers == ModifierKeys.Shift)
            return;

        var scroll = FindScrollViewer(grid);
        if (scroll is null)
            return;

        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindScrollViewer(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    private void RefreshList(int? selectSaleId = null)
    {
        _caixaOpen = !StoreNetworkMode.IsClient && CashService.IsOperational();
        var range = StoreNetworkMode.IsClient
            ? (From: DateTime.Today, To: DateTime.Today, CarriedOver: false, IsOpen: false)
            : CashService.GetPdvSalesDateRange();
        var rows = PdvQueryService.ListSales(includeCancelled: true);
        SalesGrid.ItemsSource = rows;

        var ativas = rows.Where(r => !r.Cancelled).ToList();
        var totalAtivas = ativas.Sum(r => r.Total);
        var meta = $"{rows.Count} venda(s) · Ativas: R$ {totalAtivas:N2}";
        if (StoreNetworkMode.IsClient)
            meta += " · dados do PC da loja (somente consulta)";
        else if (!_caixaOpen)
            meta += " · Caixa fechado (só consulta)";
        ListMetaText.Text = meta;

        TitleText.Text = range.From.Date == range.To.Date
            ? $"Consulta de Vendas do Dia — {range.From:dd/MM/yyyy}"
            : $"Consulta de Vendas do Turno — {range.From:dd/MM/yyyy} a {range.To:dd/MM/yyyy}";
        UpdateActionButtonsEnabled();

        if (rows.Count == 0)
        {
            ClearDetail();
            return;
        }

        if (selectSaleId is int sid)
        {
            var match = rows.FirstOrDefault(r => r.Id == sid);
            if (match is not null)
            {
                SalesGrid.SelectedItem = match;
                SalesGrid.ScrollIntoView(match);
                return;
            }
        }

        SalesGrid.SelectedIndex = 0;
    }

    private void SalesGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is PdvSaleListRow { Cancelled: true })
            e.Row.Opacity = 0.55;
        else
            e.Row.Opacity = 1;
    }

    private void SalesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SalesGrid.SelectedItem is not PdvSaleListRow row)
        {
            ClearDetail();
            return;
        }

        try
        {
            ShowDetail(PdvQueryService.GetSaleDetail(row.Id));
        }
        catch (PdvException ex)
        {
            MessageBox.Show(ex.Message, "Consulta vendas", MessageBoxButton.OK, MessageBoxImage.Warning);
            ClearDetail();
        }
    }

    private void ShowDetail(PdvSaleDetail detail)
    {
        _currentDetail = detail;
        DetailTitleText.Text = detail.Cancelled
            ? $"Venda #{detail.Id} — CANCELADA"
            : $"Venda #{detail.Id} — {detail.PaymentType}";

        while (ResumoPanel.Children.Count > 5)
            ResumoPanel.Children.RemoveAt(5);

        ChipDataHora.Text = detail.CreatedAtBr;
        ChipCliente.Text = string.IsNullOrWhiteSpace(detail.CustomerName) ? "—" : detail.CustomerName!;
        ChipVendedor.Text = string.IsNullOrWhiteSpace(detail.SellerName) ? "—" : detail.SellerName!;
        ChipTotal.Text = $"R$ {detail.Total:N2}";
        ChipForma.Text = detail.PaymentType;

        if (detail.Payments.Count > 1)
        {
            foreach (var part in detail.Payments)
            {
                var chip = new StackPanel { Margin = new Thickness(0, 0, 20, 6) };
                chip.Children.Add(new TextBlock
                {
                    Text = part.PaymentType,
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.DimGray,
                });
                chip.Children.Add(new TextBlock
                {
                    Text = $"R$ {part.Amount:N2}",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                });
                ResumoPanel.Children.Add(chip);
            }
        }

        var hasTroco = detail.CashReceived is > 0 && detail.ChangeAmount is > 0.009;
        if (hasTroco)
        {
            var cashPart = detail.Payments
                .Where(p => p.PaymentType.Contains("Dinheiro", StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.Amount);
            if (cashPart < 0.009)
                cashPart = detail.Total;

            DinheiroParteLabel.Text = detail.Payments.Count > 1 ? "Parte em dinheiro" : "Total da venda";
            DinheiroParteValue.Text = $"R$ {cashPart:N2}";
            DinheiroRecebidoValue.Text = $"R$ {detail.CashReceived:N2}";
            DinheiroTrocoValue.Text = $"R$ {detail.ChangeAmount:N2}";
            DinheiroPanel.Visibility = Visibility.Visible;
        }
        else
        {
            DinheiroPanel.Visibility = Visibility.Collapsed;
        }

        ItemsGrid.ItemsSource = detail.Items;
        if (detail.Items.Count > 0)
            ItemsGrid.SelectedIndex = 0;

        UpdateActionButtonsEnabled();
    }

    private void ClearDetail()
    {
        _currentDetail = null;
        DetailTitleText.Text = "Selecione uma venda";
        ChipDataHora.Text = "—";
        ChipCliente.Text = "—";
        ChipVendedor.Text = "—";
        ChipTotal.Text = "—";
        ChipForma.Text = "—";
        while (ResumoPanel.Children.Count > 5)
            ResumoPanel.Children.RemoveAt(5);
        DinheiroPanel.Visibility = Visibility.Collapsed;
        ItemsGrid.ItemsSource = null;
        UpdateActionButtonsEnabled();
    }

    private void UpdateActionButtonsEnabled()
    {
        // No notebook (cliente): só consulta — sem cancelar / trocar / alterar pagamento
        if (StoreNetworkMode.IsClient)
        {
            BtnTrocar.IsEnabled = false;
            BtnPagamento.IsEnabled = false;
            BtnCancelar.IsEnabled = false;
            BtnTrocaDevolucao.IsEnabled = false;
            BtnTrocar.Opacity = 0.45;
            BtnPagamento.Opacity = 0.45;
            BtnCancelar.Opacity = 0.45;
            BtnTrocaDevolucao.Opacity = 0.45;
            BtnTrocar.ToolTip = "Só no PC da loja";
            BtnPagamento.ToolTip = "Só no PC da loja";
            BtnCancelar.ToolTip = "Só no PC da loja";
            BtnTrocaDevolucao.ToolTip = "Só no PC da loja";
            return;
        }

        var hasActive = _currentDetail is { Cancelled: false };
        var canMutate = hasActive && _caixaOpen;
        var canEditSale = canMutate && AccessControl.Can("PdvEditarVenda");
        var canChangePay = canMutate && AccessControl.Can("PdvAlterarPagamento");
        var canCancel = canMutate && AccessControl.Can("PdvCancelarVenda");
        var canExchange = hasActive && AccessControl.Can("PdvTrocaDevolucao");
        BtnTrocar.IsEnabled = canEditSale;
        BtnPagamento.IsEnabled = canChangePay;
        BtnCancelar.IsEnabled = canCancel;
        BtnTrocaDevolucao.IsEnabled = canExchange;
        BtnTrocar.Opacity = canEditSale ? 1 : 0.45;
        BtnPagamento.Opacity = canChangePay ? 1 : 0.45;
        BtnCancelar.Opacity = canCancel ? 1 : 0.45;
        BtnTrocaDevolucao.Opacity = canExchange ? 1 : 0.45;
        if (hasActive && !AccessControl.Can("PdvTrocaDevolucao"))
            BtnTrocaDevolucao.ToolTip = "Sem permissão para Troca / Devolução";
        if (hasActive && _caixaOpen && !AccessControl.Can("PdvAlterarPagamento"))
            BtnPagamento.ToolTip = "Sem permissão para alterar pagamento";
        if (hasActive && _caixaOpen && !AccessControl.Can("PdvCancelarVenda"))
            BtnCancelar.ToolTip = "Sem permissão para cancelar venda";
        if (hasActive && _caixaOpen && !AccessControl.Can("PdvEditarVenda"))
            BtnTrocar.ToolTip = "Sem permissão para editar / trocar item da venda";
        else if (canEditSale)
            BtnTrocar.ToolTip = null;
    }

    private bool EnsureCanMutate()
    {
        if (StoreNetworkMode.IsClient)
        {
            MessageBox.Show(
                "No notebook só é possível consultar vendas.\nCancelar, trocar ou alterar pagamento deve ser feito no PC da loja.",
                "Consulta vendas", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!_caixaOpen)
        {
            MessageBox.Show("Caixa fechado — só consulta. Abra o caixa para alterar ou cancelar vendas.",
                "Consulta vendas", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (_currentDetail is null)
        {
            MessageBox.Show("Selecione uma venda.", "Consulta vendas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (_currentDetail.Cancelled)
        {
            MessageBox.Show("Venda já cancelada.", "Consulta vendas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private void Trocar_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanMutate() || _currentDetail is null)
            return;
        if (!AccessControl.Ensure("PdvEditarVenda", "editar / trocar item da venda", this))
            return;
        if (ItemsGrid.SelectedItem is not PdvSaleItemRow item)
        {
            MessageBox.Show("Selecione o item da venda para trocar.", "Trocar produto",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new PdvTrocarProdutoWindow(item) { Owner = this };
        if (dlg.ShowDialog() != true || !dlg.Confirmed || dlg.SelectedProduct is null)
            return;

        if (!TryResolveCigaretteModeForSwap(dlg.SelectedProduct, out var cigaretteMode))
            return;

        try
        {
            var preview = ApplicationServices.PreviewSwapSaleItem.Execute(new PreviewSwapSaleItemCommand
            {
                SaleId = _currentDetail.Id,
                ItemId = item.Id,
                NewProductId = dlg.SelectedProduct.Id,
                KeepLinePrice = dlg.KeepLinePrice,
                NewQuantity = dlg.NewQuantity,
                CigaretteMode = cigaretteMode,
            });

            IReadOnlyList<SalePayment>? confirmedPayments = null;
            double cashReceived = 0;
            int? customerPersonId = null;

            if (preview.RequiresPaymentConfirmation)
            {
                var diffNote = preview.Difference > 0
                    ? $"Diferença a cobrar: R$ {preview.Difference:N2}.\n" +
                      "Confirme como o cliente pagará. PIX/cartão: cobrança física/real deve ter ocorrido."
                    : $"Diferença a devolver/estornar: R$ {Math.Abs(preview.Difference):N2}.\n" +
                      "Confirme a nova composição. PIX/cartão: estorno físico deve ser tratado na maquininha/MP.";

                var detailForPay = new PdvSaleDetail
                {
                    Id = preview.SaleId,
                    Total = preview.NewTotal,
                    PaymentType = preview.PaymentType,
                    PaymentLabel = _currentDetail.PaymentLabel,
                    CustomerPersonId = preview.CustomerPersonId,
                    CustomerName = _currentDetail.CustomerName,
                    CashReceived = _currentDetail.CashReceived,
                    Payments = preview.CurrentPayments
                        .Select(p => new PdvPaymentPart { PaymentType = p.PaymentType, Amount = p.Amount })
                        .ToList(),
                };

                var payDlg = new PdvAlterarPagamentoWindow(detailForPay, diffNote) { Owner = this };
                payDlg.Title = "Confirmar pagamento da troca";
                if (payDlg.ShowDialog() != true || !payDlg.Confirmed)
                    return;

                confirmedPayments = (payDlg.Payments ?? [])
                    .Select(p => new SalePayment { PaymentType = p.PaymentType, Amount = p.Amount })
                    .ToList();
                cashReceived = payDlg.CashReceived;
                customerPersonId = payDlg.CustomerPersonId;
            }
            else if (preview.IsPureFiado && Math.Abs(preview.Difference) >= 0.01)
            {
                var fiadoMsg = preview.Difference > 0
                    ? $"Fiado passará de R$ {preview.OldTotal:N2} para R$ {preview.NewTotal:N2}.\nConfirmar troca?"
                    : $"Dívida (fiado) passará de R$ {preview.OldTotal:N2} para R$ {preview.NewTotal:N2}.\nConfirmar troca?";
                if (MessageBox.Show(fiadoMsg, "Trocar produto",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }

            var result = ApplicationServices.SwapSaleItem.Execute(new SwapSaleItemCommand
            {
                SaleId = _currentDetail.Id,
                ItemId = item.Id,
                NewProductId = dlg.SelectedProduct.Id,
                KeepLinePrice = dlg.KeepLinePrice,
                NewQuantity = dlg.NewQuantity,
                CigaretteMode = cigaretteMode,
                ConfirmedPayments = confirmedPayments,
                CashReceived = cashReceived,
                CustomerPersonId = customerPersonId,
            });
            MessageBox.Show(result.Message, "Trocar produto", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshList(result.SaleId);
        }
        catch (Exception ex) when (ex is PdvException or CashOperationException)
        {
            MessageBox.Show(ex.Message, "Trocar produto", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Cigarro com PrecoAvulso → diálogo Avulso/Maço; demais → null (legado MAÇO no Service).
    /// Retorna false se o operador cancelar (Esc).
    /// </summary>
    private bool TryResolveCigaretteModeForSwap(Product product, out string? cigaretteMode)
    {
        cigaretteMode = null;
        if (!PdvCartHelper.NeedsCigaretteModeChoice(product))
            return true;

        var extra = ProductExtra.Parse(product.ExtraJson);
        var packPrice = extra.PrecoAtacado > 0 ? extra.PrecoAtacado : product.SalePrice;
        var modeDlg = new PdvCigaretteModeWindow(
            product.Name,
            ProductPriceHelper.RoundPrice(extra.PrecoAvulso),
            ProductPriceHelper.RoundPrice(packPrice))
        {
            Owner = this,
        };
        if (modeDlg.ShowDialog() != true || string.IsNullOrWhiteSpace(modeDlg.SelectedMode))
            return false;

        cigaretteMode = modeDlg.SelectedMode;
        return true;
    }

    private void TrocaDevolucao_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetail is null)
        {
            MessageBox.Show("Selecione uma venda.", "Troca / Devolução",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_currentDetail.Cancelled)
        {
            MessageBox.Show("Venda já cancelada.", "Troca / Devolução",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!AccessControl.Ensure("PdvTrocaDevolucao", "Troca / Devolução de venda", this))
            return;

        var dlg = new SaleExchangeWindow(_currentDetail.Id) { Owner = this };
        if (dlg.ShowDialog() == true)
            RefreshList(_currentDetail.Id);
    }

    private void Pagamento_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanMutate() || _currentDetail is null)
            return;
        if (!AccessControl.Ensure("PdvAlterarPagamento", "alterar pagamento da venda", this))
            return;

        var dlg = new PdvAlterarPagamentoWindow(_currentDetail) { Owner = this };
        if (dlg.ShowDialog() != true || !dlg.Confirmed)
            return;

        try
        {
            var updated = ApplicationServices.ChangeSalePayment.Execute(new ChangeSalePaymentCommand
            {
                SaleId = _currentDetail.Id,
                Payments = (dlg.Payments ?? []).Select(p => new SalePayment
                {
                    PaymentType = p.PaymentType,
                    Amount = p.Amount,
                }).ToList(),
                CashReceived = dlg.CashReceived,
                CustomerPersonId = dlg.CustomerPersonId,
            });
            MessageBox.Show("Forma de pagamento alterada — caixa atualizado.",
                "Alterar pagamento", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshList(updated.SaleId);
        }
        catch (Exception ex) when (ex is PdvException or CashOperationException)
        {
            MessageBox.Show(ex.Message, "Alterar pagamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelSale_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCanMutate() || _currentDetail is null)
            return;
        if (!AccessControl.Ensure("PdvCancelarVenda", "cancelar venda do dia", this))
            return;

        if (MessageBox.Show(
                $"Cancelar venda #{_currentDetail.Id}?\nEstoque e caixa serão estornados.",
                "Consulta vendas",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            ApplicationServices.CancelSale.Execute(new CancelSaleCommand
            {
                SaleId = _currentDetail.Id,
            });
            MessageBox.Show($"Venda #{_currentDetail.Id} cancelada.", "Consulta vendas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshList();
        }
        catch (Exception ex) when (ex is PdvException or CashOperationException)
        {
            MessageBox.Show(ex.Message, "Consulta vendas", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshList(_currentDetail?.Id);

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Deseja fechar a consulta de vendas?", "Fechar consulta",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F4 || e.Key == Key.Delete)
        {
            CancelSale_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3)
        {
            Trocar_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F7)
        {
            TrocaDevolucao_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            Pagamento_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F9)
        {
            RefreshList(_currentDetail?.Id);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Up or Key.Down && SalesGrid.Items.Count > 0
            && !ItemsGrid.IsKeyboardFocusWithin)
        {
            var idx = SalesGrid.SelectedIndex;
            if (e.Key == Key.Down && idx < SalesGrid.Items.Count - 1)
                SalesGrid.SelectedIndex = idx + 1;
            else if (e.Key == Key.Up && idx > 0)
                SalesGrid.SelectedIndex = idx - 1;
            SalesGrid.ScrollIntoView(SalesGrid.SelectedItem);
            e.Handled = true;
        }
    }
}
