using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Views;

namespace SGDB;

public partial class MainWindow : Window
{
    private readonly User _user;
    private readonly HomeSplashView _homeView = new();
    private string? _activeModule;
    private readonly DispatcherTimer _stockAlertTimer = new() { Interval = TimeSpan.FromMinutes(2) };
    private readonly DispatcherTimer _preContaAlertTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private bool _handlingPreContaAlert;
    /// <summary>
    /// True durante logout com nova sessão: fecha esta MainWindow sem Application.Shutdown
    /// (ShutdownMode=OnExplicitShutdown exige Shutdown explícito no fechamento definitivo / X).
    /// </summary>
    private bool _isLoggingOut;

    private int? _pagarPurchaseId;

    private static readonly Dictionary<string, (string Title, string Body)> Modules = new()
    {
    };

    public MainWindow(User user)
    {
        _user = user;
        AppSession.SetUser(user);
        InitializeComponent();

        _homeView.ValidityAlertClicked += (_, _) => ShowValidityControl();
        RefreshCompanyTitle();
        UserNameText.Text = StoreNetworkMode.IsClient
            ? $"Rede Loja • {user.Nome}"
            : user.Nome;
        UserRoleText.Text = FormatRole(user.Role);

        ShowHome();
        Loaded += (_, _) =>
        {
            ApplyPermissionUi();
            BackupSchedulerService.Start();
            TryAutoStartStoreServer();
            RefreshCompanyTitle();
            RefreshStockAlert();
            _stockAlertTimer.Start();
            _preContaAlertTimer.Start();
        };
        _stockAlertTimer.Tick += (_, _) => RefreshStockAlert();
        _preContaAlertTimer.Tick += (_, _) => ProcessPreContaAlerts();
        Closing += (_, _) =>
        {
            _stockAlertTimer.Stop();
            _preContaAlertTimer.Stop();
            try { BackupSchedulerService.TryBackupOnAppClose(); }
            catch { /* backup nunca bloqueia fechamento */ }
        };
        Closed += (_, _) =>
        {
            BackupSchedulerService.Stop();
            DeckCompanionHost.Current?.Dispose();
            StoreNetworkHost.Current?.Dispose();

            // X / fechamento definitivo: encerra o processo.
            // Logout (nova MainWindow já criada): não Shutdown.
            if (!_isLoggingOut)
            {
                ApplicationLoginService.AbandonLocalSession();
                System.Windows.Application.Current?.Shutdown();
            }
        };
    }

    private void RefreshCompanyTitle()
    {
        var deposito = AppSettingsService.GetNomeDeposito();
        DepositoTitleText.Text = deposito;
        _homeView.DepositoName = deposito;

        var ver = FormatAppVersion(AutoUpdateService.GetCurrentVersion());
        string? role = null;
        if (StoreNetworkMode.IsClient)
            role = "Cliente";
        else if (StoreNetworkMode.IsServer || StoreNetworkHost.Current?.IsRunning == true)
            role = "Servidor";

        Title = role is null
            ? $"SGDB — {deposito}  v{ver}"
            : $"SGDB — {deposito}  [{role} · v{ver}]";
        if (DatabaseService.IsIsolatedDatabasePath(DatabaseService.DatabasePath))
            Title += "  — TESTE ISOLADO";
    }

    private void RefreshStockAlert()
    {
        try
        {
            var snap = StockAlertService.GetSnapshot(3);
            if (snap.TotalBelowMin <= 0)
            {
                StockAlertBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                var drinks = snap.DrinkBelowMin;
                if (drinks > 0)
                {
                    StockAlertBtnText.Text = drinks == 1
                        ? "1 no mínimo"
                        : $"{drinks} no mínimo";
                    StockAlertBtn.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFE, 0xE2, 0xE2));
                    StockAlertBtn.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));
                    StockAlertBtnText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x99, 0x1B, 0x1B));
                    var tip = snap.TopDrinks.Count == 0
                        ? $"{drinks} cerveja/refri no estoque mínimo"
                        : string.Join("\n", snap.TopDrinks.Select(a => $"• {a.Name} ({a.Stock:0.#}/{a.MinStock:0.#})"));
                    if (snap.TotalBelowMin > drinks)
                        tip += $"\n+ {snap.TotalBelowMin - drinks} outro(s) produto(s)";
                    tip += "\n\nClique para abrir e corrigir.";
                    StockAlertBtn.ToolTip = tip;
                }
                else
                {
                    StockAlertBtnText.Text = snap.TotalBelowMin == 1
                        ? "1 no mínimo"
                        : $"{snap.TotalBelowMin} no mínimo";
                    StockAlertBtn.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFE, 0xF3, 0xC7));
                    StockAlertBtn.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
                    StockAlertBtnText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x92, 0x40, 0x0E));
                    StockAlertBtn.ToolTip =
                        $"{snap.TotalBelowMin} produto(s) no estoque mínimo.\nClique para abrir e corrigir.";
                }

                StockAlertBtn.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            StockAlertBtn.Visibility = Visibility.Collapsed;
        }

        RefreshLotExpiryAlert();
    }

    private void RefreshLotExpiryAlert()
    {
        try
        {
            var snap = ValidityControlService.GetSnapshot();
            var text = ValidityControlEngine.FormatHomeSummary(snap.Cards);
            var show = ValidityControlEngine.ShouldShowHomeAlert(snap.Cards);
            _homeView.SetValidityAlert(show ? text : null, snap.Cards.Expired > 0);

            if (!show)
            {
                LotExpiryAlertBtn.Visibility = Visibility.Collapsed;
                return;
            }

            LotExpiryAlertBtnText.Text = snap.Cards.Expired > 0
                ? (snap.Cards.Expired == 1 ? "1 vencido" : $"{snap.Cards.Expired} vencidos")
                : text;
            LotExpiryAlertBtn.ToolTip = text + "\nClique para abrir o Controle de Validades.";
            LotExpiryAlertBtn.Visibility = Visibility.Visible;
        }
        catch
        {
            _homeView.SetValidityAlert(null, false);
            LotExpiryAlertBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void ProcessPreContaAlerts()
    {
        if (_handlingPreContaAlert || !IsLoaded || !IsVisible)
            return;

        List<OpenTabListRow> pending;
        try
        {
            pending = OpenTabService.ListPendingPreContaAlerts().ToList();
        }
        catch
        {
            return;
        }

        if (pending.Count == 0)
            return;

        _handlingPreContaAlert = true;
        try
        {
            var autoPrint = false;
            try { autoPrint = AppSettingsService.GetPrinterSettings().AutoPrintDeckPreConta; }
            catch { /* usa false */ }

            var ackIds = new List<int>();
            foreach (var tab in pending)
            {
                try
                {
                    System.Media.SystemSounds.Exclamation.Play();
                    try { Console.Beep(880, 180); Console.Beep(1175, 220); }
                    catch { /* sem alto-falante */ }

                    var label = OpenTabService.FormatCashierPreContaLabel(tab);
                    var msg = $"{label} solicitou o fechamento da conta.";
                    if (autoPrint)
                        msg += "\n\nImpressão automática da pré-conta ativada.";
                    else
                        msg += "\n\nAbra a mesa amarela e use Pré-conta (F9) para imprimir.";

                    MessageBox.Show(this, msg, "Pré-conta solicitada",
                        MessageBoxButton.OK, MessageBoxImage.Warning);

                    if (autoPrint)
                    {
                        try { OpenTabService.PrintPreConta(tab.Id); }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this,
                                $"Não foi possível imprimir a pré-conta de {label}:\n{ex.Message}",
                                "Impressão", MessageBoxButton.OK, MessageBoxImage.Error);
                            ackIds.Add(tab.Id);
                        }
                    }
                    else
                    {
                        ackIds.Add(tab.Id);
                    }
                }
                catch
                {
                    ackIds.Add(tab.Id);
                }
            }

            if (ackIds.Count > 0)
            {
                try { OpenTabService.AckPreContaAlerts(ackIds); }
                catch { /* próxima rodada tenta de novo */ }
            }

            OpenTabService.RaiseOpenTabsChanged();
        }
        finally
        {
            _handlingPreContaAlert = false;
        }
    }

    private void StockAlertBtn_Click(object sender, RoutedEventArgs e) => ShowMinStockFix();

    private void LotExpiryAlertBtn_Click(object sender, RoutedEventArgs e) => ShowValidityControl();

    private void ShowMinStockFix()
    {
        var view = new MinStockFixModuleView();
        view.CloseRequested += (_, _) =>
        {
            ShowHome();
            RefreshStockAlert();
        };
        MainContent.Content = view;
        UpdateToolbarHighlight();
    }

    private void ShowLotExpiryReport(int? days = null) =>
        ShowValidityControl(ValidityControlService.FilterFromLegacyDays(days));

    private void ShowValidityControl(ValidityControlFilterKind filter = ValidityControlFilterKind.All)
    {
        _activeModule = "estoque_controle_validades";
        MainAreaBorder.Background = (System.Windows.Media.Brush)FindResource("SgdbBgBrush");
        var view = new LotExpiryModuleView(filter);
        view.CloseRequested += (_, _) =>
        {
            ShowHome();
            RefreshStockAlert();
        };
        MainContent.Content = view;
        UpdateToolbarHighlight();
    }

    private void ShowStockLotConsistencyReport()
    {
        var view = new StockLotConsistencyModuleView();
        view.CloseRequested += (_, _) => ShowHome();
        MainContent.Content = view;
        UpdateToolbarHighlight();
    }

    private static string FormatAppVersion(Version v)
    {
        if (v.Build >= 0 && v.Revision > 0)
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        if (v.Build >= 0)
            return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        return $"{v.Major}.{v.Minor}";
    }

    /// <summary>
    /// Se este PC já é Servidor, religa a rede ao abrir o SGDB
    /// (senão o notebook consulta o banco local vazio).
    /// </summary>
    private static void TryAutoStartStoreServer()
    {
        if (!StoreNetworkMode.IsServer) return;
        if (StoreNetworkHost.Current?.IsRunning == true) return;
        try
        {
            StoreNetworkHost.StartNew(StoreNetworkMode.GetPort());
        }
        catch
        {
            /* usuário pode ligar manualmente em Rede Loja */
        }
    }

    private static string FormatRole(string role) => role.ToLowerInvariant() switch
    {
        "admin" => "Administrador",
        "gestor" => "Gestor",
        "vendedor" => "Vendedor",
        _ => role,
    };

    private void ShowHome()
    {
        _activeModule = "home";
        MainAreaBorder.Background = (System.Windows.Media.Brush)FindResource("SplashBackgroundBrush");
        MainContent.Content = _homeView;
        UpdateToolbarHighlight();
        RefreshStockAlert();
    }

    private void ShowPayables(int? purchaseId = null)
    {
        if (!AccessControl.EnsureModule("pagar", this))
            return;

        _activeModule = "pagar";
        MainAreaBorder.Background = (System.Windows.Media.Brush)FindResource("SgdbBgBrush");
        if (purchaseId is int pid)
            PayableService.EnsurePayablesForClosedPurchase(pid);
        var view = new PayablesModuleView(purchaseId);
        view.CloseRequested += (_, _) => ShowHome();
        MainContent.Content = view;
        UpdateToolbarHighlight();
    }

    private void ShowMovimentacao(string tipo)
    {
        _activeModule = "consultar_movimentacao";
        MainAreaBorder.Background = (System.Windows.Media.Brush)FindResource("SgdbBgBrush");
        var view = new MovimentacaoModuleView(tipo);
        view.CloseRequested += (_, _) => ShowHome();
        MainContent.Content = view;
        UpdateToolbarHighlight();
    }

    private void ShowModule(string moduleId)
    {
        if (StoreNetworkMode.IsModuleBlockedOnClient(moduleId))
        {
            MessageBox.Show(
                StoreNetworkMode.BlockedModuleMessage(moduleId),
                "Rede Loja",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!AccessControl.EnsureModule(moduleId, this))
            return;

        _activeModule = moduleId;
        MainAreaBorder.Background = (System.Windows.Media.Brush)FindResource("SgdbBgBrush");

        if (moduleId == "produtos")
        {
            var view = new ProductsModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "clientes")
        {
            var view = new ClientsModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "compras")
        {
            var view = new PurchasesModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            view.OpenPayablesRequested += (_, purchaseId) => ShowPayables(purchaseId);
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "pagar")
        {
            ShowPayables(_pagarPurchaseId);
            _pagarPurchaseId = null;
            return;
        }

        if (moduleId == "fiado")
        {
            var view = new FiadoModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "devolucao_venda")
        {
            if (!AccessControl.Ensure("PdvTrocaDevolucao", "Troca / Devolução de venda", this))
                return;
            var dlg = new SaleExchangeWindow { Owner = this };
            dlg.ShowDialog();
            // Dialog — não troca a área principal
            _activeModule = "home";
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "relatorio")
        {
            var view = new ReportsModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "relatorio_vendas")
        {
            var view = new ReportsModuleView("vendas_pdv");
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "relatorio_mais_vendidos")
        {
            var view = new ReportsModuleView("mais_vendidos");
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "relatorio_dre")
        {
            var view = new DreReportModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "relatorio_estoque_io")
        {
            var view = new StockIoReportModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "relatorio_fiado")
        {
            var view = new ReportsModuleView("fiado_contas");
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "relatorio_vendedores")
        {
            var view = new ReportsModuleView("vendas_pdv");
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "consultar_movimentacao")
        {
            ShowMovimentacao("todas");
            return;
        }

        if (moduleId == "movimentacao_vendas")
        {
            ShowMovimentacao("vendas");
            return;
        }

        if (moduleId == "movimentacao_compras")
        {
            ShowMovimentacao("compras");
            return;
        }

        if (moduleId == "inicio")
        {
            var view = new MeuNegocioModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "caixa")
        {
            var view = new CashModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "contas_bancarias")
        {
            var view = new BankAccountsModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "vasilhame")
        {
            var view = new VasilhameModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "empresa")
        {
            var view = new CompanySettingsModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            view.CompanySaved += (_, _) => RefreshCompanyTitle();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "usuarios")
        {
            var view = new UsersModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "impressoras")
        {
            var view = new PrinterSettingsModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "perifericos")
        {
            var view = new PeripheralsSettingsModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "auditoria")
        {
            var view = new AuditLogModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "backup")
        {
            var view = new BackupModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "residuos_unificacoes")
        {
            var view = new LegacyMergeCleanupModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "formas_pagamento")
        {
            var view = new PaymentMethodsModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "vendedores")
        {
            var view = new SellersModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "tipos_vasilhame")
        {
            var view = new ContainerTypesModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "categorias_financeiras")
        {
            var view = new ExpenseCategoriesModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "depositos_caixa")
        {
            var view = new CashDepositModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "decks")
        {
            var view = new OpenTabsModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "tabela_preco")
        {
            var view = new PriceTablesModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "estoque_grupos")
        {
            ShowEstoqueCatalog(ProductCatalogKind.Groups);
            return;
        }
        if (moduleId == "estoque_unidades")
        {
            ShowEstoqueCatalog(ProductCatalogKind.Units);
            return;
        }
        if (moduleId == "estoque_marcas")
        {
            ShowEstoqueCatalog(ProductCatalogKind.Brands);
            return;
        }
        if (moduleId == "ajusta_estoque")
        {
            ShowEstoqueAdjust(StockAdjustMode.Entrada);
            return;
        }
        if (moduleId == "ajusta_saldo")
        {
            ShowEstoqueAdjust(StockAdjustMode.Saldo);
            return;
        }
        if (moduleId == "ajusta_preco")
        {
            var view = new PriceAdjustModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }
        if (moduleId == "estoque_negativo")
        {
            ShowEstoqueReport(StockReportKind.Negativo);
            return;
        }
        if (moduleId == "estoque_minimo")
        {
            ShowMinStockFix();
            return;
        }
        if (moduleId == "estoque_geladeira")
        {
            ShowEstoqueReport(StockReportKind.FridgeRestock);
            return;
        }
        if (moduleId == "estoque_validade" || moduleId == "estoque_validade_lotes"
            || moduleId == "estoque_controle_validades")
        {
            var filter = moduleId == "estoque_validade"
                ? ValidityControlFilterKind.Days7
                : ValidityControlFilterKind.All;
            ShowValidityControl(filter);
            return;
        }
        if (moduleId == "estoque_consistencia_lotes")
        {
            ShowStockLotConsistencyReport();
            return;
        }
        if (moduleId == "estoque_mais_vendidos")
        {
            ShowEstoqueReport(StockReportKind.MaisVendidos);
            return;
        }
        if (moduleId == "estoque_menos_vendidos")
        {
            ShowEstoqueReport(StockReportKind.MenosVendidos);
            return;
        }
        if (moduleId == "estoque_mais_lucrativos")
        {
            ShowEstoqueReport(StockReportKind.MaisLucrativos);
            return;
        }
        if (moduleId == "estoque_menos_lucrativos")
        {
            ShowEstoqueReport(StockReportKind.MenosLucrativos);
            return;
        }
        if (moduleId == "estoque_zera_negativo")
        {
            ShowEstoqueReport(StockReportKind.ZeraNegativo);
            return;
        }
        if (moduleId == "estoque_importar_xml")
        {
            var view = new NfeImportModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }
        if (moduleId == "estoque_inventario")
        {
            var view = new InventoryModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }
        if (moduleId == "estoque_curva_abc")
        {
            var view = new StockAbcModuleView();
            view.CloseRequested += (_, _) => ShowHome();
            MainContent.Content = view;
            UpdateToolbarHighlight();
            return;
        }

        if (moduleId == "pdv")
        {
            try
            {
                if (StoreNetworkMode.IsPdvSalesBlockedOnClient)
                {
                    if (!AccessControl.Ensure("PdvResumoDia", "ver o resumo do dia no PDV", this))
                        return;
                    var resumo = new PdvResumoDiaWindow { Owner = this };
                    resumo.ShowDialog();
                }
                else
                {
                    var pdv = new PdvWindow { Owner = this };
                    pdv.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Não foi possível abrir o PDV:\n\n{ex.Message}",
                    "PDV — Erro",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            UpdateToolbarHighlight();
            return;
        }

        if (!Modules.TryGetValue(moduleId, out var info))
            return;

        var placeholder = new ModulePlaceholderView
        {
            ModuleTitle = info.Title,
            ModuleBody = info.Body + $"\n\nUsuário: {_user.Nome}\nBanco: {DatabaseService.DatabasePath}",
        };
        placeholder.CloseRequested += (_, _) => ShowHome();
        MainContent.Content = placeholder;
        UpdateToolbarHighlight();
    }

    private void ShowEstoqueCatalog(ProductCatalogKind kind)
    {
        var view = new CatalogModuleView(kind);
        view.CloseRequested += (_, _) => ShowHome();
        MainContent.Content = view;
        UpdateToolbarHighlight();
    }

    private void ShowEstoqueAdjust(StockAdjustMode mode)
    {
        var view = new StockAdjustModuleView(mode);
        view.CloseRequested += (_, _) => ShowHome();
        MainContent.Content = view;
        UpdateToolbarHighlight();
    }

    private void ShowEstoqueReport(StockReportKind kind)
    {
        var view = new StockReportModuleView(kind);
        view.CloseRequested += (_, _) => ShowHome();
        MainContent.Content = view;
        UpdateToolbarHighlight();
    }

    private void UpdateToolbarHighlight()
    {
        SetToolbarActive(BtnClientes, "clientes");
        SetToolbarActive(BtnProdutos, "produtos");
        SetToolbarActive(BtnCaixa, "caixa");
        SetToolbarActive(BtnRelatorio, "relatorio");
        SetToolbarActive(BtnPagar, "pagar");
        SetToolbarActive(BtnFiado, "fiado");
        SetToolbarActive(BtnVasilhame, "vasilhame");
        SetToolbarActive(BtnCompras, "compras");
        SetToolbarActive(BtnPdv, "pdv");
        SetToolbarActive(BtnDecks, "decks");
        SetToolbarActive(BtnMeuNegocio, "inicio");
        SetToolbarActive(BtnFechar, "home");
    }

    private void SetToolbarActive(Button btn, string moduleId) =>
        btn.Tag = _activeModule == moduleId ? "active" : moduleId;

    private void Toolbar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        var moduleId = btn.Tag as string;
        if (moduleId == "active")
            moduleId = GetModuleId(btn);

        if (moduleId == "home")
            ShowHome();
        else if (!string.IsNullOrEmpty(moduleId))
            ShowModule(moduleId);
    }

    private void MenuModule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string moduleId)
            ShowModule(moduleId);
    }

    private void ApplyPermissionUi()
    {
        SetToolbarPermission(BtnClientes, "clientes");
        SetToolbarPermission(BtnProdutos, "produtos");
        SetToolbarPermission(BtnCaixa, "caixa");
        SetToolbarPermission(BtnRelatorio, "relatorio");
        SetToolbarPermission(BtnPagar, "pagar");
        SetToolbarPermission(BtnFiado, "fiado");
        SetToolbarPermission(BtnVasilhame, "vasilhame");
        SetToolbarPermission(BtnCompras, "compras");
        SetToolbarPermission(BtnPdv, "pdv");
        SetToolbarPermission(BtnDecks, "decks");
        SetToolbarPermission(BtnMeuNegocio, "inicio");

        foreach (var item in MainMenu.Items)
        {
            if (item is MenuItem mi)
                ApplyMenuItemPermission(mi);
        }
    }

    private static void SetToolbarPermission(Button btn, string moduleId)
    {
        var allowed = AccessControl.CanAccessModule(moduleId);
        btn.IsEnabled = allowed;
        btn.Opacity = allowed ? 1 : 0.35;
        btn.ToolTip = allowed ? null : "Sem permissão para este módulo";
    }

    private static void ApplyMenuItemPermission(MenuItem mi)
    {
        if (mi.Tag is string moduleId && !string.IsNullOrWhiteSpace(moduleId))
        {
            if ((moduleId == "usuarios" || moduleId == LegacyMergeCleanupRules.ModuleId)
                && StoreNetworkMode.IsModuleBlockedOnClient(moduleId))
            {
                mi.IsEnabled = false;
                if (moduleId == "usuarios")
                    mi.Visibility = Visibility.Collapsed;
                mi.ToolTip = moduleId == "usuarios"
                    ? ApplicationLoginService.LocalUserAdministrationMessage
                    : LegacyMergeCleanupRules.ClientBlockedMessage;
                return;
            }

            var allowed = AccessControl.CanAccessModule(moduleId);
            mi.IsEnabled = allowed;
            mi.Visibility = Visibility.Visible;
            mi.ToolTip = allowed ? null : "Sem permissão";
        }

        foreach (var sub in mi.Items)
        {
            if (sub is MenuItem nested)
                ApplyMenuItemPermission(nested);
        }
    }

    private void MenuRedeLoja_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new StoreNetworkWindow { Owner = this };
        dlg.ShowDialog();
        RefreshCompanyTitle();
    }

    private async void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await AutoUpdateService.CheckAndOfferUpdateAsync(this, notifyResult: true)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Não foi possível verificar atualização:\n\n{ex.Message}",
                "SGDB — Atualização",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        var ver = FormatAppVersion(AutoUpdateService.GetCurrentVersion());
        MessageBox.Show(
            $"SGDB — Sistema de Gestão de Depósito de Bebidas\n\n" +
            $"Versão: {ver}\n\n" +
            "Versão nativa Windows (sem navegador).",
            "Sobre o SGDB",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private string GetModuleId(Button btn)
    {
        if (btn == BtnClientes) return "clientes";
        if (btn == BtnProdutos) return "produtos";
        if (btn == BtnCaixa) return "caixa";
        if (btn == BtnRelatorio) return "relatorio";
        if (btn == BtnPagar) return "pagar";
        if (btn == BtnFiado) return "fiado";
        if (btn == BtnVasilhame) return "vasilhame";
        if (btn == BtnCompras) return "compras";
        if (btn == BtnPdv) return "pdv";
        if (btn == BtnDecks) return "decks";
        if (btn == BtnMeuNegocio) return "inicio";
        return "home";
    }

    internal void BeginLogoutClose()
    {
        _isLoggingOut = true;
        Close();
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        AuditService.Log("logout", "sessao", _user.Id.ToString(), _user.Login);
        ApplicationLoginService.Logout();
        var login = new LoginWindow();
        if (login.ShowDialog() == true && login.AuthenticatedUser is not null)
        {
            var next = login.AuthenticatedUser;
            if (System.Windows.Application.Current is App app
                && app.CompleteInteractiveLogin(next, login.TypedPassword))
            {
                var main = new MainWindow(next);
                System.Windows.Application.Current.MainWindow = main;
                main.Show();
                BeginLogoutClose();
                return;
            }

            System.Windows.Application.Current.Shutdown();
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }
}
