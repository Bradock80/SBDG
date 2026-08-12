using System.Globalization;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class SaleExchangeService
{
    public static IReadOnlyList<SaleExchangeSearchRow> SearchSales(string? term, int limit = 40)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT s.id, s.session_date, s.created_at, s.total, s.payment_type,
                   IFNULL(p.name, '') AS customer_name
            FROM sales s
            LEFT JOIN people p ON p.id = s.customer_id
            WHERE s.cancelled = 0
            """;
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            if (int.TryParse(t, out var saleId))
            {
                sql += " AND s.id = $id";
                cmd.Parameters.AddWithValue("$id", saleId);
            }
            else
            {
                sql += " AND (IFNULL(p.name,'') LIKE $q OR s.payment_type LIKE $q)";
                cmd.Parameters.AddWithValue("$q", "%" + t + "%");
            }
        }

        sql += " ORDER BY s.created_at DESC, s.id DESC LIMIT $lim;";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$lim", Math.Clamp(limit, 1, 200));

        var list = new List<SaleExchangeSearchRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var created = reader.GetString(2);
            list.Add(new SaleExchangeSearchRow
            {
                Id = reader.GetInt32(0),
                SessionDateBr = FormatDateBr(reader.GetString(1)),
                CreatedAtBr = FormatDateTimeBr(created),
                Total = reader.GetDouble(3),
                PaymentLabel = reader.GetString(4),
                CustomerName = string.IsNullOrWhiteSpace(reader.GetString(5)) ? "—" : reader.GetString(5),
            });
        }
        return list;
    }

    public static IReadOnlyList<SaleExchangeSaleItemVm> LoadReturnableItems(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.id, si.product_id, IFNULL(si.product_code,''), IFNULL(si.product_name,''),
                   si.quantity, si.unit_price,
                   IFNULL((
                     SELECT SUM(r.qty) FROM sale_exchange_return_items r
                     WHERE r.sale_item_id = si.id
                   ), 0) AS returned
            FROM sale_items si
            INNER JOIN sales s ON s.id = si.sale_id
            WHERE si.sale_id = $id AND s.cancelled = 0
            ORDER BY si.id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<SaleExchangeSaleItemVm>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SaleExchangeSaleItemVm
            {
                SaleItemId = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                ProductCode = reader.GetString(2),
                ProductName = reader.GetString(3),
                SoldQty = reader.GetDouble(4),
                UnitPrice = reader.GetDouble(5),
                AlreadyReturnedQty = reader.GetDouble(6),
                ReturnQty = 0,
            });
        }
        return list;
    }

    public static SaleExchangeResult Confirm(SaleExchangeRequest request)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("troca/devolução");
        if (!AccessControl.Can("PdvTrocaDevolucao"))
            throw new SaleExchangeException("Sem permissão para Troca / Devolução.");

        if (request.Returns.Count == 0 || request.Returns.All(r => r.Qty <= 0.0001))
            throw new SaleExchangeException("Informe ao menos um item para devolver.");

        var paymentType = PaymentMethodsService.NormalizeToApiLabel(request.PaymentType ?? "Dinheiro");
        var today = DateTime.Today;

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        using (var saleCmd = conn.CreateCommand())
        {
            saleCmd.Transaction = tx;
            saleCmd.CommandText = """
                SELECT id, cancelled, payment_type, customer_id, total
                FROM sales WHERE id = $id LIMIT 1;
                """;
            saleCmd.Parameters.AddWithValue("$id", request.OriginalSaleId);
            using var sr = saleCmd.ExecuteReader();
            if (!sr.Read())
                throw new SaleExchangeException("Venda não encontrada.");
            if (sr.GetInt32(1) != 0)
                throw new SaleExchangeException("Venda cancelada não pode ser trocada.");
            var originalPayment = sr.GetString(2);
            int? customerId = sr.IsDBNull(3) ? null : sr.GetInt32(3);
            sr.Close();

            var returnLines = new List<(int SaleItemId, int ProductId, string Code, string Name,
                double Qty, double UnitPrice, double Amount, double StockRestore)>();
            double returnTotal = 0;

            foreach (var ret in request.Returns.Where(r => r.Qty > 0.0001))
            {
                using var itemCmd = conn.CreateCommand();
                itemCmd.Transaction = tx;
                itemCmd.CommandText = """
                    SELECT id, product_id, IFNULL(product_code,''), IFNULL(product_name,''),
                           quantity, unit_price, IFNULL(stock_qty,0),
                           IFNULL((
                             SELECT SUM(r.qty) FROM sale_exchange_return_items r
                             WHERE r.sale_item_id = sale_items.id
                           ), 0)
                    FROM sale_items
                    WHERE id = $id AND sale_id = $sale LIMIT 1;
                    """;
                itemCmd.Parameters.AddWithValue("$id", ret.SaleItemId);
                itemCmd.Parameters.AddWithValue("$sale", request.OriginalSaleId);
                using var ir = itemCmd.ExecuteReader();
                if (!ir.Read())
                    throw new SaleExchangeException($"Item #{ret.SaleItemId} não pertence à venda.");
                var soldQty = ir.GetDouble(4);
                var unitPrice = ir.GetDouble(5);
                var stockQty = ir.GetDouble(6);
                var already = ir.GetDouble(7);
                var available = ProductPriceHelper.RoundPrice(soldQty - already);
                var qty = ProductPriceHelper.RoundPrice(ret.Qty);
                if (qty <= 0 || qty > available + 0.0001)
                    throw new SaleExchangeException(
                        $"Quantidade inválida para devolução de '{ir.GetString(3)}' (disponível: {available:0.###}).");

                var amount = ProductPriceHelper.RoundPrice(qty * unitPrice);
                var baseStock = stockQty > 0.0001 ? stockQty : soldQty;
                var stockRestore = ProductPriceHelper.RoundPrice(baseStock * (qty / soldQty));
                returnLines.Add((
                    ir.GetInt32(0), ir.GetInt32(1), ir.GetString(2), ir.GetString(3),
                    qty, unitPrice, amount, stockRestore));
                returnTotal += amount;
                ir.Close();
            }

            returnTotal = ProductPriceHelper.RoundPrice(returnTotal);

            var newLines = new List<(Product Product, double Qty, double UnitPrice, double Amount)>();
            double newTotal = 0;
            foreach (var n in request.NewItems.Where(x => x.Qty > 0.0001))
            {
                var product = LoadProduct(conn, tx, n.ProductId)
                    ?? throw new SaleExchangeException($"Produto #{n.ProductId} não encontrado.");
                if (!product.Active)
                    throw new SaleExchangeException($"Produto inativo: {product.Name}");
                var qty = ProductPriceHelper.RoundPrice(n.Qty);
                var price = n.UnitPrice is > 0 ? ProductPriceHelper.RoundPrice(n.UnitPrice.Value) : product.SalePrice;
                if (price < 0)
                    throw new SaleExchangeException("Preço inválido no item novo.");
                var amount = ProductPriceHelper.RoundPrice(qty * price);
                newLines.Add((product, qty, price, amount));
                newTotal += amount;
            }
            newTotal = ProductPriceHelper.RoundPrice(newTotal);
            var difference = ProductPriceHelper.RoundPrice(newTotal - returnTotal);

            // Caixa aberto só quando há valor a cobrar ou devolver
            int? cashSessionId = null;
            if (Math.Abs(difference) >= 0.01)
            {
                CashService.RequireOperational(conn, today);
                cashSessionId = CashService.GetOperacaoView(today).SessionId;
                if (string.IsNullOrWhiteSpace(paymentType))
                    throw new SaleExchangeException("Informe a forma de pagamento da diferença.");
                if (PaymentMethodsService.IsFiadoLabel(paymentType) && customerId is null)
                    throw new SaleExchangeException("Para usar Fiado na diferença, a venda precisa ter cliente.");
            }

            // Estoque: devolve
            foreach (var line in returnLines)
            {
                var product = LoadProduct(conn, tx, line.ProductId);
                if (product is null)
                    continue;
                foreach (var (comp, qtyBack) in ProductCompositionService.StockMovementsForSale(product, line.StockRestore))
                {
                    StockService.ApplySaleRestore(conn, tx, comp.Id, qtyBack,
                        notes: "Devolução / troca de venda",
                        refType: "sale_exchange",
                        operation: "devolucao_troca");
                }
            }

            // Estoque: novos
            foreach (var (product, qty, _, _) in newLines)
            {
                foreach (var (comp, deduct) in ProductCompositionService.StockMovementsForSale(product, qty))
                {
                    StockService.ApplySaleDeduction(conn, tx, comp.Id, deduct,
                        notes: "Troca — novo item",
                        refType: "sale_exchange");
                }
            }

            var user = AppSession.CurrentUser;
            int exchangeId;
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO sale_exchanges (
                      original_sale_id, created_at, user_id, user_name,
                      return_total, new_total, difference, payment_type, notes, cash_session_id
                    ) VALUES (
                      $sale, $created, $uid, $uname, $ret, $new, $diff, $pay, $notes, $sid
                    );
                    SELECT last_insert_rowid();
                    """;
                ins.Parameters.AddWithValue("$sale", request.OriginalSaleId);
                ins.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
                ins.Parameters.AddWithValue("$uid", (object?)user?.Id ?? DBNull.Value);
                ins.Parameters.AddWithValue("$uname", (object?)user?.Nome ?? DBNull.Value);
                ins.Parameters.AddWithValue("$ret", returnTotal);
                ins.Parameters.AddWithValue("$new", newTotal);
                ins.Parameters.AddWithValue("$diff", difference);
                ins.Parameters.AddWithValue("$pay",
                    Math.Abs(difference) < 0.01 ? (object)DBNull.Value : paymentType);
                ins.Parameters.AddWithValue("$notes",
                    (object?)(string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()) ?? DBNull.Value);
                ins.Parameters.AddWithValue("$sid", (object?)cashSessionId ?? DBNull.Value);
                exchangeId = Convert.ToInt32(ins.ExecuteScalar());
            }

            foreach (var line in returnLines)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO sale_exchange_return_items (
                      exchange_id, sale_item_id, product_id, product_code, product_name,
                      qty, unit_price, amount
                    ) VALUES ($ex, $si, $pid, $code, $name, $qty, $price, $amt);
                    """;
                ins.Parameters.AddWithValue("$ex", exchangeId);
                ins.Parameters.AddWithValue("$si", line.SaleItemId);
                ins.Parameters.AddWithValue("$pid", line.ProductId);
                ins.Parameters.AddWithValue("$code", line.Code);
                ins.Parameters.AddWithValue("$name", line.Name);
                ins.Parameters.AddWithValue("$qty", line.Qty);
                ins.Parameters.AddWithValue("$price", line.UnitPrice);
                ins.Parameters.AddWithValue("$amt", line.Amount);
                ins.ExecuteNonQuery();
            }

            foreach (var (product, qty, price, amount) in newLines)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO sale_exchange_new_items (
                      exchange_id, product_id, product_code, product_name, unit,
                      qty, unit_price, amount
                    ) VALUES ($ex, $pid, $code, $name, $unit, $qty, $price, $amt);
                    """;
                ins.Parameters.AddWithValue("$ex", exchangeId);
                ins.Parameters.AddWithValue("$pid", product.Id);
                ins.Parameters.AddWithValue("$code", product.Code ?? "");
                ins.Parameters.AddWithValue("$name", product.Name);
                ins.Parameters.AddWithValue("$unit", product.Unit);
                ins.Parameters.AddWithValue("$qty", qty);
                ins.Parameters.AddWithValue("$price", price);
                ins.Parameters.AddWithValue("$amt", amount);
                ins.ExecuteNonQuery();
            }

            var originalFiado = PaymentMethodsService.IsFiadoLabel(originalPayment)
                || originalPayment.Contains("Fiado", StringComparison.OrdinalIgnoreCase);
            var warnPix = false;
            string message;

            if (Math.Abs(difference) < 0.01)
            {
                message = newLines.Count == 0
                    ? $"Devolução #{exchangeId} concluída (sem valor a devolver — troca seca / ajuste)."
                    : $"Troca #{exchangeId} concluída sem diferença.";
            }
            else if (difference > 0)
            {
                // Cliente paga complemento
                if (PaymentMethodsService.IsFiadoLabel(paymentType) && customerId is int cid)
                {
                    InsertFiadoCredit(conn, tx, cid, difference,
                        $"Complemento troca #{exchangeId} venda #{request.OriginalSaleId}",
                        isCreditToStore: true);
                    message = $"Troca #{exchangeId}: complemento de R$ {difference:N2} lançado no fiado.";
                }
                else
                {
                    var desc = $"TROCA #{exchangeId} — complemento venda #{request.OriginalSaleId} — {paymentType}";
                    CashService.AddExchangeMovement(conn, tx, today, exchangeId, desc, paymentType,
                        amountIn: difference, amountOut: 0);
                    message = $"Troca #{exchangeId}: cliente paga R$ {difference:N2} ({paymentType}).";
                }
            }
            else
            {
                var refund = ProductPriceHelper.RoundPrice(Math.Abs(difference));
                if (originalFiado && customerId is int cid && PaymentMethodsService.IsFiadoLabel(paymentType))
                {
                    InsertFiadoCredit(conn, tx, cid, refund,
                        $"Crédito devolução troca #{exchangeId} venda #{request.OriginalSaleId}",
                        isCreditToStore: false);
                    message = $"Devolução #{exchangeId}: R$ {refund:N2} abatido do fiado do cliente.";
                }
                else
                {
                    var desc = $"TROCA #{exchangeId} — devolução venda #{request.OriginalSaleId} — {paymentType}";
                    CashService.AddExchangeMovement(conn, tx, today, exchangeId, desc, paymentType,
                        amountIn: 0, amountOut: refund);
                    warnPix = paymentType.Contains("Pix", StringComparison.OrdinalIgnoreCase)
                        || paymentType.Contains("Cartão", StringComparison.OrdinalIgnoreCase)
                        || paymentType.Contains("Cartao", StringComparison.OrdinalIgnoreCase);
                    message = warnPix
                        ? $"Devolução #{exchangeId}: R$ {refund:N2} registrado. Estorne manualmente no PIX/maquininha se necessário."
                        : $"Devolução #{exchangeId}: devolver R$ {refund:N2} ao cliente ({paymentType}).";
                }
            }

            tx.Commit();

            AuditService.LogJson("troca", "venda", request.OriginalSaleId.ToString(),
                AuditPayloadBuilder.SaleExchange(request.OriginalSaleId, exchangeId, returnTotal, newTotal,
                    difference, paymentType, user?.Id, user?.Nome, request.Notes),
                message);

            return new SaleExchangeResult
            {
                ExchangeId = exchangeId,
                ReturnTotal = returnTotal,
                NewTotal = newTotal,
                Difference = difference,
                Message = message,
                WarnManualPixRefund = warnPix,
            };
        }
    }

    /// <summary>
    /// isCreditToStore=false: abate saldo do cliente (pagamento).
    /// isCreditToStore=true: não usado como charge — para complemento fiado criamos payment negativo? 
    /// Melhor: complemento fiado = não inserir payment; criar seria charge. Simplificação: 
    /// para complemento fiado inserimos uma "venda" simbólica... 
    /// Aqui usamos payment só para abate (crédito ao cliente).
    /// </summary>
    private static void InsertFiadoCredit(
        SqliteConnection conn, SqliteTransaction tx, int customerId, double amount,
        string notes, bool isCreditToStore)
    {
        if (isCreditToStore)
        {
            // Complemento no fiado: aumenta dívida via venda mínima ligada ao cliente
            using var insSale = conn.CreateCommand();
            insSale.Transaction = tx;
            insSale.CommandText = """
                INSERT INTO sales (session_date, total, payment_type, customer_id, created_at)
                VALUES ($date, $total, 'Fiado', $cust, $created);
                SELECT last_insert_rowid();
                """;
            insSale.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd"));
            insSale.Parameters.AddWithValue("$total", amount);
            insSale.Parameters.AddWithValue("$cust", customerId);
            insSale.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
            var saleId = Convert.ToInt32(insSale.ExecuteScalar());

            CashService.AddSalePaymentMovement(conn, tx, DateTime.Today, saleId,
                CashMovementKind.VendaFiado,
                $"TROCA — complemento fiado venda #{saleId}", "Fiado", amount,
                null, affectsBalance: false, notes: notes);
            return;
        }

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO fiado_payments (
              customer_id, amount, interest_amount, payment_type, payment_date, notes, reversed
            ) VALUES ($cust, $amt, 0, 'Crédito devolução', $date, $notes, 0);
            """;
        ins.Parameters.AddWithValue("$cust", customerId);
        ins.Parameters.AddWithValue("$amt", amount);
        ins.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd"));
        ins.Parameters.AddWithValue("$notes", notes);
        ins.ExecuteNonQuery();
    }

    private static Product? LoadProduct(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at, IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new Product
        {
            Id = r.GetInt32(0),
            Code = r.IsDBNull(1) ? null : r.GetString(1),
            Barcode = r.IsDBNull(2) ? null : r.GetString(2),
            Name = r.GetString(3),
            GroupName = r.IsDBNull(4) ? null : r.GetString(4),
            Unit = r.IsDBNull(5) ? "UN" : r.GetString(5),
            CostPrice = r.IsDBNull(6) ? 0 : r.GetDouble(6),
            SalePrice = r.GetDouble(7),
            MinStock = r.IsDBNull(8) ? 0 : (int)r.GetDouble(8),
            Stock = r.GetDouble(9),
            Location = r.IsDBNull(10) ? null : r.GetString(10),
            ExtraJson = r.IsDBNull(11) ? "{}" : r.GetString(11),
            Active = r.GetInt32(12) != 0,
            CreatedAt = r.IsDBNull(13) ? "" : r.GetString(13),
            StockFridge = r.FieldCount > 14 && !r.IsDBNull(14) ? r.GetDouble(14) : 0,
            StockFridgeMin = r.FieldCount > 15 && !r.IsDBNull(15) ? Convert.ToInt32(r.GetValue(15)) : 0,
        };
    }

    private static string FormatDateBr(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy")
            : iso;

    private static string FormatDateTimeBr(string iso)
    {
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return iso;
        return DateBrHelper.FormatUtcToBrazil(dt, "dd/MM/yy HH:mm");
    }
}
