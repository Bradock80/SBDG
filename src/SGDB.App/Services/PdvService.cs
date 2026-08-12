using Microsoft.Data.Sqlite;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class PdvService
{
    public static Product? FindProduct(string? term)
    {
        return ResolveScan(term)?.Product;
    }

    /// <summary>
    /// Resolve bipe: código da unidade = 1 un no preço avulso;
    /// código do fardo/maço = fator (ex. 20) no preço do maço.
    /// </summary>
    public static PdvScanResult? ResolveScan(string? term)
    {
        var t = (term ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            return null;

        using var conn = DatabaseService.OpenConnection();

        Product? product = null;
        foreach (var cand in BarcodeLookupTerms(t))
        {
            product = MatchBarcode(conn, cand);
            if (product is not null)
                break;
        }

        product ??= GetByCode(conn, t);
        product ??= SearchFirst(conn, t);
        if (product is null)
            return null;

        var extra = ProductExtra.Parse(product.ExtraJson);
        var scanned = new string(t.Where(char.IsDigit).ToArray());
        var unitDigits = new string((product.Barcode ?? "").Where(char.IsDigit).ToArray());
        var packDigits = new string((extra.BarcodeEmbalagem ?? "").Where(char.IsDigit).ToArray());

        // Só trata como caixa/fardo se o código bipado for o da embalagem
        // E for diferente do código da unidade (senão bipe unitário vira CX por engano).
        var matchesPack = packDigits.Length >= 4
            && scanned.Length >= 4
            && BarcodesEqual(packDigits, scanned);
        var matchesUnit = unitDigits.Length >= 4
            && scanned.Length >= 4
            && BarcodesEqual(unitDigits, scanned);
        var packDistinct = packDigits.Length >= 4
            && (unitDigits.Length < 4 || !BarcodesEqual(packDigits, unitDigits));
        var isPackScan = matchesPack && packDistinct && !matchesUnit;

        var packQty = extra.FatorEmbalagem >= 2
            ? extra.FatorEmbalagem
            : (extra.QtdAtacado >= 2 ? extra.QtdAtacado : 0);

        // Cigarro: qtd 1 (maço), preço do maço, estoque −20 por maço
        var cigarettePack = ProductClassificationHelper.IsCigarette(product.Name, product.GroupName)
                            && packQty >= 10
                            && product.SalePrice >= 5;

        if (cigarettePack && packQty >= 2)
        {
            var packPrice = extra.PrecoAtacado > 0 ? extra.PrecoAtacado : product.SalePrice;
            return new PdvScanResult
            {
                Product = product,
                Quantity = 1,
                UnitPrice = ProductPriceHelper.RoundPrice(packPrice),
                IsPackSale = true,
                ModeLabel = "MAÇO",
                StockUnitsPerSale = packQty,
            };
        }

        if (isPackScan && packQty >= 2)
        {
            var packTotal = extra.PrecoAtacado > 0
                ? extra.PrecoAtacado
                : product.SalePrice;
            var unitPrice = ProductPriceHelper.RoundPrice(packTotal / packQty);

            return new PdvScanResult
            {
                Product = product,
                Quantity = packQty,
                UnitPrice = unitPrice,
                IsPackSale = true,
                ModeLabel = "MAÇO/CX",
                StockUnitsPerSale = 1,
            };
        }

        return new PdvScanResult
        {
            Product = product,
            Quantity = 1,
            UnitPrice = product.SalePrice,
            IsPackSale = false,
            ModeLabel = null,
            StockUnitsPerSale = 1,
        };
    }

    /// <summary>
    /// Seleção manual no PDV: cigarro → qtd 1, preço maço, baixa fator no estoque.
    /// Avulso: produto "Varejo …".
    /// </summary>
    public static PdvScanResult ResolveManualSale(Product product)
    {
        var extra = ProductExtra.Parse(product.ExtraJson);
        var packQty = extra.FatorEmbalagem >= 2
            ? extra.FatorEmbalagem
            : (extra.QtdAtacado >= 2 ? extra.QtdAtacado : 0);

        var cigarettePack = ProductClassificationHelper.IsCigarette(product.Name, product.GroupName)
                            && packQty >= 10
                            && product.SalePrice >= 5;

        if (cigarettePack && packQty >= 2)
        {
            var packPrice = extra.PrecoAtacado > 0 ? extra.PrecoAtacado : product.SalePrice;
            return new PdvScanResult
            {
                Product = product,
                Quantity = 1,
                UnitPrice = ProductPriceHelper.RoundPrice(packPrice),
                IsPackSale = true,
                ModeLabel = "MAÇO",
                StockUnitsPerSale = packQty,
            };
        }

        return new PdvScanResult
        {
            Product = product,
            Quantity = 1,
            UnitPrice = product.SalePrice,
            IsPackSale = false,
            ModeLabel = null,
            StockUnitsPerSale = 1,
        };
    }

    public static double StockQuantityForSale(double displayQty, double stockUnitsPerSale) =>
        ProductPriceCalculator.StockQuantityForSale(displayQty, stockUnitsPerSale);

    /// <summary>
    /// Preço unitário conforme quantidade no PDV.
    /// Atacado: qty &gt;= Qtd. Atacado → Preço Atacado (unitário se ≤ venda; senão total ÷ qtd).
    /// </summary>
    public static double UnitPriceForQuantity(Product product, double qty)
    {
        var extra = ProductExtra.Parse(product.ExtraJson);
        if (qty <= 0)
            return product.SalePrice;

        var minAtac = extra.QtdAtacado;
        var precoAtac = extra.PrecoAtacado;
        var fator = extra.FatorEmbalagem >= 2 ? extra.FatorEmbalagem : 0;

        if (minAtac >= 2 && precoAtac > 0 && qty + 1e-9 >= minAtac)
            return WholesaleUnitPrice(product.SalePrice, precoAtac, minAtac);

        // Fardo sem qtd. atacado: preço especial só na qtd exata do fator
        if (fator >= 2 && precoAtac > 0 && Math.Abs(qty - fator) < 0.0001)
            return WholesaleUnitPrice(product.SalePrice, precoAtac, fator);

        return product.SalePrice;
    }

    /// <summary>
    /// Preço atacado cadastrado como unitário (≤ venda) ou como total do lote (&gt; venda).
    /// </summary>
    public static double WholesaleUnitPrice(double salePrice, double precoAtacado, double qtdLote) =>
        ProductPriceCalculator.WholesaleUnitPrice(salePrice, precoAtacado, qtdLote);

    public static IReadOnlyList<Product> SearchProducts(string? term, int limit = 25)
    {
        var t = (term ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            return [];

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var escaped = t.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products
            WHERE active = 1
              AND (
                UPPER(name) LIKE $like ESCAPE '\'
                OR UPPER(IFNULL(code,'')) LIKE $like ESCAPE '\'
                OR IFNULL(barcode,'') LIKE $like ESCAPE '\'
              )
            ORDER BY name
            LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$like", $"%{escaped.ToUpperInvariant()}%");
        cmd.Parameters.AddWithValue("$lim", limit);
        return ReadProducts(cmd);
    }

    /// <summary>
    /// Confere itens/estoque antes de cobrar o cliente (PIX, cartão…).
    /// Evita receber o dinheiro e a venda falhar depois.
    /// </summary>
    public static void ValidateItemsBeforePayment(IReadOnlyList<PdvCartLine> items)
    {
        if (items.Count == 0)
            throw new PdvException("Adicione pelo menos um produto à venda.");

        using var conn = DatabaseService.OpenConnection();
        foreach (var item in items)
        {
            if (item.Quantity <= 0)
                throw new PdvException("Quantidade inválida.");

            var product = LoadProduct(conn, null, item.ProductId)
                ?? throw new PdvException($"Produto #{item.ProductId} não encontrado.");
            if (!product.Active)
                throw new PdvException($"Produto inativo: {product.Name}");

            EnsureAvailableStock(product, item.StockQuantity);
        }
    }

    public static PdvFinalizeResult FinalizeSale(PdvFinalizeRequest request, DateTime? sessionDate = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("venda PDV");
        var d = (sessionDate ?? DateTime.Today).Date;

        using var conn = DatabaseService.OpenConnection();
        CashService.RequireOperational(conn, d);

        using var tx = conn.BeginTransaction();
        var result = FinalizeSaleCore(conn, tx, request, d);
        tx.Commit();
        return result;
    }

    /// <summary>
    /// Núcleo da finalização de venda usando conexão/transação existentes.
    /// Usado por FinalizeSale e pelo fechamento atômico de deck.
    /// </summary>
    internal static PdvFinalizeResult FinalizeSaleCore(
        SqliteConnection conn,
        SqliteTransaction tx,
        PdvFinalizeRequest request,
        DateTime sessionDate)
    {
        if (request.Items.Count == 0)
            throw new PdvException("Adicione pelo menos um produto à venda.");

        var d = sessionDate.Date;

        var lines = new List<(Product Product, double Qty, double StockQty, double UnitPrice, double Subtotal)>();
        double total = 0;

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
                throw new PdvException("Quantidade inválida.");

            var product = LoadProduct(conn, tx, item.ProductId);
            if (product is null)
                throw new PdvException($"Produto #{item.ProductId} não encontrado.");
            if (!product.Active)
                throw new PdvException($"Produto inativo: {product.Name}");

            var unitPrice = item.UnitPrice > 0 ? item.UnitPrice : product.SalePrice;
            if (unitPrice < 0)
                throw new PdvException("Preço inválido.");

            var stockQty = item.StockQuantity;
            EnsureAvailableStock(product, stockQty);

            var subtotal = ProductPriceHelper.RoundPrice(item.Quantity * unitPrice);
            lines.Add((product, item.Quantity, stockQty, unitPrice, subtotal));
            total += subtotal;
        }

        total = ProductPriceHelper.RoundPrice(total);
        var discount = ProductPriceHelper.RoundPrice(Math.Max(0, request.Discount));
        if (discount > 0.009 && !AccessControl.Can("PdvDesconto"))
            throw new PdvException("Sem permissão para desconto no balcão.");
        var surcharge = ProductPriceHelper.RoundPrice(Math.Max(0, request.Surcharge));
        total = ProductPriceHelper.RoundPrice(total - discount + surcharge);
        if (total <= 0)
            throw new PdvException("Total da venda deve ser maior que zero.");

        var paymentParts = NormalizePaymentParts(request.PaymentType, total, request.Payments);
        var fiadoTotal = paymentParts.Where(p => IsFiado(p.PaymentType)).Sum(p => p.Amount);
        if (fiadoTotal > 0.009)
        {
            if (request.CustomerPersonId is null or <= 0)
                throw new PdvException("Selecione o cliente para venda fiado.");
        }

        var paymentLabel = SalePaymentLabel(paymentParts);

        string? customerName = null;
        if (request.CustomerPersonId is > 0)
        {
            customerName = GetPersonName(conn, tx, request.CustomerPersonId.Value);
            if (customerName is null)
                throw new PdvException("Cliente não encontrado.");
        }

        var (cashReceived, changeAmount) = ResolveCashTroco(paymentParts, total, request.CashReceived);

        int saleId;
        using (var insSale = conn.CreateCommand())
        {
            insSale.Transaction = tx;
            insSale.CommandText = """
                INSERT INTO sales (
                  session_date, total, payment_type, customer_id, seller_id,
                  cash_received, change_amount, created_at
                ) VALUES ($date, $total, $pay, $cust, $seller, $recv, $chg, $created);
                SELECT last_insert_rowid();
                """;
            insSale.Parameters.AddWithValue("$date", d.ToString("yyyy-MM-dd"));
            insSale.Parameters.AddWithValue("$total", total);
            insSale.Parameters.AddWithValue("$pay", paymentLabel);
            insSale.Parameters.AddWithValue("$cust", (object?)request.CustomerPersonId ?? DBNull.Value);
            insSale.Parameters.AddWithValue("$seller", (object?)request.SellerId ?? DBNull.Value);
            insSale.Parameters.AddWithValue("$recv", (object?)cashReceived ?? DBNull.Value);
            insSale.Parameters.AddWithValue("$chg", changeAmount);
            insSale.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
            saleId = Convert.ToInt32(insSale.ExecuteScalar());
        }

        foreach (var (product, qty, stockQty, unitPrice, subtotal) in lines)
        {
            using var insItem = conn.CreateCommand();
            insItem.Transaction = tx;
            insItem.CommandText = """
                INSERT INTO sale_items (
                  sale_id, product_id, product_code, product_name, unit,
                  quantity, unit_price, subtotal, stock_qty
                ) VALUES ($sale, $pid, $code, $name, $unit, $qty, $price, $sub, $stock);
                """;
            insItem.Parameters.AddWithValue("$sale", saleId);
            insItem.Parameters.AddWithValue("$pid", product.Id);
            insItem.Parameters.AddWithValue("$code", product.Code ?? "");
            insItem.Parameters.AddWithValue("$name", product.Name);
            insItem.Parameters.AddWithValue("$unit", product.Unit);
            insItem.Parameters.AddWithValue("$qty", qty);
            insItem.Parameters.AddWithValue("$price", unitPrice);
            insItem.Parameters.AddWithValue("$sub", subtotal);
            insItem.Parameters.AddWithValue("$stock", stockQty);
            insItem.ExecuteNonQuery();

            var movements = ProductCompositionService.StockMovementsForSale(product, stockQty);
            foreach (var (comp, deduct) in movements)
            {
                StockService.ApplySaleDeduction(conn, tx, comp.Id, deduct,
                    notes: $"Venda Pedido #{saleId}",
                    refType: "sale", refId: saleId);
            }
        }

        var descPrefix = $"VENDA PDV #{saleId}";
        foreach (var part in paymentParts)
        {
            if (IsFiado(part.PaymentType))
            {
                var desc = $"{descPrefix} — FIADO R$ {part.Amount:F2}";
                if (!string.IsNullOrWhiteSpace(customerName))
                    desc = $"{descPrefix} — FIADO R$ {part.Amount:F2} — {customerName}";
                CashService.AddSalePaymentMovement(conn, tx, d, saleId, CashMovementKind.VendaFiado,
                    desc, part.PaymentType, part.Amount, customerName, affectsBalance: false);
            }
            else
            {
                var desc = paymentParts.Count > 1
                    ? $"{descPrefix} — {part.PaymentType} R$ {part.Amount:F2}"
                    : $"{descPrefix} — {part.PaymentType}";
                string? movNotes = null;
                if (IsDinheiro(part.PaymentType) && cashReceived is not null && changeAmount > 0.009
                    && Math.Abs(part.Amount - paymentParts.Where(p => IsDinheiro(p.PaymentType)).Sum(p => p.Amount)) < 0.02)
                {
                    desc = $"{desc} (recebido R$ {cashReceived:F2}, troco R$ {changeAmount:F2})";
                    movNotes = $"{{\"cash_received\":{cashReceived.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"change\":{changeAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
                }
                CashService.AddSalePaymentMovement(conn, tx, d, saleId, CashMovementKind.Venda,
                    desc, part.PaymentType, part.Amount, customerName, affectsBalance: true, movNotes);
            }
        }

        return new PdvFinalizeResult
        {
            SaleId = saleId,
            Total = total,
            ChangeAmount = changeAmount,
            CashReceived = cashReceived ?? 0,
        };
    }

    public static void CancelSale(int saleId, DateTime? sessionDate = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("cancelar venda");
        if (!AccessControl.Can("PdvCancelarVenda"))
            throw new PdvException("Sem permissão para cancelar venda do dia.");

        using var conn = DatabaseService.OpenConnection();
        var d = (sessionDate ?? DateTime.Today).Date;
        CashService.RequireOperational(conn, d);

        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, session_date, cancelled FROM sales WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new PdvException("Venda não encontrada.");
        if (reader.GetInt32(2) != 0)
            throw new PdvException("Venda já cancelada.");
        var saleDate = DateTime.Parse(reader.GetString(1)).Date;
        reader.Close();

        if (saleDate != d)
            throw new PdvException("Só é possível cancelar vendas de hoje com o caixa aberto.");

        double saleTotal;
        using (var totalCmd = conn.CreateCommand())
        {
            totalCmd.Transaction = tx;
            totalCmd.CommandText = "SELECT total FROM sales WHERE id = $id LIMIT 1;";
            totalCmd.Parameters.AddWithValue("$id", saleId);
            saleTotal = Convert.ToDouble(totalCmd.ExecuteScalar() ?? 0);
        }

        var saleItems = new List<(int ProductId, string Name, double Quantity, double StockQty, double UnitPrice)>();
        using (var items = conn.CreateCommand())
        {
            items.Transaction = tx;
            items.CommandText = """
                SELECT product_id, IFNULL(product_name,''), quantity, IFNULL(stock_qty,0), unit_price
                FROM sale_items WHERE sale_id = $id;
                """;
            items.Parameters.AddWithValue("$id", saleId);
            using var ir = items.ExecuteReader();
            while (ir.Read())
            {
                saleItems.Add((
                    ir.GetInt32(0),
                    ir.GetString(1),
                    ir.GetDouble(2),
                    ir.GetDouble(3),
                    ir.GetDouble(4)));
            }
        }

        foreach (var item in saleItems)
        {
            var product = LoadProduct(conn, tx, item.ProductId);
            if (product is null)
                continue;

            var restoreQty = item.StockQty > 0 ? item.StockQty : item.Quantity;
            foreach (var (comp, qtyBack) in ProductCompositionService.StockMovementsForSale(product, restoreQty))
            {
                StockService.ApplySaleRestore(conn, tx, comp.Id, qtyBack,
                    notes: $"Cancelamento Pedido #{saleId}",
                    refType: "sale_cancel", refId: saleId);
            }
        }

        CashService.DeleteSaleMovements(conn, tx, saleId);

        using (var updSale = conn.CreateCommand())
        {
            updSale.Transaction = tx;
            updSale.CommandText = "UPDATE sales SET cancelled = 1 WHERE id = $id;";
            updSale.Parameters.AddWithValue("$id", saleId);
            updSale.ExecuteNonQuery();
        }

        tx.Commit();

        var itemPayload = saleItems.Select(i => new
        {
            name = i.Name,
            qty = i.Quantity,
            unit_price = i.UnitPrice,
        }).ToList();
        var itemsSummary = string.Join(", ", saleItems.Select(i =>
            $"{i.Quantity:0.##}x {i.Name}"));
        AuditService.LogJson("cancelar", "venda", saleId.ToString(),
            AuditPayloadBuilder.SaleCancel(saleId, saleTotal, itemPayload),
            $"Venda #{saleId} de R$ {saleTotal:N2} cancelada" +
            (string.IsNullOrEmpty(itemsSummary) ? "" : $" · Itens: {itemsSummary}"));
    }

    /// <summary>
    /// Calcula o impacto do Swap sem gravar (estoque/pagamento intactos).
    /// </summary>
    public static PdvSwapItemPreview PreviewSwapSaleItem(
        int saleId, int itemId, int newProductId, bool keepLinePrice,
        double? newQuantity = null, DateTime? sessionDate = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("trocar item da venda");
        using var conn = DatabaseService.OpenConnection();
        var d = (sessionDate ?? DateTime.Today).Date;
        var plan = PlanSwapSaleItem(conn, tx: null, saleId, itemId, newProductId, keepLinePrice, newQuantity, d);
        var payments = PdvQueryService.LoadSalePayments(conn, saleId, plan.PaymentType, plan.OldTotal);
        var isPureFiado = IsPureFiadoPayment(payments);
        var difference = ProductPriceHelper.RoundPrice(plan.NewTotal - plan.OldTotal);
        var totalChanged = Math.Abs(difference) >= 0.01;
        var requiresConfirm = totalChanged && !isPureFiado;
        double? refundHint = null;
        if (totalChanged && !isPureFiado && difference < 0)
            refundHint = ProductPriceHelper.RoundPrice(-difference);

        return new PdvSwapItemPreview
        {
            SaleId = saleId,
            OldTotal = plan.OldTotal,
            OldGross = plan.OldGross,
            OriginalAdjustment = plan.OriginalAdjustment,
            NewGross = plan.NewGross,
            NewTotal = plan.NewTotal,
            Difference = difference,
            PaymentType = plan.PaymentType,
            CurrentPayments = payments,
            CustomerPersonId = plan.CustomerId,
            IsPureFiado = isPureFiado,
            RequiresPaymentConfirmation = requiresConfirm,
            RefundHint = refundHint,
        };
    }

    /// <summary>
    /// Troca item da venda do dia. Política de pagamento (ETAPA 24.5):
    /// total igual → preserva partes; fiado puro → ajusta dívida ao newTotal;
    /// demais formas com total diferente → exige <paramref name="confirmedPayments"/>.
    /// </summary>
    public static PdvSwapItemResult SwapSaleItem(
        int saleId, int itemId, int newProductId, bool keepLinePrice,
        double? newQuantity = null, DateTime? sessionDate = null,
        IReadOnlyList<PdvPaymentPart>? confirmedPayments = null,
        double cashReceived = 0,
        int? customerPersonId = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("trocar item da venda");
        if (!AccessControl.Can("PdvEditarVenda"))
            throw new PdvException("Sem permissão para editar / trocar item da venda.");

        using var conn = DatabaseService.OpenConnection();
        var d = (sessionDate ?? DateTime.Today).Date;
        using var tx = conn.BeginTransaction();

        var plan = PlanSwapSaleItem(conn, tx, saleId, itemId, newProductId, keepLinePrice, newQuantity, d);
        var oldPaymentsSnapshot = PdvQueryService.LoadSalePayments(conn, saleId, plan.PaymentType, plan.OldTotal);

        // Devolve estoque do produto antigo (stock_qty / composição — sem remultiplicar fator)
        if (plan.OldProduct is not null)
        {
            foreach (var (comp, qtyBack) in ProductCompositionService.StockMovementsForSale(plan.OldProduct, plan.OldStockQty))
            {
                StockService.ApplySaleRestore(conn, tx, comp.Id, qtyBack,
                    notes: $"Troca item Pedido #{saleId} (devolução)",
                    refType: "sale_edit", refId: saleId);
            }
        }

        // Baixa estoque do novo (após devolução, se for o mesmo produto o estoque já inclui o que voltou)
        var stockOk = LoadProduct(conn, tx, newProductId)
            ?? throw new PdvException("Produto não encontrado.");
        EnsureAvailableStock(stockOk, plan.NewStockQty);

        foreach (var (comp, qtyOut) in ProductCompositionService.StockMovementsForSale(stockOk, plan.NewStockQty))
        {
            StockService.ApplySaleDeduction(conn, tx, comp.Id, qtyOut,
                notes: $"Troca item Pedido #{saleId} (nova baixa)",
                refType: "sale_edit", refId: saleId);
        }

        using (var updItem = conn.CreateCommand())
        {
            updItem.Transaction = tx;
            updItem.CommandText = """
                UPDATE sale_items SET
                  product_id = $pid, product_code = $code, product_name = $name,
                  unit = $unit, quantity = $qty, unit_price = $price, subtotal = $sub,
                  stock_qty = $stock
                WHERE id = $id;
                """;
            updItem.Parameters.AddWithValue("$pid", plan.NewProduct.Id);
            updItem.Parameters.AddWithValue("$code", plan.NewProduct.Code ?? "");
            updItem.Parameters.AddWithValue("$name", plan.NewProduct.Name);
            updItem.Parameters.AddWithValue("$unit", plan.NewProduct.Unit);
            updItem.Parameters.AddWithValue("$qty", plan.Qty);
            updItem.Parameters.AddWithValue("$price", plan.UnitPrice);
            updItem.Parameters.AddWithValue("$sub", plan.LineSubtotal);
            updItem.Parameters.AddWithValue("$stock", plan.NewStockQty);
            updItem.Parameters.AddWithValue("$id", itemId);
            updItem.ExecuteNonQuery();
        }

        // Confere bruto pós-update (mesma fórmula do preview).
        var newGrossItemsTotal = SumSaleItemsSubtotal(conn, tx, saleId);
        var newTotal = ProductPriceHelper.RoundPrice(Math.Max(0, newGrossItemsTotal + plan.OriginalAdjustment));
        ApplySaleTotal(conn, tx, saleId, newTotal);

        var currentParts = oldPaymentsSnapshot;
        var difference = ProductPriceHelper.RoundPrice(newTotal - plan.OldTotal);
        var totalChanged = Math.Abs(difference) >= 0.01;
        var isPureFiado = IsPureFiadoPayment(currentParts);

        List<PdvPaymentPart> parts;
        double cashRecvInput;
        int? finalCustomerId = plan.CustomerId;

        if (!totalChanged)
        {
            parts = currentParts
                .Select(p => new PdvPaymentPart { PaymentType = p.PaymentType, Amount = p.Amount })
                .ToList();
            cashRecvInput = plan.CashReceived ?? 0;
        }
        else if (isPureFiado)
        {
            parts =
            [
                new PdvPaymentPart
                {
                    PaymentType = currentParts[0].PaymentType,
                    Amount = newTotal,
                },
            ];
            cashRecvInput = 0;
            finalCustomerId = plan.CustomerId;
        }
        else
        {
            if (confirmedPayments is null || confirmedPayments.Count == 0)
            {
                throw new PdvException(
                    "O total da venda mudou. Confirme a nova forma de pagamento antes de concluir a troca do item.");
            }

            if (newTotal <= 0.009)
            {
                // Total zero: não inventa misto; grava a forma informada com valor 0.
                parts =
                [
                    new PdvPaymentPart
                    {
                        PaymentType = NormalizePayment(confirmedPayments[0].PaymentType),
                        Amount = 0,
                    },
                ];
            }
            else
            {
                parts = NormalizePaymentParts(
                    confirmedPayments[0].PaymentType, newTotal, confirmedPayments);
            }

            cashRecvInput = cashReceived;
            finalCustomerId = customerPersonId ?? plan.CustomerId;

            var fiadoTotal = parts.Where(p => IsFiado(p.PaymentType)).Sum(p => p.Amount);
            if (fiadoTotal > 0.009)
            {
                if (finalCustomerId is null or <= 0)
                    throw new PdvException("Selecione o cliente para venda fiado.");
                _ = GetPersonName(conn, tx, finalCustomerId.Value)
                    ?? throw new PdvException("Cliente não encontrado.");
            }
        }

        double? refundHint = null;
        double? extraHint = null;
        if (totalChanged && !isPureFiado)
        {
            if (difference < 0)
                refundHint = ProductPriceHelper.RoundPrice(-difference);
            else if (difference > 0)
                extraHint = difference;
        }

        var (recv, change) = ResolveCashTroco(parts, newTotal, cashRecvInput);
        ApplySalePaymentUpdate(conn, tx, saleId, plan.SaleDate, parts, newTotal, recv, change, finalCustomerId);

        tx.Commit();

        var newPaymentType = SalePaymentLabel(parts);
        AuditService.LogJson(
            "trocar_item",
            "venda",
            saleId.ToString(),
            new
            {
                op = "trocar_item",
                sale_id = saleId,
                item_id = itemId,
                old_product_id = plan.OldProductId,
                new_product_id = plan.NewProduct.Id,
                old_quantity = plan.OldQty,
                new_quantity = plan.Qty,
                old_stock_qty = plan.OldStockQty,
                new_stock_qty = plan.NewStockQty,
                old_unit_price = plan.OldUnitPrice,
                new_unit_price = plan.UnitPrice,
                old_total = plan.OldTotal,
                new_total = newTotal,
                original_adjustment = plan.OriginalAdjustment,
                difference,
                keep_line_price = keepLinePrice,
                old_payment_type = plan.PaymentType,
                new_payment_type = newPaymentType,
                old_customer_id = plan.CustomerId,
                new_customer_id = finalCustomerId,
                old_payments = oldPaymentsSnapshot.Select(p => new { payment_type = p.PaymentType, amount = p.Amount }).ToList(),
                new_payments = parts.Select(p => new { payment_type = p.PaymentType, amount = p.Amount }).ToList(),
            },
            $"Troca item venda #{saleId}: produto {plan.OldProductId} → {plan.NewProduct.Id}");

        string msg;
        if (isPureFiado && totalChanged)
        {
            msg = $"Produto trocado. Fiado atualizado para R$ {newTotal:N2}.";
        }
        else if (refundHint is > 0.009)
        {
            msg = $"Produto trocado. Devolver/estornar R$ {refundHint:N2} ao cliente (conforme forma confirmada).";
        }
        else if (extraHint is > 0.009)
        {
            msg = $"Produto trocado. Diferença de R$ {extraHint:N2} confirmada no pagamento.";
        }
        else
        {
            msg = "Produto trocado — estoque e venda atualizados.";
        }

        return new PdvSwapItemResult
        {
            Sale = PdvQueryService.GetSaleDetail(saleId),
            RefundHint = refundHint,
            Message = msg,
        };
    }

    private sealed class SwapPlan
    {
        public required DateTime SaleDate { get; init; }
        public required double OldTotal { get; init; }
        public required double OldGross { get; init; }
        public required double OriginalAdjustment { get; init; }
        public required double NewGross { get; init; }
        public required double NewTotal { get; init; }
        public required string PaymentType { get; init; }
        public required int? CustomerId { get; init; }
        public required double? CashReceived { get; init; }
        public required int OldProductId { get; init; }
        public required Product? OldProduct { get; init; }
        public required Product NewProduct { get; init; }
        public required double OldQty { get; init; }
        public required double OldUnitPrice { get; init; }
        public required double OldStockQty { get; init; }
        public required double Qty { get; init; }
        public required double UnitPrice { get; init; }
        public required double LineSubtotal { get; init; }
        public required double NewStockQty { get; init; }
    }

    private static SwapPlan PlanSwapSaleItem(
        SqliteConnection conn, SqliteTransaction? tx,
        int saleId, int itemId, int newProductId, bool keepLinePrice,
        double? newQuantity, DateTime today)
    {
        var (saleDate, _, oldTotal, paymentType, customerId, cashReceived) =
            LoadSaleHeaderForEdit(conn, tx, saleId, today);

        var oldGross = SumSaleItemsSubtotal(conn, tx, saleId);
        var originalAdjustment = ProductPriceHelper.RoundPrice(oldTotal - oldGross);

        using var itemCmd = conn.CreateCommand();
        itemCmd.Transaction = tx;
        itemCmd.CommandText = """
            SELECT id, product_id, product_code, product_name, unit, quantity, unit_price, subtotal,
                   IFNULL(stock_qty, 0)
            FROM sale_items WHERE id = $id AND sale_id = $sale LIMIT 1;
            """;
        itemCmd.Parameters.AddWithValue("$id", itemId);
        itemCmd.Parameters.AddWithValue("$sale", saleId);
        using var ir = itemCmd.ExecuteReader();
        if (!ir.Read())
            throw new PdvException("Item da venda não encontrado.");
        var oldProductId = ir.GetInt32(1);
        var oldQty = ir.GetDouble(5);
        var oldUnitPrice = ir.GetDouble(6);
        var oldItemSubtotal = ir.GetDouble(7);
        var oldStockQtyRaw = ir.GetDouble(8);
        ir.Close();

        if (oldQty <= 0)
            throw new PdvException("Quantidade inválida no item.");

        var oldStockQty = oldStockQtyRaw > 0 ? oldStockQtyRaw : oldQty;

        var qty = newQuantity is > 0 ? ProductPriceHelper.RoundPrice(newQuantity.Value) : oldQty;
        if (qty <= 0)
            throw new PdvException("Informe uma quantidade maior que zero.");

        if (newProductId == oldProductId && Math.Abs(qty - oldQty) < 0.0001)
            throw new PdvException("Selecione um produto diferente ou altere a quantidade.");

        var newProduct = LoadProduct(conn, tx, newProductId)
            ?? throw new PdvException("Produto não encontrado.");
        if (!newProduct.Active)
            throw new PdvException($"Produto inativo: {newProduct.Name}");

        var unitPrice = keepLinePrice ? oldUnitPrice : newProduct.SalePrice;
        if (unitPrice < 0)
            throw new PdvException("Preço inválido.");
        var lineSubtotal = ProductPriceHelper.RoundPrice(qty * unitPrice);

        var newStockUnitsPerSale = ResolveManualSale(newProduct).StockUnitsPerSale;
        var newStockQty = StockQuantityForSale(qty, newStockUnitsPerSale);

        var newGross = ProductPriceHelper.RoundPrice(oldGross - oldItemSubtotal + lineSubtotal);
        var newTotal = ProductPriceHelper.RoundPrice(Math.Max(0, newGross + originalAdjustment));

        return new SwapPlan
        {
            SaleDate = saleDate,
            OldTotal = oldTotal,
            OldGross = oldGross,
            OriginalAdjustment = originalAdjustment,
            NewGross = newGross,
            NewTotal = newTotal,
            PaymentType = paymentType,
            CustomerId = customerId,
            CashReceived = cashReceived,
            OldProductId = oldProductId,
            OldProduct = LoadProduct(conn, tx, oldProductId),
            NewProduct = newProduct,
            OldQty = oldQty,
            OldUnitPrice = oldUnitPrice,
            OldStockQty = oldStockQty,
            Qty = qty,
            UnitPrice = unitPrice,
            LineSubtotal = lineSubtotal,
            NewStockQty = newStockQty,
        };
    }

    private static bool IsPureFiadoPayment(IReadOnlyList<PdvPaymentPart> parts) =>
        parts.Count == 1 && IsFiado(parts[0].PaymentType);

    public static PdvSaleDetail ChangeSalePayment(
        int saleId,
        IReadOnlyList<PdvPaymentPart> payments,
        double cashReceived = 0,
        int? customerPersonId = null,
        DateTime? sessionDate = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("alterar pagamento da venda");
        if (!AccessControl.Can("PdvAlterarPagamento"))
            throw new PdvException("Sem permissão para alterar pagamento da venda.");

        using var conn = DatabaseService.OpenConnection();
        var d = (sessionDate ?? DateTime.Today).Date;
        using var tx = conn.BeginTransaction();

        var (saleDate, _, total, oldPaymentType, existingCustomerId, oldCashReceived) =
            LoadSaleHeaderForEdit(conn, tx, saleId, d);
        var oldChangeAmount = LoadSaleChangeAmount(conn, tx, saleId);
        // Partes anteriores a partir dos movimentos de caixa (antes do DELETE).
        var oldPayments = PdvQueryService.LoadSalePayments(conn, saleId, oldPaymentType, total);

        var parts = NormalizePaymentParts(payments.Count > 0 ? payments[0].PaymentType : "Dinheiro", total, payments);
        var fiadoTotal = parts.Where(p => IsFiado(p.PaymentType)).Sum(p => p.Amount);

        // null = não informado → preserva cliente da venda (não remove).
        var customerId = customerPersonId ?? existingCustomerId;
        if (fiadoTotal > 0.009)
        {
            if (customerId is null or <= 0)
                throw new PdvException("Selecione o cliente para venda fiado.");
            _ = GetPersonName(conn, tx, customerId.Value)
                ?? throw new PdvException("Cliente não encontrado.");
        }

        var (recv, change) = ResolveCashTroco(parts, total, cashReceived);
        ApplySalePaymentUpdate(conn, tx, saleId, saleDate, parts, total, recv, change, customerId);

        tx.Commit();

        var detail = PdvQueryService.GetSaleDetail(saleId);
        AuditService.LogJson(
            "alterar_pagamento",
            "venda",
            saleId.ToString(),
            new
            {
                op = "alterar_pagamento",
                sale_id = saleId,
                total,
                old_payment_type = oldPaymentType,
                new_payment_type = detail.PaymentType,
                old_customer_id = existingCustomerId,
                new_customer_id = detail.CustomerPersonId,
                old_cash_received = oldCashReceived,
                new_cash_received = detail.CashReceived,
                old_change_amount = oldChangeAmount,
                new_change_amount = detail.ChangeAmount,
                old_payments = oldPayments.Select(p => new { payment_type = p.PaymentType, amount = p.Amount }).ToList(),
                new_payments = parts.Select(p => new { payment_type = p.PaymentType, amount = p.Amount }).ToList(),
            },
            $"Pagamento venda #{saleId}: {oldPaymentType} → {detail.PaymentType}");

        return detail;
    }

    private static double LoadSaleChangeAmount(SqliteConnection conn, SqliteTransaction tx, int saleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT IFNULL(change_amount, 0) FROM sales WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
    }

    private static (DateTime SaleDate, bool Cancelled, double Total, string PaymentType, int? CustomerId, double? CashReceived)
        LoadSaleHeaderForEdit(SqliteConnection conn, SqliteTransaction? tx, int saleId, DateTime today)
    {
        CashService.RequireOperational(conn, today);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT session_date, cancelled, total, payment_type, customer_id, cash_received
            FROM sales WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new PdvException("Venda não encontrada.");
        if (reader.GetInt32(1) != 0)
            throw new PdvException("Venda cancelada não pode ser alterada.");
        var saleDate = DateTime.Parse(reader.GetString(0)).Date;
        if (saleDate != today)
            throw new PdvException("Só é possível alterar vendas de hoje com o caixa aberto.");
        var total = reader.GetDouble(2);
        var paymentType = reader.GetString(3);
        int? customerId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
        double? cashReceived = reader.IsDBNull(5) ? null : reader.GetDouble(5);
        return (saleDate, false, total, paymentType, customerId, cashReceived);
    }

    private static double SumSaleItemsSubtotal(SqliteConnection conn, SqliteTransaction? tx, int saleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT IFNULL(SUM(subtotal), 0) FROM sale_items WHERE sale_id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return ProductPriceHelper.RoundPrice(Convert.ToDouble(cmd.ExecuteScalar()));
    }

    private static void ApplySaleTotal(SqliteConnection conn, SqliteTransaction tx, int saleId, double total)
    {
        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE sales SET total = $total WHERE id = $id;";
        upd.Parameters.AddWithValue("$total", total);
        upd.Parameters.AddWithValue("$id", saleId);
        upd.ExecuteNonQuery();
    }

    /// <summary>
    /// Recalcula sales.total = SUM(sale_items.subtotal). Não preserva desconto/surcharge.
    /// Preferir ApplySaleTotal com ajuste líquido no fluxo de SwapSaleItem.
    /// </summary>
    private static double RecalcSaleTotal(SqliteConnection conn, SqliteTransaction tx, int saleId)
    {
        var total = SumSaleItemsSubtotal(conn, tx, saleId);
        ApplySaleTotal(conn, tx, saleId, total);
        return total;
    }

    private static List<PdvPaymentPart> ScalePaymentPartsToTotal(IReadOnlyList<PdvPaymentPart> parts, double newTotal)
    {
        if (parts.Count == 0)
            return [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = newTotal }];
        if (parts.Count == 1)
            return [new PdvPaymentPart { PaymentType = parts[0].PaymentType, Amount = newTotal }];

        var oldSum = parts.Sum(p => p.Amount);
        if (oldSum <= 0.009)
            return [new PdvPaymentPart { PaymentType = parts[0].PaymentType, Amount = newTotal }];

        var scaled = new List<PdvPaymentPart>();
        double allocated = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            double amt;
            if (i == parts.Count - 1)
                amt = ProductPriceHelper.RoundPrice(newTotal - allocated);
            else
            {
                amt = ProductPriceHelper.RoundPrice(parts[i].Amount / oldSum * newTotal);
                allocated += amt;
            }
            if (amt > 0.009)
                scaled.Add(new PdvPaymentPart { PaymentType = parts[i].PaymentType, Amount = amt });
        }
        return scaled;
    }

    /// <summary>
    /// Se o total aumentou (troca mais cara), mantém os valores originais e lança a diferença
    /// em outra forma (misto), para o operador só ajustar no F5 se quiser.
    /// Se diminuiu, reduz proporcionalmente.
    /// </summary>
    private static List<PdvPaymentPart> AdjustPaymentPartsAfterTotalChange(
        IReadOnlyList<PdvPaymentPart> parts, double oldTotal, double newTotal)
    {
        if (parts.Count == 0)
            return [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = newTotal }];

        var oldSum = ProductPriceHelper.RoundPrice(parts.Sum(p => p.Amount));
        if (oldSum <= 0.009)
            return [new PdvPaymentPart { PaymentType = parts[0].PaymentType, Amount = newTotal }];

        var diff = ProductPriceHelper.RoundPrice(newTotal - oldSum);
        if (Math.Abs(diff) <= 0.02)
            return parts.Select(p => new PdvPaymentPart { PaymentType = p.PaymentType, Amount = p.Amount }).ToList();

        // Total menor: escala (ou reduz a última parte).
        if (diff < 0)
            return ScalePaymentPartsToTotal(parts, newTotal);

        // Total maior: mantém partes e adiciona a diferença em outra forma (misto).
        var result = parts
            .Select(p => new PdvPaymentPart { PaymentType = p.PaymentType, Amount = p.Amount })
            .ToList();
        var primary = result[0].PaymentType;
        var extraType = IsDinheiro(primary) ? "Pix"
            : primary.Equals("Pix", StringComparison.OrdinalIgnoreCase) ? "Dinheiro"
            : "Pix";

        var existingExtra = result.FindIndex(p => p.PaymentType.Equals(extraType, StringComparison.OrdinalIgnoreCase));
        if (existingExtra >= 0)
        {
            result[existingExtra] = new PdvPaymentPart
            {
                PaymentType = extraType,
                Amount = ProductPriceHelper.RoundPrice(result[existingExtra].Amount + diff),
            };
        }
        else
        {
            result.Add(new PdvPaymentPart { PaymentType = extraType, Amount = diff });
        }
        return result;
    }

    private static void ApplySalePaymentUpdate(
        SqliteConnection conn, SqliteTransaction tx, int saleId, DateTime saleDate,
        IReadOnlyList<PdvPaymentPart> parts, double total,
        double? cashReceived, double changeAmount, int? customerPersonId)
    {
        var label = SalePaymentLabel(parts);
        string? customerName = null;
        if (customerPersonId is > 0)
            customerName = GetPersonName(conn, tx, customerPersonId.Value);

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE sales SET
                  payment_type = $pay, total = $total,
                  cash_received = $recv, change_amount = $chg,
                  customer_id = $cust
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$pay", label);
            upd.Parameters.AddWithValue("$total", total);
            upd.Parameters.AddWithValue("$recv", (object?)cashReceived ?? DBNull.Value);
            upd.Parameters.AddWithValue("$chg", changeAmount);
            upd.Parameters.AddWithValue("$cust", (object?)customerPersonId ?? DBNull.Value);
            upd.Parameters.AddWithValue("$id", saleId);
            upd.ExecuteNonQuery();
        }

        CashService.DeleteSaleMovements(conn, tx, saleId);

        var descPrefix = $"VENDA PDV #{saleId}";
        foreach (var part in parts)
        {
            if (IsFiado(part.PaymentType))
            {
                var desc = string.IsNullOrWhiteSpace(customerName)
                    ? $"{descPrefix} — FIADO R$ {part.Amount:F2}"
                    : $"{descPrefix} — FIADO R$ {part.Amount:F2} — {customerName}";
                CashService.AddSalePaymentMovement(conn, tx, saleDate, saleId, CashMovementKind.VendaFiado,
                    desc, part.PaymentType, part.Amount, customerName, affectsBalance: false);
            }
            else
            {
                var desc = parts.Count > 1
                    ? $"{descPrefix} — {part.PaymentType} R$ {part.Amount:F2}"
                    : $"{descPrefix} — {part.PaymentType}";
                string? movNotes = null;
                if (IsDinheiro(part.PaymentType) && cashReceived is not null && changeAmount > 0.009)
                {
                    desc = $"{desc} (recebido R$ {cashReceived:F2}, troco R$ {changeAmount:F2})";
                    movNotes = $"{{\"cash_received\":{cashReceived.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"change\":{changeAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
                }
                CashService.AddSalePaymentMovement(conn, tx, saleDate, saleId, CashMovementKind.Venda,
                    desc, part.PaymentType, part.Amount, customerName, affectsBalance: true, movNotes);
            }
        }
    }

    private static List<PdvPaymentPart> NormalizePaymentParts(
        string paymentType, double total, IReadOnlyList<PdvPaymentPart>? payments)
    {
        if (payments is { Count: > 0 })
        {
            var parts = payments
                .Select(p => new PdvPaymentPart
                {
                    PaymentType = NormalizePayment(p.PaymentType),
                    Amount = ProductPriceHelper.RoundPrice(p.Amount),
                })
                .Where(p => p.Amount > 0)
                .ToList();
            if (parts.Count == 0)
                throw new PdvException("Informe ao menos uma forma de pagamento.");
            var sum = ProductPriceHelper.RoundPrice(parts.Sum(p => p.Amount));
            if (Math.Abs(sum - total) > 0.02)
                throw new PdvException($"Soma dos pagamentos (R$ {sum:N2}) difere do total (R$ {total:N2}).");
            return parts;
        }
        var forma = NormalizePayment(paymentType);
        return [new PdvPaymentPart { PaymentType = forma, Amount = total }];
    }

    private static string SalePaymentLabel(IReadOnlyList<PdvPaymentPart> parts)
    {
        if (parts.Count == 1)
            return parts[0].PaymentType;
        var abbrev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dinheiro"] = "DIN",
            ["Pix"] = "PIX",
            ["Cartão Débito"] = "DEB",
            ["Cartão Crédito"] = "CRÉD",
            ["Fiado"] = "Fiado",
        };
        var label = string.Join("+", parts.Select(p => abbrev.GetValueOrDefault(p.PaymentType, p.PaymentType[..Math.Min(6, p.PaymentType.Length)])));
        return label.Length <= 30 ? label : "Misto";
    }

    private static (double? cashReceived, double changeAmount) ResolveCashTroco(
        IReadOnlyList<PdvPaymentPart> parts, double total, double cashReceivedInput)
    {
        var recv = ProductPriceHelper.RoundPrice(cashReceivedInput);
        if (recv <= 0)
            return (null, 0);
        var dinheiroAmt = ProductPriceHelper.RoundPrice(parts.Where(p => IsDinheiro(p.PaymentType)).Sum(p => p.Amount));
        if (dinheiroAmt <= 0 && parts.Count == 1 && IsDinheiro(parts[0].PaymentType))
            dinheiroAmt = total;
        // Sem componente em dinheiro: ignora cashReceived (evita troco fantasma em PIX/cartão).
        if (dinheiroAmt <= 0)
            return (null, 0);
        if (recv <= dinheiroAmt + 0.009)
            return (null, 0);
        return (recv, ProductPriceHelper.RoundPrice(recv - dinheiroAmt));
    }

    private static Product? LoadProduct(SqliteConnection conn, SqliteTransaction? tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        return ReadProducts(cmd).FirstOrDefault();
    }

    private static string? GetPersonName(SqliteConnection conn, SqliteTransaction tx, int personId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT name FROM people WHERE id = $id AND active = 1 LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", personId);
        return cmd.ExecuteScalar() as string;
    }

    private static Product? MatchBarcode(SqliteConnection conn, string barcodeTerm)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE active = 1 AND barcode = $bc LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$bc", barcodeTerm);
        var hit = ReadProducts(cmd).FirstOrDefault();
        if (hit is not null)
            return hit;

        var digits = new string(barcodeTerm.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits))
            return null;

        using var like = conn.CreateCommand();
        like.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE active = 1 AND barcode IS NOT NULL AND barcode != ''
              AND barcode LIKE $like LIMIT 20;
            """;
        like.Parameters.AddWithValue("$like", $"%{digits}%");
        foreach (var p in ReadProducts(like))
        {
            var stored = new string((p.Barcode ?? "").Where(char.IsDigit).ToArray());
            if (stored == digits || stored.TrimStart('0') == digits.TrimStart('0'))
                return p;
        }

        // Código de barras do fardo/caixa (extra_json.barcode_embalagem)
        using var packCmd = conn.CreateCommand();
        packCmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products
            WHERE active = 1 AND IFNULL(extra_json,'') LIKE $like
            LIMIT 40;
            """;
        packCmd.Parameters.AddWithValue("$like", "%\"barcode_embalagem\":\"" + digits + "%");
        foreach (var p in ReadProducts(packCmd))
        {
            var pack = ProductExtra.Parse(p.ExtraJson).BarcodeEmbalagem;
            var packDigits = new string((pack ?? "").Where(char.IsDigit).ToArray());
            if (packDigits == digits || packDigits.TrimStart('0') == digits.TrimStart('0'))
                return p;
        }
        return null;
    }

    private static Product? GetByCode(SqliteConnection conn, string code)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE active = 1 AND UPPER(IFNULL(code,'')) = $code LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$code", code.ToUpperInvariant());
        return ReadProducts(cmd).FirstOrDefault();
    }

    private static Product? SearchFirst(SqliteConnection conn, string term)
    {
        using var cmd = conn.CreateCommand();
        var escaped = term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE active = 1
              AND (UPPER(name) LIKE $like ESCAPE '\'
                OR UPPER(IFNULL(code,'')) LIKE $like ESCAPE '\'
                OR IFNULL(barcode,'') LIKE $like ESCAPE '\')
            ORDER BY name LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$like", $"%{escaped.ToUpperInvariant()}%");
        return ReadProducts(cmd).FirstOrDefault();
    }

    private static bool BarcodesEqual(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        return a == b || a.TrimStart('0') == b.TrimStart('0');
    }

    private static List<string> BarcodeLookupTerms(string term)
    {
        var t = term.Trim();
        var digits = new string(t.Where(char.IsDigit).ToArray());
        var list = new List<string>();
        if (!string.IsNullOrEmpty(digits))
        {
            list.Add(digits);
            var stripped = digits.TrimStart('0');
            if (!string.IsNullOrEmpty(stripped) && stripped != digits)
                list.Add(stripped);
            if (digits.Length < 13)
                list.Add(digits.PadLeft(13, '0'));
        }
        if (!list.Contains(t))
            list.Add(t);
        return list.Distinct().ToList();
    }

    /// <summary>
    /// Depósito: permite vender mesmo com estoque zerado/negativo
    /// (comum quando o inventário ainda não foi ajustado).
    /// Mantém só checagens de produto válido em ValidateItemsBeforePayment.
    /// </summary>
    private static void EnsureAvailableStock(Product product, double qtySale)
    {
        _ = product;
        _ = qtySale;
    }

    private static string NormalizePayment(string? paymentType) =>
        PaymentMethodsService.NormalizeToApiLabel(paymentType);

    private static bool IsDinheiro(string? paymentType) =>
        PaymentMethodsService.IsDinheiroLabel(paymentType);

    private static bool IsFiado(string? paymentType) =>
        PaymentMethodsService.IsFiadoLabel(paymentType);

    private static List<Product> ReadProducts(SqliteCommand cmd)
    {
        var list = new List<Product>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Product
            {
                Id = reader.GetInt32(0),
                Code = reader.IsDBNull(1) ? null : reader.GetString(1),
                Barcode = reader.IsDBNull(2) ? null : reader.GetString(2),
                Name = reader.GetString(3),
                GroupName = reader.IsDBNull(4) ? null : reader.GetString(4),
                Unit = reader.GetString(5),
                CostPrice = reader.GetDouble(6),
                SalePrice = reader.GetDouble(7),
                MinStock = reader.GetInt32(8),
                Stock = reader.GetDouble(9),
                Location = reader.IsDBNull(10) ? null : reader.GetString(10),
                ExtraJson = reader.IsDBNull(11) ? "{}" : reader.GetString(11),
                Active = reader.GetInt32(12) != 0,
                CreatedAt = reader.GetString(13),
                StockFridge = reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetDouble(14) : 0,
                StockFridgeMin = reader.FieldCount > 15 && !reader.IsDBNull(15) ? Convert.ToInt32(reader.GetValue(15)) : 0,
            });
        }
        return list;
    }
}

public class PdvException : Exception
{
    public PdvException(string message) : base(message) { }
}
