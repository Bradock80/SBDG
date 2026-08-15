using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PdvPaymentWindow : Window
{
    private readonly double _subtotal;
    private readonly IReadOnlyList<PdvCartLine> _cartLines;
    private readonly ObservableCollection<PayMethodRow> _methods = new();
    private readonly Dictionary<string, double> _amounts = new(StringComparer.OrdinalIgnoreCase);
    private Person? _selectedCliente;
    private double _discount;
    private double _surcharge;
    private double _discountPct;
    private double _tableSurcharge;
    private double _fiadoUnitExtra;
    private bool _manualSurcharge;
    private bool _syncingDiscount;
    private bool _syncingSurcharge;
    private bool _suppressSelection;
    private bool _inputTouched;
    private bool _preferRemainingOnNextFill;
    private string? _editingRowId;
    private string? _editingText;
    private double _cashReceivedInput;
    private string _selectedId = "dinheiro";

    public string SelectedPaymentType { get; private set; } = "Dinheiro";
    public IReadOnlyList<PdvPaymentPart> Payments { get; private set; } = [];
    public double Discount => _discount;
    public double Surcharge => _surcharge;
    public double CashReceived { get; private set; }
    public int? CustomerPersonId { get; private set; }
    public int? SellerId { get; private set; }
    public bool Confirmed { get; private set; }

    /// <summary>Valor já recebido via PIX Mercado Pago nesta venda (0 se não houve).</summary>
    public double PixPaidAmount { get; private set; }

    /// <summary>Id do pagamento no Mercado Pago, quando houver.</summary>
    public long? PixPaymentId { get; private set; }

    public PdvPaymentWindow(double subtotal, IReadOnlyList<PdvCartLine>? cartLines = null)
    {
        _subtotal = subtotal;
        _cartLines = cartLines ?? [];
        InitializeComponent();

        foreach (var m in PaymentMethodsService.ListForPdv())
        {
            var tecla = string.IsNullOrWhiteSpace(m.PdvKey) ? "—" : m.PdvKey.ToUpperInvariant();
            var nome = m.ApiLabel;
            // Fiado = À Prazo no cadastro; no PDV sempre tecla E e rótulo "Fiado"
            if (m.Id == "fiado")
            {
                if (tecla is "—" or "")
                    tecla = "E";
                nome = "Fiado";
            }
            _methods.Add(new PayMethodRow(tecla, m.Id, nome));
        }

        // Garante Fiado (E) quando ativo no cadastro (À Prazo), mesmo se faltar na lista
        EnsureFiadoRowPresent();

        if (_methods.Count == 0)
        {
            // Fallback de segurança se o banco estiver vazio
            _methods.Add(new PayMethodRow("A", "dinheiro", "Dinheiro"));
            _methods.Add(new PayMethodRow("B", "debito", "Cartão Débito"));
            _methods.Add(new PayMethodRow("C", "credito", "Cartão Crédito"));
            _methods.Add(new PayMethodRow("D", "pix", "PIX QR CODE"));
            _methods.Add(new PayMethodRow("E", "fiado", "Fiado"));
        }
        foreach (var m in _methods)
            _amounts[m.Id] = 0;

        if (_methods.All(m => m.Id != "dinheiro"))
            _selectedId = _methods[0].Id;

        MethodsGrid.ItemsSource = _methods;
        LoadSellers();
        DescontoValBox.Text = CurrencyBr(0);
        AcrescimoValBox.Text = CurrencyBr(0);

        WireMoneyField(DescontoPctBox);
        WireMoneyField(DescontoValBox);
        WireMoneyField(AcrescimoPctBox);
        WireMoneyField(AcrescimoValBox);

        ApplyDiscountPermission();

        RefreshTotalPagar();
        // Igual ao gestão: abre com amounts zerados; total em Dinheiro é só sugestão até editar/Enter.
        OpenOnDinheiroSuggestion();
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        if (Height > MaxHeight)
            Height = MaxHeight;
        Loaded += (_, _) => FocusValorCell();
    }

    /// <summary>
    /// À Prazo no cadastro = Fiado no PDV (tecla E). Inclui se estiver ativo e ainda não estiver na lista.
    /// </summary>
    private void EnsureFiadoRowPresent()
    {
        if (_methods.Any(m => m.Id == "fiado"))
            return;

        var fiado = PaymentMethodsService.GetById("fiado");
        if (fiado is null || !fiado.Active)
            return;

        var tecla = string.IsNullOrWhiteSpace(fiado.PdvKey) ? "E" : fiado.PdvKey.ToUpperInvariant();
        var insertAt = _methods.Count;
        for (var i = 0; i < _methods.Count; i++)
        {
            if (string.CompareOrdinal(_methods[i].Tecla, tecla) > 0)
            {
                insertAt = i;
                break;
            }
        }
        _methods.Insert(insertAt, new PayMethodRow(tecla, "fiado", "Fiado"));
    }

    private void ApplyDiscountPermission()
    {
        var can = AccessControl.Can("PdvDesconto");
        DescontoPctBox.IsEnabled = can;
        DescontoValBox.IsEnabled = can;
        if (!can)
        {
            DescontoPctBox.ToolTip = "Sem permissão para desconto";
            DescontoValBox.ToolTip = "Sem permissão para desconto";
            DescontoPctBox.Opacity = 0.5;
            DescontoValBox.Opacity = 0.5;
        }
    }

    private void LoadSellers()
    {
        var list = new List<Seller> { new() { Id = 0, Name = "— Sem vendedor —" } };
        try
        {
            list.AddRange(SellersService.ListForPdv());
        }
        catch
        {
            // tabela pode não existir ainda em DB antigo
        }
        SellerBox.ItemsSource = list;
        SellerBox.SelectedIndex = 0;
    }

    private void ApplyTableSurchargeIfNeeded()
    {
        if (_cartLines.Count == 0)
        {
            _tableSurcharge = 0;
            _fiadoUnitExtra = CalcFiadoUnitExtra();
            SyncAutoSurcharge();
            return;
        }

        var amounts = BuildEffectivePaymentAmountsForPricing();
        _tableSurcharge = PriceTablesService.CalcCartSurchargeAllocated(
            _cartLines.Select(c => (c.ProductId, c.UnitPrice, c.Quantity, c.StockUnitsPerSale)),
            amounts);
        _fiadoUnitExtra = CalcFiadoUnitExtra();
        SyncAutoSurcharge();
    }

    /// <summary>
    /// Acréscimo por unidade do cliente (cadastro), só quando a venda tem fiado.
    /// Ex.: 10 un × R$ 0,50 = R$ 5,00.
    /// </summary>
    private double CalcFiadoUnitExtra()
    {
        if (!NeedsCliente())
            return 0;
        if (_selectedCliente is null || _selectedCliente.FiadoUnitSurcharge < 0.009)
            return 0;
        if (_cartLines.Count == 0)
            return 0;

        var units = _cartLines.Sum(c => c.Quantity);
        if (units < 0.0001)
            return 0;
        return ProductPriceHelper.RoundPrice(units * _selectedCliente.FiadoUnitSurcharge);
    }

    private void SyncAutoSurcharge()
    {
        var autoTotal = ProductPriceHelper.RoundPrice(_tableSurcharge + _fiadoUnitExtra);

        // Se o operador não editou o acréscimo manualmente, sincroniza com tabela + fiado/unidade
        if (!_manualSurcharge
            || _surcharge < autoTotal - 0.009
            || Math.Abs(_surcharge - autoTotal) < 0.02)
        {
            _syncingSurcharge = true;
            _manualSurcharge = false;
            _surcharge = autoTotal;
            AcrescimoValBox.Text = CurrencyBr(_surcharge);
            var pct = _subtotal > 0.009
                ? ProductPriceHelper.RoundPrice(_surcharge / _subtotal * 100)
                : 0;
            AcrescimoPctBox.Text = ProductPriceHelper.FormatBr(pct);
            _syncingSurcharge = false;
        }

        UpdateFiadoUnitHint();
    }

    private void UpdateFiadoUnitHint()
    {
        if (ClienteNomeText is null)
            return;

        if (_selectedCliente is null)
            return;

        if (_fiadoUnitExtra > 0.009)
        {
            var rate = ProductPriceHelper.FormatBr(_selectedCliente.FiadoUnitSurcharge);
            var extra = CurrencyBr(_fiadoUnitExtra);
            ClienteNomeText.Text = $"{_selectedCliente.Name}  ·  +R$ {rate}/un = {extra}";
        }
        else if (CustomerPersonId == _selectedCliente.Id)
        {
            ClienteNomeText.Text = _selectedCliente.Name;
        }
    }

    /// <summary>
    /// Monta o mapa de valores por forma para o cálculo do acréscimo.
    /// O restante ainda não digitado é atribuído à forma selecionada.
    /// </summary>
    private Dictionary<string, double> BuildEffectivePaymentAmountsForPricing()
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _methods)
        {
            var amt = _amounts.GetValueOrDefault(m.Id);
            if (amt > 0.009)
                map[m.Id] = amt;
        }

        // Base sem acréscimo (evita circularidade no cálculo)
        var baseTotal = ProductPriceHelper.RoundPrice(Math.Max(0, _subtotal - _discount));
        if (baseTotal <= 0.009)
            return map;

        if (map.Count == 0)
        {
            map[_selectedId is { Length: > 0 } ? _selectedId : "dinheiro"] = baseTotal;
            return map;
        }

        // Se o usuário está digitando na forma atual, usa o rascunho
        if (!string.IsNullOrEmpty(_selectedId))
        {
            var draft = DraftInputAmount();
            var others = map
                .Where(kv => !kv.Key.Equals(_selectedId, StringComparison.OrdinalIgnoreCase))
                .Sum(kv => kv.Value);
            others = ProductPriceHelper.RoundPrice(others);
            var maxSel = Math.Max(0, ProductPriceHelper.RoundPrice(baseTotal - others));
            if (_inputTouched && draft > 0.009)
                map[_selectedId] = Math.Min(Math.Max(0, draft), maxSel);
            else if (!map.ContainsKey(_selectedId))
            {
                var missing = ProductPriceHelper.RoundPrice(baseTotal - map.Values.Sum());
                if (missing > 0.009)
                    map[_selectedId] = missing;
            }
        }

        // Qualquer restante sem forma → forma selecionada (ou dinheiro)
        var allocated = ProductPriceHelper.RoundPrice(map.Values.Sum());
        var gap = ProductPriceHelper.RoundPrice(baseTotal - allocated);
        if (gap > 0.009)
        {
            var key = !string.IsNullOrEmpty(_selectedId) ? _selectedId : "dinheiro";
            map[key] = ProductPriceHelper.RoundPrice(map.GetValueOrDefault(key) + gap);
        }

        return map;
    }

    private double TotalAPagar() =>
        ProductPriceHelper.RoundPrice(Math.Max(0, _subtotal - _discount + _surcharge));

    private PayMethodRow? SelectedRow =>
        _methods.FirstOrDefault(m => m.Id == _selectedId) ?? MethodsGrid.SelectedItem as PayMethodRow;

    private static string CurrencyBr(double value) =>
        $"R$ {ProductPriceHelper.FormatBr(value)}";

    private void RefreshTotalPagar() =>
        TotalPagarText.Text = CurrencyBr(TotalAPagar());

    private double SumAmounts(string? excludeId = null)
    {
        double sum = 0;
        foreach (var m in _methods)
        {
            if (excludeId != null && m.Id == excludeId)
                continue;
            sum += _amounts.GetValueOrDefault(m.Id);
        }
        return ProductPriceHelper.RoundPrice(sum);
    }

    private void CaptureFocusedEdit()
    {
        if (Keyboard.FocusedElement is TextBox tb && tb.Tag is PayMethodRow row)
        {
            _editingRowId = row.Id;
            _editingText = tb.Text;
            if (row.Id == _selectedId)
            {
                row.ValorText = tb.Text;
                _inputTouched = true;
            }
        }
    }

    /// <summary>Grava célula em edição antes de trocar forma (A–E) ou concluir.</summary>
    private void EndEditAndCapture()
    {
        try
        {
            if (MethodsGrid.IsKeyboardFocusWithin)
                MethodsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        catch
        {
            // ignore — célula pode não estar em edição
        }

        CaptureFocusedEdit();

        if (SelectedRow is not { } row)
            return;

        var amt = DraftInputAmount();
        if (amt > 0.009 && (_inputTouched || !string.IsNullOrWhiteSpace(_editingText)))
            row.ValorText = CurrencyBr(amt);
    }

    private double DraftInputAmount()
    {
        var row = SelectedRow;
        if (row is null)
            return 0;

        CaptureFocusedEdit();

        if (_editingRowId == row.Id && !string.IsNullOrWhiteSpace(_editingText))
            return ProductPriceHelper.ParseBr(_editingText);

        return ProductPriceHelper.ParseBr(row.ValorText);
    }

    /// <summary>
    /// Valor que o cliente entregou em espécie (pode ser maior que a parte em dinheiro = troco).
    /// Prefere o buffer interno, porque após o commit a célula mostra só a parte alocada.
    /// </summary>
    private double EffectiveCashReceived()
    {
        var draft = DraftInputAmount();
        if (_cashReceivedInput > 0.009 && draft > 0.009)
            return Math.Max(_cashReceivedInput, draft);
        if (_cashReceivedInput > 0.009)
            return _cashReceivedInput;
        return draft;
    }

    private bool IsPureCashSale()
    {
        if (_amounts.GetValueOrDefault("fiado") > 0.009)
            return false;
        foreach (var m in _methods)
        {
            if (m.Id is "dinheiro" or "fiado")
                continue;
            if (_amounts.GetValueOrDefault(m.Id) > 0.009)
                return false;
        }

        // Dinheiro parcial (ex.: 7 de 14,50) = pagamento misto
        var total = TotalAPagar();
        var cash = _amounts.GetValueOrDefault("dinheiro");
        if (cash > 0.009 && cash + 0.02 < total)
            return false;

        return true;
    }

    private void ClearAllAmounts()
    {
        foreach (var m in _methods)
            _amounts[m.Id] = 0;
        _cashReceivedInput = 0;
        _inputTouched = false;
        _preferRemainingOnNextFill = false;
        _editingRowId = null;
        _editingText = null;
    }

    private void OpenOnDinheiroSuggestion()
    {
        ClearAllAmounts();
        _selectedId = "dinheiro";
        _manualSurcharge = false;
        _suppressSelection = true;
        MethodsGrid.SelectedIndex = 0;
        _suppressSelection = false;
        RefreshOtherRowsDisplay();
        UpdateClienteVisibility();
        UpdateTotals();
    }

    private void ResetToSingleMethod(string methodId)
    {
        ClearAllAmounts();
        _selectedId = methodId;
        var idx = _methods.ToList().FindIndex(m => m.Id == methodId);
        _suppressSelection = true;
        MethodsGrid.SelectedIndex = idx >= 0 ? idx : 0;
        _suppressSelection = false;
        RefreshOtherRowsDisplay();
        UpdateClienteVisibility();
        UpdateTotals();
    }

    private void RefreshOtherRowsDisplay()
    {
        foreach (var m in _methods)
        {
            if (m.Id == _selectedId)
                continue;
            var amt = _amounts.GetValueOrDefault(m.Id);
            m.ValorText = amt > 0.009 ? CurrencyBr(amt) : "";
        }
    }

    private void FillInputForMethod(PayMethodRow row)
    {
        _inputTouched = false;
        _editingRowId = null;
        _editingText = null;

        var remaining = ProductPriceHelper.RoundPrice(TotalAPagar() - SumAmounts(row.Id));
        var committed = _amounts.GetValueOrDefault(row.Id);

        // Após cancelar fiado: sugere o restante nesta forma (pode somar em dinheiro/débito de novo)
        if (_preferRemainingOnNextFill && remaining > 0.009)
        {
            _preferRemainingOnNextFill = false;
            row.ValorText = CurrencyBr(remaining);
            return;
        }

        if (row.Id == "dinheiro" && IsPureCashSale())
        {
            var show = _cashReceivedInput > 0.009
                ? _cashReceivedInput
                : (committed > 0.009 ? committed : TotalAPagar());
            row.ValorText = show > 0.009 ? CurrencyBr(show) : "";
            return;
        }

        if (committed > 0.009)
            row.ValorText = CurrencyBr(committed);
        else if (remaining > 0.009)
            row.ValorText = CurrencyBr(remaining);
        else
            row.ValorText = "";
    }

    private bool ShouldCommit(bool force) => force || _inputTouched;

    private void CommitCurrentInput(bool force = false)
    {
        EndEditAndCapture();
        if (!ShouldCommit(force))
            return;

        var row = SelectedRow;
        if (row is null)
            return;

        var inp = DraftInputAmount();
        var total = TotalAPagar();
        var others = SumAmounts(row.Id);

        if (row.Id == "fiado")
        {
            var maxAlloc = Math.Max(0, ProductPriceHelper.RoundPrice(total - others));
            _amounts["fiado"] = Math.Min(Math.Max(0, inp), maxAlloc);
            row.ValorText = _amounts["fiado"] > 0.009 ? CurrencyBr(_amounts["fiado"]) : "";
            return;
        }

        if (row.Id == "dinheiro")
        {
            var need = Math.Max(0, ProductPriceHelper.RoundPrice(total - others - _amounts.GetValueOrDefault("fiado")));
            // Após um commit anterior, ValorText mostra a parte alocada (need), não o
            // valor que o cliente deu. Não sobrescrever um recebimento maior (troco).
            if (IsPureCashSale()
                && _cashReceivedInput > need + 0.009
                && Math.Abs(inp - need) < 0.05)
            {
                // mantém _cashReceivedInput
            }
            else
            {
                _cashReceivedInput = Math.Max(0, inp);
            }

            // Parcial (ex.: R$ 7 de R$ 14,50) ou misto: grava exatamente o digitado (limitado ao need)
            if (others > 0.009 || _cashReceivedInput + 0.02 < need)
                _amounts["dinheiro"] = Math.Min(Math.Max(0, _cashReceivedInput), need);
            else if (IsPureCashSale() && _cashReceivedInput + 0.009 >= need)
                _amounts["dinheiro"] = need;
            else
                _amounts["dinheiro"] = Math.Min(Math.Max(0, _cashReceivedInput), need);

            // Em dinheiro puro com troco, continua mostrando o valor recebido.
            if (IsPureCashSale() && _cashReceivedInput > need + 0.009)
                row.ValorText = CurrencyBr(_cashReceivedInput);
            else
                row.ValorText = _amounts["dinheiro"] > 0.009 ? CurrencyBr(_amounts["dinheiro"]) : "";
            return;
        }

        var maxOther = Math.Max(0, ProductPriceHelper.RoundPrice(total - others - _amounts.GetValueOrDefault("fiado")));
        _amounts[row.Id] = Math.Min(Math.Max(0, inp), maxOther);
        row.ValorText = _amounts[row.Id] > 0.009 ? CurrencyBr(_amounts[row.Id]) : "";
    }

    /// <summary>Preenche o restante na forma selecionada (pagamento misto).</summary>
    private void AutoAllocateRemaining()
    {
        for (var pass = 0; pass < 4; pass++)
        {
            UpdateTotals();
            var total = TotalAPagar();
            var sum = SumAmounts();
            var remaining = ProductPriceHelper.RoundPrice(total - sum);
            if (remaining <= 0.009)
                return;
            if (sum <= 0.009)
                return;

            var target = SelectedRow
                ?? _methods.FirstOrDefault(m => _amounts.GetValueOrDefault(m.Id) < 0.009);
            if (target is null)
                return;

            _amounts[target.Id] = ProductPriceHelper.RoundPrice(_amounts.GetValueOrDefault(target.Id) + remaining);
        }
    }

    private bool CanConclude()
    {
        var total = TotalAPagar();
        if (total <= 0)
            return false;

        CommitCurrentInput(force: true);
        AutoAllocateRemaining();

        if (NeedsCliente() && CustomerPersonId is null or <= 0)
            return false;

        var allocated = SumAmounts();
        if (Math.Abs(allocated - total) < 0.02)
        {
            if (NeedsCliente())
                return CustomerPersonId is > 0;

            var activeNonFiado = _methods
                .Where(m => m.Id != "fiado" && _amounts.GetValueOrDefault(m.Id) > 0.009)
                .ToList();
            if (activeNonFiado.Count == 1 && activeNonFiado[0].Id == "dinheiro")
            {
                var received = IsPureCashSale() && _selectedId == "dinheiro"
                    ? DraftInputAmount()
                    : (_cashReceivedInput > 0 ? _cashReceivedInput : _amounts.GetValueOrDefault("dinheiro"));
                return received + 0.009 >= total;
            }
            return activeNonFiado.Count > 0;
        }

        if (allocated < 0.009 && _selectedId == "dinheiro")
            return DraftInputAmount() + 0.009 >= total;

        return false;
    }

    private bool TryAdvanceToNextMethod()
    {
        var total = TotalAPagar();
        var remaining = ProductPriceHelper.RoundPrice(total - SumAmounts());
        if (remaining <= 0.009)
            return false;

        var currentIdx = MethodsGrid.SelectedIndex;
        if (currentIdx < 0)
            currentIdx = _methods.ToList().FindIndex(m => m.Id == _selectedId);

        for (var step = 1; step < _methods.Count; step++)
        {
            var idx = (currentIdx + step) % _methods.Count;
            var candidate = _methods[idx];
            if (_amounts.GetValueOrDefault(candidate.Id) > 0.009)
                continue;

            SelectMethodByIndex(idx, skipCommit: true);
            return true;
        }

        return false;
    }

    private void MethodsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;
        if (MethodsGrid.SelectedItem is not PayMethodRow row)
            return;
        if (row.Id == _selectedId)
            return;

        // Troca de forma sem parcial: total vai inteiro para a nova forma (ex.: Dinheiro → Débito).
        if (!HasRealPartialPayment())
        {
            ResetToSingleMethod(row.Id);
            if (row.Id == "fiado")
                Dispatcher.BeginInvoke(FocusClienteSearch, DispatcherPriority.Input);
            else
                Dispatcher.BeginInvoke(FocusValorCell);
            return;
        }

        CommitCurrentInput(force: true);

        // Cancela fiado se saiu dele ou escolheu forma vazia para o restante (PIX/cartão)
        if (ShouldReleaseFiadoOnSwitch(row.Id))
            ClearFiadoAllocation();

        // PIX ainda não pago no MP: libera ao trocar de forma (evita valor "preso" no PIX)
        if (ShouldReleasePixOnSwitch(row.Id))
            ClearPixAllocationIfUnpaid();

        RefreshOtherRowsDisplay();

        _inputTouched = false;
        _editingRowId = null;
        _editingText = null;

        _selectedId = row.Id;
        _manualSurcharge = false; // troca de forma → reaplica tabela de preço
        UpdateClienteVisibility();
        UpdateTotals();
        if (row.Id == "fiado")
            Dispatcher.BeginInvoke(FocusClienteSearch, DispatcherPriority.Input);
        else
            Dispatcher.BeginInvoke(FocusValorCell);
    }

    /// <summary>
    /// Cancela a parte fiado e libera o valor para outra forma (PIX, cartão, dinheiro, débito…).
    /// Mantém os valores já informados nas outras formas; o restante pode somar nelas de novo.
    /// </summary>
    private void ClearFiadoAllocation()
    {
        if (_amounts.GetValueOrDefault("fiado") < 0.009)
        {
            var row = _methods.FirstOrDefault(m => m.Id == "fiado");
            if (row is not null && !string.IsNullOrWhiteSpace(row.ValorText))
                row.ValorText = "";
            return;
        }

        _amounts["fiado"] = 0;
        var fiadoRow = _methods.FirstOrDefault(m => m.Id == "fiado");
        if (fiadoRow is not null)
            fiadoRow.ValorText = "";
        _preferRemainingOnNextFill = true;
    }

    /// <summary>
    /// Libera fiado ao escolher qualquer outra forma (dinheiro, débito, crédito, PIX).
    /// </summary>
    private bool ShouldReleaseFiadoOnSwitch(string newMethodId)
    {
        if (newMethodId == "fiado")
            return false;
        return _amounts.GetValueOrDefault("fiado") > 0.009 || _selectedId == "fiado";
    }

    /// <summary>
    /// Cancela a parte PIX (ainda não confirmada no Mercado Pago) e libera o restante.
    /// </summary>
    private void ClearPixAllocationIfUnpaid()
    {
        if (PixPaidAmount > 0.009)
            return;

        var cleared = false;
        foreach (var m in _methods.Where(m => IsPixMethodId(m.Id)))
        {
            if (_amounts.GetValueOrDefault(m.Id) > 0.009)
            {
                _amounts[m.Id] = 0;
                cleared = true;
            }
            if (!string.IsNullOrWhiteSpace(m.ValorText))
                m.ValorText = "";
        }

        if (cleared)
            _preferRemainingOnNextFill = true;
    }

    private bool ShouldReleasePixOnSwitch(string newMethodId)
    {
        if (IsPixMethodId(newMethodId))
            return false;
        if (PixPaidAmount > 0.009)
            return false;
        if (IsPixMethodId(_selectedId))
            return true;
        return _methods.Any(m => IsPixMethodId(m.Id) && _amounts.GetValueOrDefault(m.Id) > 0.009);
    }

    private static bool IsPixMethodId(string? id) =>
        PaymentMethodsService.IsPixFamily(id);

    /// <summary>
    /// True quando o operador já informou valor parcial / misto.
    /// False = só sugestão do total (trocar A–E move o valor inteiro).
    /// </summary>
    private bool HasRealPartialPayment()
    {
        var total = TotalAPagar();
        if (total <= 0.009)
            return false;

        // Conta formas pagas agora (ignora fiado — ele pode ser cancelado e redistribuído)
        var fundedNow = _methods.Count(m =>
            m.Id != "fiado" && _amounts.GetValueOrDefault(m.Id) > 0.009);
        if (fundedNow > 1)
            return true;

        var sumNow = ProductPriceHelper.RoundPrice(
            _methods.Where(m => m.Id != "fiado")
                .Sum(m => _amounts.GetValueOrDefault(m.Id)));
        if (sumNow > 0.009 && sumNow + 0.02 < total)
            return true;

        if (FundedMethodCount() > 1)
            return true;

        var sum = SumAmounts();
        if (sum > 0.009 && sum + 0.02 < total)
            return true;

        // Digitando parcial na forma atual (ainda não commitado)
        if (_inputTouched || !string.IsNullOrWhiteSpace(_editingText))
        {
            var draft = DraftInputAmount();
            if (draft > 0.009 && draft + 0.02 < total)
                return true;
        }

        return false;
    }

    private bool NeedsCliente()
    {
        if (_selectedId == "fiado")
            return true;
        return _amounts.GetValueOrDefault("fiado") > 0.009;
    }

    private void UpdateClienteVisibility() =>
        ClientePanel.Visibility = NeedsCliente() ? Visibility.Visible : Visibility.Collapsed;

    private void MethodsGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditingElement is TextBox tb && e.Row.Item is PayMethodRow row)
        {
            _editingRowId = row.Id;
            _editingText = tb.Text;
            row.ValorText = tb.Text;
            if (row.Id == _selectedId)
                _inputTouched = true;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_inputTouched)
                CommitCurrentInput(force: true);
            _editingRowId = null;
            _editingText = null;
            RefreshOtherRowsDisplay();
            UpdateTotals();
        });
    }

    private void MethodsGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is not TextBox tb)
            return;
        if (e.Row.Item is PayMethodRow row)
        {
            tb.TextChanged -= PaymentValue_TextChanged;
            tb.TextChanged += PaymentValue_TextChanged;
            tb.PreviewTextInput -= PaymentValue_PreviewTextInput;
            tb.PreviewTextInput += PaymentValue_PreviewTextInput;
            tb.PreviewKeyDown -= PaymentValue_PreviewKeyDown;
            tb.PreviewKeyDown += PaymentValue_PreviewKeyDown;
            DataObject.RemovePastingHandler(tb, PaymentValue_Pasting);
            DataObject.AddPastingHandler(tb, PaymentValue_Pasting);
            InputMethod.SetIsInputMethodEnabled(tb, false);
            tb.Tag = row;
        }
        Dispatcher.BeginInvoke(() =>
        {
            tb.SelectAll();
            tb.Focus();
        });
    }

    private static void PaymentValue_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsMoneyChar(e.Text);
    }

    private static void PaymentValue_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private static void PaymentValue_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }
        var text = e.DataObject.GetData(typeof(string)) as string ?? "";
        if (string.IsNullOrEmpty(text) || text.Any(c => !IsMoneyChar(c.ToString())))
            e.CancelCommand();
    }

    private static bool IsMoneyChar(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (var c in text)
        {
            // Só números e separador decimal (sem espaço/letras)
            if (char.IsDigit(c) || c is ',' or '.')
                continue;
            return false;
        }
        return true;
    }

    private void PaymentValue_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Tag: PayMethodRow row } tb)
            return;
        _editingRowId = row.Id;
        _editingText = tb.Text;
        if (row.Id == _selectedId)
            _inputTouched = true;
        Dispatcher.BeginInvoke(UpdateTotals);
    }

    /// <summary>Soma commitado + valor visível em cada linha (inclui célula em edição).</summary>
    private double TotalAllocatedIncludingVisible()
    {
        CaptureFocusedEdit();

        double sum = 0;
        foreach (var m in _methods)
        {
            var committed = _amounts.GetValueOrDefault(m.Id);
            var visible = ProductPriceHelper.ParseBr(m.ValorText);

            if (_editingRowId == m.Id && !string.IsNullOrWhiteSpace(_editingText))
                visible = Math.Max(visible, ProductPriceHelper.ParseBr(_editingText));

            sum += Math.Max(committed, visible);
        }

        return ProductPriceHelper.RoundPrice(sum);
    }

    private void UpdateRestanteDisplay(double total)
    {
        var allocated = Math.Min(TotalAllocatedIncludingVisible(), total);
        var restante = ProductPriceHelper.RoundPrice(Math.Max(0, total - allocated));
        RestanteText.Text = CurrencyBr(restante);
    }

    private void UpdateTotals()
    {
        var totalBefore = TotalAPagar();
        ApplyTableSurchargeIfNeeded();
        RefreshTotalPagar();
        var total = TotalAPagar();

        // Se o acréscimo da tabela mudou o total, completa a diferença na forma
        // que já tem valor (ou atualiza a sugestão da forma selecionada).
        var gap = ProductPriceHelper.RoundPrice(total - totalBefore);
        if (Math.Abs(gap) > 0.009)
            AdjustAmountsForTotalGap(gap);

        var selected = SelectedRow;
        var draft = selected is not null ? DraftInputAmount() : 0;

        // Preenche sugestão do restante ANTES de calcular o rodapé
        if (!_inputTouched && selected is not null)
        {
            FillInputForMethod(selected);
            RefreshOtherRowsDisplay();
        }

        UpdateRestanteDisplay(total);

        double troco = 0;
        if (selected?.Id == "dinheiro" || (IsPureCashSale() && _amounts.GetValueOrDefault("dinheiro") > 0.009))
        {
            var received = selected?.Id == "dinheiro"
                ? Math.Max(_cashReceivedInput, draft)
                : _cashReceivedInput;
            if (received < 0.009 && selected?.Id == "dinheiro")
                received = draft;
            var dinheiroNeed = IsPureCashSale() && SumAmounts("dinheiro") < 0.009 && SumAmounts() < 0.009
                ? total
                : ProductPriceHelper.RoundPrice(Math.Max(0, total - SumAmounts("dinheiro")));
            if (IsPureCashSale() && _amounts.GetValueOrDefault("dinheiro") > 0.009)
                dinheiroNeed = _amounts.GetValueOrDefault("dinheiro");
            else if (IsPureCashSale())
                dinheiroNeed = total;
            if (received > dinheiroNeed + 0.009)
                troco = ProductPriceHelper.RoundPrice(received - dinheiroNeed);
        }

        TrocoText.Text = CurrencyBr(troco);
        UpdateClienteVisibility();
    }

    /// <summary>
    /// Quando a tabela de preço muda o total, só completa o valor se a forma única
    /// já cobria o total anterior (pagamento integral). Em pagamento misto/parcial,
    /// o restante vai naturalmente para a próxima forma.
    /// </summary>
    private void AdjustAmountsForTotalGap(double gap)
    {
        if (Math.Abs(gap) < 0.009)
            return;

        var funded = _methods
            .Where(m => _amounts.GetValueOrDefault(m.Id) > 0.009)
            .Select(m => m.Id)
            .ToList();

        if (funded.Count == 0)
            return;

        // Total diminuiu (desconto): reduz da última forma com valor
        if (gap < 0)
        {
            var trim = Math.Abs(gap);
            for (var i = funded.Count - 1; i >= 0 && trim > 0.009; i--)
            {
                var id = funded[i];
                if (id == "fiado")
                    continue;
                var amt = _amounts.GetValueOrDefault(id);
                var cut = Math.Min(amt, trim);
                _amounts[id] = ProductPriceHelper.RoundPrice(amt - cut);
                trim = ProductPriceHelper.RoundPrice(trim - cut);
            }
            return;
        }

        // Total aumentou (ex.: acréscimo cartão) — uma forma só: ajusta ela
        if (funded.Count == 1)
        {
            var id = funded[0];
            var amt = _amounts.GetValueOrDefault(id);
            var prevTotal = ProductPriceHelper.RoundPrice(TotalAPagar() - gap);
            if (Math.Abs(amt - prevTotal) < 0.05 || amt + 0.02 >= prevTotal)
                _amounts[id] = Math.Max(0, ProductPriceHelper.RoundPrice(amt + gap));
        }
        // Misto: restante aparece na forma selecionada (FillInputForMethod)
    }

    private void MoneyField_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !IsMoneyChar(e.Text);

    private void MoneyField_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private void MoneyField_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }
        var text = e.DataObject.GetData(typeof(string)) as string ?? "";
        if (string.IsNullOrEmpty(text) || text.Any(c => !IsMoneyChar(c.ToString())))
            e.CancelCommand();
    }

    private void WireMoneyField(TextBox box)
    {
        DataObject.AddPastingHandler(box, MoneyField_Pasting);
    }

    private void DescontoPct_LostFocus(object sender, RoutedEventArgs e) => ApplyDescontoFromPct();

    private void DescontoVal_LostFocus(object sender, RoutedEventArgs e) => ApplyDescontoFromVal();

    private void AcrescimoPct_LostFocus(object sender, RoutedEventArgs e) => ApplyAcrescimoFromPct();

    private void AcrescimoVal_LostFocus(object sender, RoutedEventArgs e) => ApplyAcrescimoFromVal();

    private void ApplyDescontoFromPct()
    {
        if (!AccessControl.Can("PdvDesconto"))
        {
            _discount = 0;
            _discountPct = 0;
            DescontoPctBox.Text = "0,00";
            DescontoValBox.Text = CurrencyBr(0);
            return;
        }
        if (_syncingDiscount) return;
        _syncingDiscount = true;
        _discountPct = ProductPriceHelper.ParseBr(DescontoPctBox.Text);
        _discount = ProductPriceHelper.RoundPrice(_subtotal * (_discountPct / 100.0));
        DescontoValBox.Text = CurrencyBr(_discount);
        _syncingDiscount = false;
        SyncAfterTotalChange();
    }

    private void ApplyDescontoFromVal()
    {
        if (!AccessControl.Can("PdvDesconto"))
        {
            _discount = 0;
            _discountPct = 0;
            DescontoPctBox.Text = "0,00";
            DescontoValBox.Text = CurrencyBr(0);
            return;
        }
        if (_syncingDiscount) return;
        _syncingDiscount = true;
        _discount = ProductPriceHelper.ParseBr(DescontoValBox.Text);
        _discountPct = _subtotal > 0 ? ProductPriceHelper.RoundPrice(_discount / _subtotal * 100) : 0;
        DescontoPctBox.Text = ProductPriceHelper.FormatBr(_discountPct);
        DescontoValBox.Text = CurrencyBr(_discount);
        _syncingDiscount = false;
        SyncAfterTotalChange();
    }

    private void ApplyAcrescimoFromPct()
    {
        if (_syncingSurcharge) return;
        _syncingSurcharge = true;
        var pct = ProductPriceHelper.ParseBr(AcrescimoPctBox.Text);
        _surcharge = ProductPriceHelper.RoundPrice(_subtotal * (pct / 100.0));
        var autoTotal = ProductPriceHelper.RoundPrice(_tableSurcharge + _fiadoUnitExtra);
        _manualSurcharge = Math.Abs(_surcharge - autoTotal) > 0.02;
        AcrescimoValBox.Text = CurrencyBr(_surcharge);
        _syncingSurcharge = false;
        SyncAfterTotalChange();
    }

    private void ApplyAcrescimoFromVal()
    {
        if (_syncingSurcharge) return;
        _syncingSurcharge = true;
        _surcharge = ProductPriceHelper.ParseBr(AcrescimoValBox.Text);
        var autoTotal = ProductPriceHelper.RoundPrice(_tableSurcharge + _fiadoUnitExtra);
        _manualSurcharge = Math.Abs(_surcharge - autoTotal) > 0.02;
        var pct = _subtotal > 0 ? ProductPriceHelper.RoundPrice(_surcharge / _subtotal * 100) : 0;
        AcrescimoPctBox.Text = ProductPriceHelper.FormatBr(pct);
        AcrescimoValBox.Text = CurrencyBr(_surcharge);
        _syncingSurcharge = false;
        SyncAfterTotalChange();
    }

    private bool IsDiscountOrSurchargeFocused()
    {
        var focused = Keyboard.FocusedElement;
        return focused == DescontoPctBox
            || focused == DescontoValBox
            || focused == AcrescimoPctBox
            || focused == AcrescimoValBox;
    }

    private void ApplyPendingDiscountOrSurcharge()
    {
        var focused = Keyboard.FocusedElement;
        if (focused == DescontoPctBox)
            ApplyDescontoFromPct();
        else if (focused == DescontoValBox)
            ApplyDescontoFromVal();
        else if (focused == AcrescimoPctBox)
            ApplyAcrescimoFromPct();
        else if (focused == AcrescimoValBox)
            ApplyAcrescimoFromVal();
        else
        {
            // Garante valores digitados mesmo se o foco já saiu do campo.
            ApplyDescontoFromVal();
            ApplyAcrescimoFromVal();
        }
    }

    private void FocusPaymentMethods()
    {
        ApplyPendingDiscountOrSurcharge();
        var idx = _methods.ToList().FindIndex(m => m.Id == _selectedId);
        if (idx < 0) idx = 0;
        _suppressSelection = true;
        MethodsGrid.SelectedIndex = idx;
        _suppressSelection = false;
        if (SelectedRow is not null)
            FillInputForMethod(SelectedRow);
        UpdateTotals();
        FocusValorCell();
    }

    private int FundedMethodCount() =>
        _methods.Count(m => _amounts.GetValueOrDefault(m.Id) > 0.009);

    private bool HasMixedPaymentInProgress()
    {
        var sum = SumAmounts();
        if (sum <= 0.009)
            return false;
        if (FundedMethodCount() > 1)
            return true;
        return Math.Abs(sum - TotalAPagar()) > 0.02;
    }

    private void SyncAfterTotalChange()
    {
        var keep = _selectedId;
        if (_amounts.GetValueOrDefault("fiado") > 0.009 || keep == "fiado")
            keep = "fiado";

        RefreshTotalPagar();

        // Pagamento misto/parcial: mantém dinheiro/cartão já digitados, mas
        // recalcula o que sobrou (principalmente PIX) quando o total muda (desconto).
        if (HasMixedPaymentInProgress())
        {
            RedistributeAfterTotalChange();
            RefreshOtherRowsDisplay();
            if (SelectedRow is not null)
                FillInputForMethod(SelectedRow);
            UpdateTotals();
            FocusValorCell();
            return;
        }

        ResetToSingleMethod(keep);
        FocusValorCell();
    }

    /// <summary>
    /// Após desconto/acréscimo: se a soma das formas passou do total, reduz primeiro o PIX
    /// (QR ainda não pago), depois outras formas (exceto fiado), por último o dinheiro.
    /// </summary>
    private void RedistributeAfterTotalChange()
    {
        var total = TotalAPagar();
        if (total < 0)
            total = 0;

        foreach (var m in _methods)
        {
            var amt = _amounts.GetValueOrDefault(m.Id);
            if (amt > total + 0.009)
                _amounts[m.Id] = total;
        }

        var sum = SumAmounts();
        if (sum <= total + 0.02)
            return;

        var trim = ProductPriceHelper.RoundPrice(sum - total);

        void TrimFrom(string id)
        {
            if (trim < 0.009)
                return;
            var amt = _amounts.GetValueOrDefault(id);
            if (amt < 0.009)
                return;
            var cut = Math.Min(amt, trim);
            _amounts[id] = ProductPriceHelper.RoundPrice(amt - cut);
            trim = ProductPriceHelper.RoundPrice(trim - cut);
        }

        // PIX não confirmado no MP: é o primeiro a absorver o desconto
        if (PixPaidAmount < 0.009)
        {
            foreach (var m in _methods.Where(m => IsPixMethodId(m.Id)))
                TrimFrom(m.Id);
        }

        foreach (var m in _methods
                     .Where(m => !IsPixMethodId(m.Id) && m.Id is not ("dinheiro" or "fiado"))
                     .Reverse())
            TrimFrom(m.Id);

        TrimFrom("dinheiro");

        // Se ainda passou (caso raro com PIX já pago), corta o PIX mesmo assim
        foreach (var m in _methods.Where(m => IsPixMethodId(m.Id)))
            TrimFrom(m.Id);
    }

    private bool _suppressClienteSearch;

    private void ClienteBusca_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressClienteSearch)
            return;

        var term = ClienteBuscaBox.Text.Trim();
        if (string.IsNullOrEmpty(term))
        {
            ClienteLookupGrid.Visibility = Visibility.Collapsed;
            ClienteLookupGrid.ItemsSource = null;
            _selectedCliente = null;
            CustomerPersonId = null;
            ClienteNomeText.Text = "— digite a 1ª letra —";
            _manualSurcharge = false;
            ApplyTableSurchargeIfNeeded();
            RefreshTotalPagar();
            return;
        }

        // Se o texto ainda é o cliente já escolhido, não reabre a lista nem limpa a seleção.
        if (_selectedCliente is not null
            && string.Equals(term, _selectedCliente.Name, StringComparison.OrdinalIgnoreCase)
            && CustomerPersonId == _selectedCliente.Id)
        {
            ClienteLookupGrid.Visibility = Visibility.Collapsed;
            ClienteNomeText.Text = _selectedCliente.Name;
            return;
        }

        var list = PersonService.List(term, tipo: "clientes");
        ClienteLookupGrid.ItemsSource = list;
        ClienteLookupGrid.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (list.Count > 0)
        {
            ClienteLookupGrid.SelectedIndex = 0;
            if (ClienteLookupGrid.SelectedItem is Person first)
                ClienteNomeText.Text = first.Name;
        }
        else
        {
            ClienteNomeText.Text = "— nenhum cliente encontrado —";
            _selectedCliente = null;
            CustomerPersonId = null;
            _manualSurcharge = false;
            ApplyTableSurchargeIfNeeded();
            RefreshTotalPagar();
        }
    }

    private void ClienteBusca_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            if (ClienteLookupGrid.Visibility == Visibility.Visible && ClienteLookupGrid.Items.Count > 0)
            {
                NavigateClienteWithArrows(e.Key);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter)
        {
            SelectCliente();
            e.Handled = true;
        }
    }

    private void ClienteLookupGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressClienteSearch)
            return;
        if (ClienteLookupGrid.SelectedItem is Person p)
            ClienteNomeText.Text = p.Name;
    }

    private void ClienteLookupGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => SelectCliente();

    private void ClienteLookupGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            NavigateClienteWithArrows(e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            SelectCliente();
            e.Handled = true;
        }
    }

    /// <summary>Navega a lista de clientes com ↑/↓ (busca fiado).</summary>
    private void NavigateClienteWithArrows(Key key)
    {
        if (ClienteLookupGrid.Visibility != Visibility.Visible || ClienteLookupGrid.Items.Count == 0)
            return;

        var count = ClienteLookupGrid.Items.Count;
        var idx = ClienteLookupGrid.SelectedIndex;
        if (idx < 0)
            idx = 0;
        else if (key == Key.Down && idx < count - 1)
            idx++;
        else if (key == Key.Up && idx > 0)
            idx--;

        ClienteLookupGrid.SelectedIndex = idx;
        if (ClienteLookupGrid.SelectedItem is not null)
            ClienteLookupGrid.ScrollIntoView(ClienteLookupGrid.SelectedItem);
        if (ClienteLookupGrid.SelectedItem is Person p)
            ClienteNomeText.Text = p.Name;
    }

    private void SelectCliente()
    {
        if (ClienteLookupGrid.SelectedItem is not Person p)
        {
            MessageBox.Show("Selecione um cliente na lista (digite o nome e pressione Enter).",
                "PDV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selectedCliente = p;
        CustomerPersonId = p.Id;
        _suppressClienteSearch = true;
        ClienteNomeText.Text = p.Name;
        ClienteBuscaBox.Text = p.Name;
        ClienteLookupGrid.Visibility = Visibility.Collapsed;
        ClienteLookupGrid.ItemsSource = null;
        _suppressClienteSearch = false;
        _manualSurcharge = false;
        ApplyTableSurchargeIfNeeded();
        RefreshTotalPagar();
        FocusValorCell();
    }

    private bool IsClienteSearchFocused()
    {
        if (ClientePanel.Visibility != Visibility.Visible)
            return false;
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused is null)
            return false;
        if (ReferenceEquals(focused, ClienteBuscaBox) || ClienteLookupGrid.IsKeyboardFocusWithin)
            return true;
        return IsWithin(focused, ClientePanel);
    }

    private static bool IsWithin(DependencyObject? child, DependencyObject parent)
    {
        while (child is not null)
        {
            if (ReferenceEquals(child, parent))
                return true;
            child = child is Visual or Visual3D
                ? VisualTreeHelper.GetParent(child)
                : LogicalTreeHelper.GetParent(child);
        }
        return false;
    }

    private void FocusClienteSearch()
    {
        UpdateClienteVisibility();
        if (ClientePanel.Visibility != Visibility.Visible)
            return;
        try
        {
            MethodsGrid.CancelEdit();
        }
        catch
        {
            // ignora se a grade não estiver em edição
        }

        ClienteBuscaBox.Focus();
        ClienteBuscaBox.SelectAll();
        Keyboard.Focus(ClienteBuscaBox);
    }

    private void FocusValorCell()
    {
        if (MethodsGrid.SelectedIndex < 0)
            return;
        MethodsGrid.CurrentCell = new DataGridCellInfo(MethodsGrid.SelectedItem, MethodsGrid.Columns[2]);
        MethodsGrid.BeginEdit();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => AttemptConcludeOrAdvance();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AttemptConcludeOrAdvance()
    {
        EndEditAndCapture();
        CommitCurrentInput(force: true);
        UpdateTotals();
        RefreshOtherRowsDisplay();
        if (SelectedRow is not null)
            FillInputForMethod(SelectedRow);
        UpdateTotals();

        if (NeedsCliente() && CustomerPersonId is null or <= 0)
        {
            var total = TotalAPagar();
            var sum = SumAmounts();
            if (Math.Abs(sum - total) < 0.02 || _selectedId == "fiado")
            {
                MessageBox.Show("Selecione o cliente para venda fiado (F2).", "PDV",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                FocusClienteSearch();
                return;
            }
        }

        if (CanConclude())
        {
            if (!TryConfirm())
                return;
            Confirmed = true;
            DialogResult = true;
            Close();
            return;
        }

        // Após parcial, avança para a próxima forma com o restante (melhoria sobre o gestão).
        if (TryAdvanceToNextMethod())
        {
            _inputTouched = false;
            _editingRowId = null;
            _editingText = null;
            if (SelectedRow is not null)
                FillInputForMethod(SelectedRow);
            UpdateTotals();
            FocusValorCell();
            return;
        }

        var totalPay = TotalAPagar();
        var sumPay = SumAmounts();
        var restante = ProductPriceHelper.RoundPrice(Math.Max(0, totalPay - sumPay));
        MessageBox.Show(
            restante > 0.009
                ? $"Faltam R$ {restante:N2}. Informe o valor e use outra forma (A–E)."
                : $"Pagamento incompleto.\nSoma: R$ {sumPay:N2} · Total: R$ {totalPay:N2}",
            "PDV",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        FocusValorCell();
    }

    private bool TryConfirm()
    {
        EndEditAndCapture();
        CommitCurrentInput(force: true);
        // Garante acréscimo da tabela com a qtd atual (ex.: 2 cigarros × R$ 1 = R$ 2)
        _manualSurcharge = false;
        ApplyTableSurchargeIfNeeded();
        RefreshTotalPagar();
        AutoAllocateRemaining();

        var total = TotalAPagar();
        var parts = new List<PdvPaymentPart>();

        foreach (var m in _methods)
        {
            var amt = _amounts.GetValueOrDefault(m.Id);
            if (amt > 0.009)
                parts.Add(new PdvPaymentPart { PaymentType = m.Nome, Amount = amt });
        }

        if (parts.Count == 0 && _selectedId == "dinheiro" && EffectiveCashReceived() + 0.009 >= total)
        {
            parts.Add(new PdvPaymentPart { PaymentType = "Dinheiro", Amount = total });
        }

        if (parts.Count == 0)
        {
            MessageBox.Show("Informe ao menos uma forma de pagamento.", "PDV",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        CashReceived = 0;
        if (parts.Count == 1 && parts[0].PaymentType == "Dinheiro")
        {
            var recv = EffectiveCashReceived();
            if (recv < total - 0.02)
            {
                MessageBox.Show("Valor em dinheiro insuficiente.", "PDV",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            parts.Clear();
            parts.Add(new PdvPaymentPart { PaymentType = "Dinheiro", Amount = total });
            if (recv > total + 0.009)
                CashReceived = recv;
        }
        else if (parts.Any(p => p.PaymentType == "Dinheiro"))
        {
            var dinheiroPart = parts.Where(p => p.PaymentType == "Dinheiro").Sum(p => p.Amount);
            var recv = EffectiveCashReceived();
            if (recv > dinheiroPart + 0.009)
                CashReceived = recv;
        }

        var sum = ProductPriceHelper.RoundPrice(parts.Sum(p => p.Amount));
        var diff = ProductPriceHelper.RoundPrice(total - sum);
        if (Math.Abs(diff) > 0.009)
        {
            // Ajuste fino (ex.: acréscimo cartão no cigarro após misto dinheiro + PIX)
            var idx = parts.FindIndex(p => p.PaymentType != "Dinheiro");
            if (idx < 0) idx = parts.Count - 1;
            parts[idx] = new PdvPaymentPart
            {
                PaymentType = parts[idx].PaymentType,
                Amount = ProductPriceHelper.RoundPrice(parts[idx].Amount + diff),
            };
            sum = ProductPriceHelper.RoundPrice(parts.Sum(p => p.Amount));
        }

        if (Math.Abs(sum - total) > 0.05)
        {
            MessageBox.Show($"Soma dos pagamentos (R$ {sum:N2}) difere do total (R$ {total:N2}).\n" +
                            "Informe valor parcial + Enter, depois complete na outra forma (A–E).",
                "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (parts.Any(p => p.PaymentType == "Fiado") && CustomerPersonId is null or <= 0)
        {
            MessageBox.Show("Selecione o cliente para venda fiado.", "PDV",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ClienteBuscaBox.Focus();
            return false;
        }

        var fiadoPart = parts.FirstOrDefault(p => p.PaymentType == "Fiado");
        if (fiadoPart is not null)
        {
            var clienteNome = _selectedCliente?.Name?.Trim();
            if (string.IsNullOrEmpty(clienteNome))
                clienteNome = ClienteNomeText.Text?.Trim();
            if (string.IsNullOrEmpty(clienteNome) || clienteNome.StartsWith("—"))
                clienteNome = "o cliente selecionado";

            var pagoAgora = ProductPriceHelper.RoundPrice(
                parts.Where(p => p.PaymentType != "Fiado").Sum(p => p.Amount));
            var msg = pagoAgora > 0.009
                ? $"Confirmar fiado de R$ {fiadoPart.Amount:N2} para:\n\n" +
                  $"    {clienteNome}\n\n" +
                  $"(Já pago agora: R$ {pagoAgora:N2} · Total da venda: R$ {total:N2})\n\n" +
                  "Deseja acrescentar essa dívida na conta fiado desta pessoa?"
                : $"Confirmar fiado de R$ {fiadoPart.Amount:N2} para:\n\n" +
                  $"    {clienteNome}\n\n" +
                  "Deseja acrescentar essa dívida na conta fiado desta pessoa?";

            var answer = MessageBox.Show(msg, "Confirmar Fiado",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                // Cancela fiado e devolve o restante (pode somar em dinheiro/débito de novo)
                ClearFiadoAllocation();
                _inputTouched = false;
                _editingRowId = null;
                _editingText = null;

                // Vai para a forma que já tem valor (dinheiro/débito) para somar o restante
                var topUpIdx = _methods.ToList().FindIndex(m =>
                    m.Id != "fiado" && _amounts.GetValueOrDefault(m.Id) > 0.009);
                if (topUpIdx < 0)
                    topUpIdx = _methods.ToList().FindIndex(m => m.Id == "dinheiro");
                if (topUpIdx >= 0)
                    SelectMethodByIndex(topUpIdx, skipCommit: true);
                else
                {
                    UpdateClienteVisibility();
                    UpdateTotals();
                    FocusValorCell();
                }

                MessageBox.Show(
                    "Fiado cancelado.\nO valor restante foi liberado — pode somar em dinheiro, débito, PIX ou outra forma (A–D).",
                    "PDV", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }

        Payments = parts;
        SelectedPaymentType = parts.Count == 1 ? parts[0].PaymentType : "Misto";
        if (SellerBox.SelectedItem is Seller seller && seller.Id > 0)
            SellerId = seller.Id;

        if (!AwaitMercadoPagoPixIfNeeded(parts))
            return false;

        return true;
    }

    private bool AwaitMercadoPagoPixIfNeeded(List<PdvPaymentPart> parts)
    {
        if (!MercadoPagoCredentials.IsPixEnabled())
            return true;

        var pixAmount = ProductPriceHelper.RoundPrice(
            parts.Where(p => PaymentMethodsService.RequiresMercadoPagoQr(null, p.PaymentType))
                .Sum(p => p.Amount));
        if (pixAmount < 0.01)
            return true;

        try
        {
            var dlg = new PdvPixQrWindow(pixAmount, $"Venda PDV R$ {pixAmount:N2}")
            {
                Owner = this,
            };
            if (dlg.ShowDialog() == true && dlg.PaidConfirmed)
            {
                PixPaidAmount = pixAmount;
                PixPaymentId = dlg.PaymentId;

                var idLine = dlg.PaymentId is long pid
                    ? $"\nID Mercado Pago: {pid}"
                    : "";
                MessageBox.Show(
                    "PIX CONFIRMADO PELO MERCADO PAGO.\n\n" +
                    $"Valor recebido: R$ {pixAmount:N2}" + idLine +
                    "\n\nA venda será finalizada.",
                    "PIX confirmado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return true;
            }

            MessageBox.Show(
                "Pagamento PIX não confirmado. A venda não foi finalizada.\n\n" +
                "Se precisar dar desconto: F4, ajuste o valor e Enter de novo para gerar outro QR.\n" +
                "Para mudar a forma: escolha Débito/Crédito/Dinheiro — o PIX solta o valor.",
                "PIX",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PIX Mercado Pago", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static bool IsPixPaymentPart(PdvPaymentPart p) =>
        PaymentMethodsService.RequiresMercadoPagoQr(null, p.PaymentType);

    private void SelectMethodByIndex(int idx, bool skipCommit = false)
    {
        if (idx < 0 || idx >= _methods.Count)
            return;

        var newId = _methods[idx].Id;
        if (newId == _selectedId && MethodsGrid.SelectedIndex == idx)
            return;

        if (!skipCommit)
        {
            EndEditAndCapture();

            // A–E sem parcial: limpa dinheiro e coloca o total na forma escolhida
            if (!HasRealPartialPayment())
            {
                ResetToSingleMethod(newId);
                if (newId == "fiado")
                    Dispatcher.BeginInvoke(FocusClienteSearch, DispatcherPriority.Input);
                else
                    FocusValorCell();
                return;
            }

            CommitCurrentInput(force: true);
        }

        // Saiu do fiado / escolheu forma vazia: libera o valor do fiado
        if (ShouldReleaseFiadoOnSwitch(newId))
            ClearFiadoAllocation();

        if (ShouldReleasePixOnSwitch(newId))
            ClearPixAllocationIfUnpaid();

        _inputTouched = false;
        _editingRowId = null;
        _editingText = null;

        RefreshOtherRowsDisplay();
        _selectedId = newId;
        _manualSurcharge = false;
        _suppressSelection = true;
        MethodsGrid.SelectedIndex = idx;
        _suppressSelection = false;
        UpdateClienteVisibility();
        UpdateTotals();

        if (_selectedId == "fiado")
            Dispatcher.BeginInvoke(FocusClienteSearch, DispatcherPriority.Input);
        else
            FocusValorCell();
    }

    private void FocusAndSelectAll(TextBox box)
    {
        try
        {
            MethodsGrid.CancelEdit();
        }
        catch
        {
            // ignora se a grade não estiver em edição
        }

        box.Focus();
        Keyboard.Focus(box);
        box.SelectAll();
        // WPF: SelectAll na hora do Focus às vezes não pega; reforça no próximo ciclo de input
        Dispatcher.BeginInvoke(() =>
        {
            if (!box.IsKeyboardFocusWithin)
            {
                box.Focus();
                Keyboard.Focus(box);
            }
            box.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F10)
        {
            AttemptConcludeOrAdvance();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (ClientePanel.Visibility == Visibility.Visible
                && (Keyboard.FocusedElement == ClienteBuscaBox || ClienteLookupGrid.IsKeyboardFocusWithin))
                return;

            // Em desconto/acréscimo, Enter aplica e volta para a forma de pagamento.
            if (IsDiscountOrSurchargeFocused())
            {
                FocusPaymentMethods();
                e.Handled = true;
                return;
            }

            AttemptConcludeOrAdvance();
            e.Handled = true;
            return;
        }

        if (e.Key is >= Key.A and <= Key.Z)
        {
            // Enquanto busca cliente (F2), A–Z são letras do nome — não troca a forma.
            if (IsClienteSearchFocused())
                return;

            var letter = (char)('a' + (e.Key - Key.A));
            var idx = _methods.ToList().FindIndex(m =>
                m.Tecla.Equals(letter.ToString(), StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                SelectMethodByIndex(idx);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.F4)
        {
            if (!AccessControl.Ensure("PdvDesconto", "dar desconto no balcão", this))
            {
                e.Handled = true;
                return;
            }
            FocusAndSelectAll(DescontoValBox);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            FocusAndSelectAll(AcrescimoValBox);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F8)
        {
            FocusPaymentMethods();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && ClientePanel.Visibility == Visibility.Visible)
        {
            FocusClienteSearch();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Up or Key.Down)
        {
            // Com fiado ativo / busca de cliente: ↑↓ navegam a lista — não trocam a forma (A–D).
            if (ClientePanel.Visibility == Visibility.Visible
                && (IsClienteSearchFocused()
                    || ClienteLookupGrid.Visibility == Visibility.Visible
                    || (NeedsCliente() && CustomerPersonId is null or <= 0)))
            {
                if (ClienteLookupGrid.Visibility == Visibility.Visible && ClienteLookupGrid.Items.Count > 0)
                    NavigateClienteWithArrows(e.Key);
                e.Handled = true;
                return;
            }

            var idx = MethodsGrid.SelectedIndex;
            if (e.Key == Key.Down && idx < _methods.Count - 1)
                SelectMethodByIndex(idx + 1);
            else if (e.Key == Key.Up && idx > 0)
                SelectMethodByIndex(idx - 1);
            e.Handled = true;
        }
    }

    private sealed class PayMethodRow : INotifyPropertyChanged
    {
        private string _valorText = "";

        public PayMethodRow(string tecla, string id, string nome)
        {
            Tecla = tecla;
            Id = id;
            Nome = nome;
        }

        public string Tecla { get; }
        public string Id { get; }
        public string Nome { get; }

        public string ValorText
        {
            get => _valorText;
            set
            {
                _valorText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
