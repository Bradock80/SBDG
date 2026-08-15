using System.Globalization;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class FiadoService
{
    private static readonly HashSet<string> FormasRecebimento =
        new(FiadoReceberFormas.All, StringComparer.OrdinalIgnoreCase);

    public static FiadoListResult ListContas(bool somenteSaldo = true, string? search = null)
    {
        using var conn = DatabaseService.OpenConnection();
        RepairMissingFiadoCustomers(conn);

        var agg = AggregateCustomers(conn);
        var term = (search ?? "").Trim().ToLowerInvariant();
        var rows = new List<FiadoContaRow>();

        foreach (var (customerId, data) in agg)
        {
            if (LoadPerson(conn, customerId) is not { } personInfo)
                continue;
            var (customerName, customerPhone) = personInfo;

            if (!string.IsNullOrEmpty(term))
            {
                var name = customerName.ToLowerInvariant();
                var phone = customerPhone.ToLowerInvariant();
                if (!name.Contains(term) && !phone.Contains(term))
                    continue;
            }

            var charges = data.TotalCharges;
            var paid = CalcPayments(conn, customerId);
            var interest = CalcInterest(conn, customerId);
            var balance = ProductPriceHelper.RoundPrice(charges - paid);
            if (somenteSaldo && balance <= 0.005)
                continue;

            var cupomName = GetDominantPartyName(conn, customerId);
            var displayName = customerName;
            if (!string.IsNullOrWhiteSpace(cupomName)
                && !NamesLooselyMatch(customerName, cupomName))
                displayName = $"{cupomName}  ·  cadastro: {customerName}";
            else if (!string.IsNullOrWhiteSpace(cupomName)
                     && cupomName.Length > customerName.Length
                     && NamesLooselyMatch(customerName, cupomName))
                displayName = cupomName;

            rows.Add(new FiadoContaRow
            {
                CustomerId = customerId,
                CustomerName = displayName,
                Phone = customerPhone,
                TotalCharges = charges,
                TotalPaid = paid,
                TotalInterest = interest,
                Balance = balance,
                SalesCount = data.SalesCount,
                LastSaleBr = data.LastSaleAt is null
                    ? ""
                    : FormatBrDateTime(data.LastSaleAt),
                Orphan = false,
            });
        }

        foreach (var orphan in AggregateOrphans(conn))
        {
            if (somenteSaldo && orphan.Balance <= 0.005)
                continue;
            if (!string.IsNullOrEmpty(term)
                && !term.Contains("sem")
                && !orphan.CustomerName.ToLowerInvariant().Contains(term))
                continue;
            rows.Add(orphan);
        }

        rows = rows.OrderByDescending(r => r.Balance).ThenBy(r => r.CustomerName).ToList();
        return new FiadoListResult
        {
            Rows = rows,
            Registros = rows.Count,
            TotalSaldo = ProductPriceHelper.RoundPrice(rows.Sum(r => r.Balance)),
            TotalJuros = ProductPriceHelper.RoundPrice(rows.Sum(r => r.TotalInterest)),
        };
    }

    public static FiadoCustomerDetail GetDetail(int customerId)
    {
        using var conn = DatabaseService.OpenConnection();
        if (LoadPerson(conn, customerId) is not { } personInfo)
            throw new FiadoException("Cliente não encontrado.");
        var (customerName, customerPhone) = personInfo;

        var charges = CalcCharges(conn, customerId);
        var paid = CalcPayments(conn, customerId);
        var balance = ProductPriceHelper.RoundPrice(charges - paid);

        var salesRaw = new List<(int Id, string SessionDate, string? CreatedAt, string PaymentType)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, session_date, created_at, payment_type, cancelled
                FROM sales
                WHERE customer_id = $id
                ORDER BY created_at DESC, id DESC;
                """;
            cmd.Parameters.AddWithValue("$id", customerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt32(4) != 0)
                    continue;
                salesRaw.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3)));
            }
        }

        var sales = new List<FiadoSaleRow>();
        foreach (var (saleId, sessionDate, createdAt, paymentType) in salesRaw)
        {
            var charge = SaleFiadoCharge(conn, saleId, paymentType);
            if (charge <= 0.009)
                continue;

            sales.Add(new FiadoSaleRow
            {
                Id = saleId,
                SessionDateBr = DateBrHelper.FormatIso(sessionDate),
                DateBr = FormatBrDateTime(createdAt),
                Total = charge,
                Items = LoadSaleItems(conn, saleId),
            });
        }

        var payments = new List<FiadoPaymentRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, payment_date, amount, interest_amount, payment_type, reversed, COALESCE(notes, '')
                FROM fiado_payments
                WHERE customer_id = $id
                ORDER BY payment_date DESC, id DESC;
                """;
            cmd.Parameters.AddWithValue("$id", customerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var amount = reader.GetDouble(2);
                var interest = reader.GetDouble(3);
                payments.Add(new FiadoPaymentRow
                {
                    Id = reader.GetInt32(0),
                    DateBr = DateBrHelper.FormatIso(reader.GetString(1)),
                    Amount = amount,
                    InterestAmount = interest,
                    PrincipalAmount = ProductPriceHelper.RoundPrice(Math.Max(0, amount - interest)),
                    PaymentType = reader.GetString(4),
                    Reversed = reader.GetInt32(5) != 0,
                    Notes = reader.GetString(6),
                });
            }
        }

        return new FiadoCustomerDetail
        {
            CustomerId = customerId,
            CustomerName = customerName,
            Phone = customerPhone,
            TotalCharges = charges,
            TotalPaid = paid,
            Balance = balance,
            Sales = sales,
            Payments = payments,
        };
    }

    public static int RegisterPayment(int customerId, FiadoReceberInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("recebimento de fiado");
        RequireFiadoReceber();
        if (customerId <= 0)
            throw new FiadoException("Cliente inválido.");

        var valor = ProductPriceHelper.RoundPrice(input.Amount);
        var juros = ProductPriceHelper.RoundPrice(Math.Max(0, input.InterestAmount));
        var abate = ProductPriceHelper.RoundPrice(input.PrincipalAmount);
        if (valor <= 0)
            throw new FiadoException("Informe o valor recebido.");
        if (abate <= 0)
            throw new FiadoException("Informe o abate do fiado.");
        if (Math.Abs(abate + juros - valor) > 0.02)
            throw new FiadoException("Total deve ser abate do fiado + juros.");

        var paidIso = DateBrHelper.ToIso(input.PaymentDate)
            ?? throw new FiadoException("Informe a data do recebimento (DD/MM/AAAA).");
        var payDate = DateTime.ParseExact(paidIso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var parts = NormalizeParts(input.Payments, valor, out var cashReceived, out var changeAmount);

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        if (LoadPerson(conn, customerId, tx) is not { } personInfo)
            throw new FiadoException("Cliente não encontrado.");
        var customerName = personInfo.Name;

        var balance = ProductPriceHelper.RoundPrice(CalcCharges(conn, customerId, tx) - CalcPayments(conn, customerId, tx));
        if (abate > balance + 0.009)
            throw new FiadoException($"Abate do fiado maior que o saldo em aberto (R$ {balance:N2}).");

        var noteParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(input.Notes))
            noteParts.Add(input.Notes.Trim());
        if (juros > 0.009)
            noteParts.Add($"Juros R$ {juros:N2}");
        if (changeAmount > 0.009 && cashReceived is not null)
            noteParts.Add($"Troco R$ {changeAmount:N2} (recebido R$ {cashReceived:N2})");

        var formaLabel = parts.Count == 1
            ? parts[0].PaymentType
            : string.Join(" + ", parts.Select(p => p.PaymentType));

        int paymentId;
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO fiado_payments (
                    customer_id, amount, interest_amount, payment_type, payment_date, notes, reversed
                ) VALUES ($cust, $amt, $juros, $type, $date, $notes, 0);
                SELECT last_insert_rowid();
                """;
            ins.Parameters.AddWithValue("$cust", customerId);
            ins.Parameters.AddWithValue("$amt", valor);
            ins.Parameters.AddWithValue("$juros", juros);
            ins.Parameters.AddWithValue("$type", formaLabel);
            ins.Parameters.AddWithValue("$date", paidIso);
            ins.Parameters.AddWithValue("$notes", noteParts.Count > 0 ? string.Join(" — ", noteParts) : DBNull.Value);
            paymentId = Convert.ToInt32(ins.ExecuteScalar());
        }

        CashService.RegisterFiadoRecebimento(conn, tx, payDate, paymentId, customerName,
            parts.Select(p => (p.PaymentType, p.Amount)).ToList(), juros,
            cashReceived, changeAmount);

        tx.Commit();
        return paymentId;
    }

    public static void ReversePayment(int paymentId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("estorno de fiado");
        RequireFiadoEstornar();
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        using (var load = conn.CreateCommand())
        {
            load.Transaction = tx;
            load.CommandText = "SELECT id, reversed FROM fiado_payments WHERE id = $id LIMIT 1;";
            load.Parameters.AddWithValue("$id", paymentId);
            using var reader = load.ExecuteReader();
            if (!reader.Read())
                throw new FiadoException("Recebimento não encontrado.");
            if (reader.GetInt32(1) != 0)
                throw new FiadoException("Recebimento já estornado.");
        }

        CashService.RemoveFiadoRecebimento(conn, tx, paymentId);

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE fiado_payments SET reversed = 1 WHERE id = $id;";
        upd.Parameters.AddWithValue("$id", paymentId);
        upd.ExecuteNonQuery();
        tx.Commit();
    }

    public static int LinkOrphanSales(int personId, string? orphanPartyKey = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("ajustar fiado");
        if (personId <= 0)
            throw new FiadoException("Informe o cliente para vincular.");

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        if (LoadPerson(conn, personId, tx) is not { } personInfo)
            throw new FiadoException("Cliente não encontrado.");
        var customerName = personInfo.Name;

        var saleIds = new List<(int Id, string PaymentType)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id, payment_type FROM sales
                WHERE customer_id IS NULL AND cancelled = 0;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                saleIds.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        var toLink = new List<int>();
        foreach (var s in saleIds)
        {
            if (SaleFiadoCharge(conn, s.Id, s.PaymentType, tx) <= 0.009)
                continue;
            if (!string.IsNullOrEmpty(orphanPartyKey)
                && !string.Equals(ResolvePartyKey(conn, s.Id, tx), orphanPartyKey, StringComparison.Ordinal))
                continue;
            toLink.Add(s.Id);
        }

        if (toLink.Count == 0)
            throw new FiadoException("Nenhuma venda fiado sem cliente para vincular.");

        foreach (var saleId in toLink)
        {
            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE sales SET customer_id = $cust WHERE id = $id;";
                upd.Parameters.AddWithValue("$cust", personId);
                upd.Parameters.AddWithValue("$id", saleId);
                upd.ExecuteNonQuery();
            }

            using var mov = conn.CreateCommand();
            mov.Transaction = tx;
            mov.CommandText = """
                UPDATE cash_movements
                SET party_name = $name,
                    description = $desc
                WHERE ref_type = 'sale' AND ref_id = $id
                  AND lower(kind) IN ('venda_fiado', 'venda')
                  AND session_id IN (SELECT id FROM cash_sessions);
                """;
            mov.Parameters.AddWithValue("$name", customerName);
            mov.Parameters.AddWithValue("$desc", $"VENDA PDV #{saleId} — FIADO — {customerName}");
            mov.Parameters.AddWithValue("$id", saleId);
            mov.ExecuteNonQuery();
        }

        tx.Commit();
        return toLink.Count;
    }

    /// <summary>
    /// Cancela vendas fiado sem cliente (não atribui a ninguém).
    /// Não devolve estoque — são vendas antigas já baixadas da loja.
    /// </summary>
    public static int DiscardOrphanSales(string? orphanPartyKey = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("ajustar fiado");
        RequireFiadoExcluir();
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var saleIds = new List<(int Id, string PaymentType)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id, payment_type FROM sales
                WHERE customer_id IS NULL AND cancelled = 0;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                saleIds.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        var toDiscard = new List<int>();
        double total = 0;
        foreach (var sale in saleIds)
        {
            var charge = SaleFiadoCharge(conn, sale.Id, sale.PaymentType, tx);
            if (charge <= 0.009)
                continue;
            if (!string.IsNullOrEmpty(orphanPartyKey)
                && !string.Equals(ResolvePartyKey(conn, sale.Id, tx), orphanPartyKey, StringComparison.Ordinal))
                continue;
            toDiscard.Add(sale.Id);
            total = ProductPriceHelper.RoundPrice(total + charge);
        }

        if (toDiscard.Count == 0)
            throw new FiadoException("Nenhuma venda fiado sem cliente para cancelar.");

        foreach (var saleId in toDiscard)
            PixSaleReverseService.ReverseForSale(saleId);

        foreach (var saleId in toDiscard)
        {
            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE sales SET cancelled = 1 WHERE id = $id AND customer_id IS NULL;";
                upd.Parameters.AddWithValue("$id", saleId);
                upd.ExecuteNonQuery();
            }

            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = """
                DELETE FROM cash_movements
                WHERE ref_type = 'sale' AND ref_id = $id AND lower(kind) = 'venda_fiado';
                """;
            del.Parameters.AddWithValue("$id", saleId);
            del.ExecuteNonQuery();
        }

        tx.Commit();

        AuditService.LogJson("cancelar", "fiado_orfaos", toDiscard.Count.ToString(),
            new { count = toDiscard.Count, total, party = orphanPartyKey, sale_ids = toDiscard.Take(200).ToList() },
            $"{toDiscard.Count} venda(s) fiado sem cliente cancelada(s) · R$ {total:N2}");

        return toDiscard.Count;
    }

    /// <summary>
    /// Remove o fiado do cliente: cancela as vendas fiado, devolve estoque,
    /// estorna recebimentos e tira da lista. Pensado para limpar testes/erros.
    /// </summary>
    public static (int SalesCancelled, int PaymentsReversed, double TotalCleared) ClearCustomerFiado(int customerId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("ajustar fiado");
        RequireFiadoExcluir();
        if (customerId <= 0)
            throw new FiadoException("Selecione um cliente.");

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        if (LoadPerson(conn, customerId, tx) is not { } personInfo)
            throw new FiadoException("Cliente não encontrado.");
        var customerName = personInfo.Name;

        var sales = new List<(int Id, string PaymentType, double Charge)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id, payment_type FROM sales
                WHERE customer_id = $id AND cancelled = 0;
                """;
            cmd.Parameters.AddWithValue("$id", customerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var payType = reader.GetString(1);
                var charge = SaleFiadoCharge(conn, id, payType, tx);
                if (charge > 0.009)
                    sales.Add((id, payType, charge));
            }
        }

        var paymentIds = new List<int>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id FROM fiado_payments
                WHERE customer_id = $id AND reversed = 0;
                """;
            cmd.Parameters.AddWithValue("$id", customerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                paymentIds.Add(reader.GetInt32(0));
        }

        if (sales.Count == 0 && paymentIds.Count == 0)
            throw new FiadoException("Este cliente não tem fiado para excluir.");

        var totalCleared = ProductPriceHelper.RoundPrice(sales.Sum(s => s.Charge));

        foreach (var sale in sales)
            PixSaleReverseService.ReverseForSale(sale.Id);

        foreach (var sale in sales)
        {
            RestoreSaleStock(conn, tx, sale.Id);
            CashService.DeleteSaleMovements(conn, tx, sale.Id);

            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE sales SET cancelled = 1 WHERE id = $id AND customer_id = $cust;";
            upd.Parameters.AddWithValue("$id", sale.Id);
            upd.Parameters.AddWithValue("$cust", customerId);
            upd.ExecuteNonQuery();
        }

        foreach (var paymentId in paymentIds)
            ForceReversePayment(conn, tx, paymentId);

        tx.Commit();

        AuditService.LogJson("excluir", "fiado_conta", customerId.ToString(),
            new
            {
                customer_id = customerId,
                customer = customerName,
                sales = sales.Count,
                payments = paymentIds.Count,
                total = totalCleared,
                sale_ids = sales.Select(s => s.Id).Take(200).ToList(),
            },
            $"Fiado excluído de {customerName}: {sales.Count} venda(s), {paymentIds.Count} recebimento(s) · R$ {totalCleared:N2}");

        return (sales.Count, paymentIds.Count, totalCleared);
    }

    private static void ForceReversePayment(SqliteConnection conn, SqliteTransaction tx, int paymentId)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = """
                DELETE FROM cash_movements
                WHERE (ref_type = 'fiado_payment' AND ref_id = $id)
                   OR (ref_type = 'fiado_payment_part'
                       AND ref_id >= $lo AND ref_id < $hi);
                """;
            del.Parameters.AddWithValue("$id", paymentId);
            del.Parameters.AddWithValue("$lo", paymentId * 100);
            del.Parameters.AddWithValue("$hi", paymentId * 100 + 100);
            del.ExecuteNonQuery();
        }

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE fiado_payments SET reversed = 1 WHERE id = $id;";
        upd.Parameters.AddWithValue("$id", paymentId);
        upd.ExecuteNonQuery();
    }

    private static void RestoreSaleStock(SqliteConnection conn, SqliteTransaction tx, int saleId)
    {
        var items = new List<(int ProductId, double Quantity, double StockQty)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT product_id, quantity, IFNULL(stock_qty, 0)
                FROM sale_items WHERE sale_id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", saleId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                items.Add((reader.GetInt32(0), reader.GetDouble(1), reader.GetDouble(2)));
        }

        foreach (var item in items)
        {
            var product = ProductService.GetById(item.ProductId);
            if (product is null)
                continue;

            var restoreQty = item.StockQty > 0.0001 ? item.StockQty : item.Quantity;
            foreach (var (comp, qtyBack) in ProductCompositionService.StockMovementsForSale(product, restoreQty))
            {
                StockService.ApplySaleRestore(conn, tx, comp.Id, qtyBack,
                    notes: $"Estorno fiado — Pedido #{saleId} (estoque devolvido)",
                    refType: "fiado", refId: saleId,
                    operation: "estorno_fiado");
            }
        }
    }

    public static bool HasOrphanSales()
    {
        using var conn = DatabaseService.OpenConnection();
        return AggregateOrphans(conn).Count > 0;
    }

    private static List<FiadoReceberPart> NormalizeParts(
        IReadOnlyList<FiadoReceberPart> payments, double total,
        out double? cashReceived, out double changeAmount)
    {
        cashReceived = null;
        changeAmount = 0;

        var parts = payments
            .Select(p => new FiadoReceberPart
            {
                PaymentType = ValidateForma(p.PaymentType),
                Amount = ProductPriceHelper.RoundPrice(p.Amount),
            })
            .Where(p => p.Amount > 0.009)
            .ToList();

        if (parts.Count == 0)
            throw new FiadoException("Informe ao menos uma forma de pagamento.");

        var nonCash = ProductPriceHelper.RoundPrice(
            parts.Where(p => !IsDinheiro(p.PaymentType)).Sum(p => p.Amount));
        var cashEntered = ProductPriceHelper.RoundPrice(
            parts.Where(p => IsDinheiro(p.PaymentType)).Sum(p => p.Amount));

        if (nonCash > total + 0.02)
            throw new FiadoException(
                $"Pix/cartão (R$ {nonCash:N2}) não pode ser maior que o total (R$ {total:N2}).");

        var cashNeed = ProductPriceHelper.RoundPrice(Math.Max(0, total - nonCash));
        if (cashNeed > 0.009 && cashEntered + 0.02 < cashNeed)
            throw new FiadoException(
                $"Falta R$ {ProductPriceHelper.RoundPrice(cashNeed - cashEntered):N2} para fechar o total.");

        if (cashEntered > cashNeed + 0.009)
        {
            cashReceived = cashEntered;
            changeAmount = ProductPriceHelper.RoundPrice(cashEntered - cashNeed);
        }

        var normalized = parts
            .Where(p => !IsDinheiro(p.PaymentType))
            .ToList();
        if (cashNeed > 0.009)
            normalized.Add(new FiadoReceberPart { PaymentType = "Dinheiro", Amount = cashNeed });

        var soma = ProductPriceHelper.RoundPrice(normalized.Sum(p => p.Amount));
        if (Math.Abs(soma - total) > 0.02)
            throw new FiadoException(
                $"A soma das formas (R$ {soma:N2}) deve ser igual ao total recebido (R$ {total:N2}).");

        return normalized;
    }

    private static bool IsDinheiro(string paymentType) =>
        paymentType.Equals("Dinheiro", StringComparison.OrdinalIgnoreCase);

    private static string ValidateForma(string? paymentType)
    {
        var forma = CashServiceNormalize(paymentType);
        if (!FormasRecebimento.Contains(forma))
            throw new FiadoException("Forma inválida — use Dinheiro, Pix, Cartão Débito ou Cartão Crédito.");
        return FormasRecebimento.First(f => f.Equals(forma, StringComparison.OrdinalIgnoreCase));
    }

    private static string CashServiceNormalize(string? paymentType)
    {
        var s = (paymentType ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return "—";
        var low = s.ToLowerInvariant();
        if (low is "dinheiro" or "cash") return "Dinheiro";
        if (low == "pix") return "Pix";
        if (low.Contains("debito") || low.Contains("débito")) return "Cartão Débito";
        if (low.Contains("credito") || low.Contains("crédito")) return "Cartão Crédito";
        return s;
    }

    private sealed class AggData
    {
        public double TotalCharges;
        public int SalesCount;
        public string? LastSaleAt;
    }

    private static Dictionary<int, AggData> AggregateCustomers(SqliteConnection conn)
    {
        var raw = new List<(int SaleId, int CustomerId, string PaymentType, string? CreatedAt)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, customer_id, payment_type, created_at
                FROM sales
                WHERE customer_id IS NOT NULL AND cancelled = 0;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                raw.Add((
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        var agg = new Dictionary<int, AggData>();
        foreach (var (saleId, customerId, paymentType, created) in raw)
        {
            var charge = SaleFiadoCharge(conn, saleId, paymentType);
            if (charge <= 0.009)
                continue;

            if (!agg.TryGetValue(customerId, out var data))
            {
                data = new AggData();
                agg[customerId] = data;
            }

            data.TotalCharges = ProductPriceHelper.RoundPrice(data.TotalCharges + charge);
            data.SalesCount++;
            if (created is not null && (data.LastSaleAt is null || string.CompareOrdinal(created, data.LastSaleAt) > 0))
                data.LastSaleAt = created;
        }
        return agg;
    }

    private static List<FiadoContaRow> AggregateOrphans(SqliteConnection conn)
    {
        var raw = new List<(int SaleId, string PaymentType, string? CreatedAt)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, payment_type, created_at
                FROM sales
                WHERE customer_id IS NULL AND cancelled = 0;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                raw.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        var groups = new Dictionary<string, (string Label, double Total, int Count, string? LastAt)>(
            StringComparer.Ordinal);

        foreach (var (saleId, paymentType, created) in raw)
        {
            var charge = SaleFiadoCharge(conn, saleId, paymentType);
            if (charge <= 0.009)
                continue;

            var key = ResolvePartyKey(conn, saleId);
            var label = string.IsNullOrEmpty(key) || key == "_"
                ? "(Sem nome no cupom — sem cliente)"
                : key;

            if (!groups.TryGetValue(key, out var g))
                g = (label, 0, 0, null);

            var last = g.LastAt;
            if (created is not null && (last is null || string.CompareOrdinal(created, last) > 0))
                last = created;

            groups[key] = (
                label,
                ProductPriceHelper.RoundPrice(g.Total + charge),
                g.Count + 1,
                last);
        }

        return groups
            .OrderByDescending(kv => kv.Value.Total)
            .Select(kv => new FiadoContaRow
            {
                CustomerId = 0,
                CustomerName = kv.Value.Label,
                Phone = "",
                TotalCharges = kv.Value.Total,
                TotalPaid = 0,
                TotalInterest = 0,
                Balance = kv.Value.Total,
                SalesCount = kv.Value.Count,
                LastSaleBr = FormatBrDateTime(kv.Value.LastAt),
                Orphan = true,
                OrphanPartyKey = kv.Key,
            })
            .ToList();
    }

    /// <summary>
    /// Recria clientes apagados (sales.customer_id sem people) e tenta ligar órfãs
    /// pelo nome/CPF gravado na descrição do movimento de caixa.
    /// </summary>
    private static void RepairMissingFiadoCustomers(SqliteConnection conn)
    {
        if (StoreNetworkMode.IsClient)
            return;
        try
        {
            // Agora o FK aponta para people — realinha pelo nome do cupom se ainda houver divergência.
            ReassignFiadoByPartyName(conn);
            RecreateDanglingFiadoPeople(conn);
        }
        catch
        {
        }
    }

    private static void ReassignFiadoByPartyName(SqliteConnection conn)
    {
        var rows = new List<(int SaleId, int CustomerId, string Party)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.id, IFNULL(s.customer_id, 0), TRIM(m.party_name)
                FROM sales s
                JOIN cash_movements m ON m.ref_type = 'sale' AND m.ref_id = s.id
                WHERE s.cancelled = 0
                  AND lower(IFNULL(s.payment_type, '')) LIKE '%fiado%'
                  AND IFNULL(TRIM(m.party_name), '') <> '';
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        }

        var cache = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (saleId, currentId, party) in rows)
        {
            var key = NormalizeNameKey(party);
            if (string.IsNullOrEmpty(key))
                continue;

            using var tx = conn.BeginTransaction();
            try
            {
                if (!cache.TryGetValue(key, out var targetId))
                {
                    targetId = FindPersonIdByPartyLabel(conn, tx, party) ?? 0;
                    if (targetId <= 0)
                        targetId = CreateClienteFromPartyLabel(conn, tx, party) ?? 0;
                    if (targetId <= 0 || !PersonExists(conn, tx, targetId))
                    {
                        tx.Rollback();
                        continue;
                    }
                    cache[key] = targetId;
                }

                if (targetId != currentId)
                    SetSaleCustomer(conn, tx, saleId, targetId, onlyIfNull: false);

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                cache.Remove(key);
            }
        }
    }

    private static void RecreateDanglingFiadoPeople(SqliteConnection conn)
    {
        var dangling = new List<(int CustomerId, int SaleId)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.customer_id, s.id
                FROM sales s
                WHERE s.cancelled = 0
                  AND s.customer_id IS NOT NULL
                  AND s.customer_id NOT IN (SELECT id FROM people)
                  AND lower(IFNULL(s.payment_type,'')) LIKE '%fiado%';
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                dangling.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        foreach (var group in dangling.GroupBy(x => x.CustomerId))
        {
            using var tx = conn.BeginTransaction();
            try
            {
                var label = $"CLIENTE FIADO #{group.Key}";
                foreach (var saleId in group.Select(g => g.SaleId))
                {
                    var party = ResolvePartyLabel(conn, saleId, tx);
                    if (!string.IsNullOrWhiteSpace(party) && party != "_")
                    {
                        label = party;
                        break;
                    }
                }
                EnsurePersonWithId(conn, tx, group.Key, label);
                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { /* ignore */ }
            }
        }
    }

    private static bool PersonExists(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        if (id <= 0) return false;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM people WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is not null;
    }

    private static void SetSaleCustomer(
        SqliteConnection conn, SqliteTransaction tx, int saleId, int customerId, bool onlyIfNull)
    {
        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = onlyIfNull
            ? "UPDATE sales SET customer_id = $cust WHERE id = $id AND customer_id IS NULL;"
            : "UPDATE sales SET customer_id = $cust WHERE id = $id;";
        upd.Parameters.AddWithValue("$cust", customerId);
        upd.Parameters.AddWithValue("$id", saleId);
        upd.ExecuteNonQuery();
    }

    private static bool NamesLooselyMatch(string a, string b)
    {
        var na = NormalizeNameKey(a);
        var nb = NormalizeNameKey(b);
        if (string.IsNullOrEmpty(na) || string.IsNullOrEmpty(nb))
            return false;
        return na == nb || na.Contains(nb) || nb.Contains(na);
    }

    private static string NormalizeNameKey(string value)
    {
        var (name, _) = SplitPartyLabel(value);
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static int? CreateClienteFromPartyLabel(
        SqliteConnection conn, SqliteTransaction tx, string label)
    {
        var (displayName, doc) = SplitPartyLabel(label);
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        // Já existe (corrida entre vendas do mesmo nome)?
        var existing = FindPersonIdByPartyLabel(conn, tx, label);
        if (existing is not null)
            return existing;

        var roles = PersonRoles.ForNewCliente().ToJson();
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO people (
                    person_type, person_kind, name, cpf_cnpj, roles_json, active, created_at
                ) VALUES (
                    'CLIENTE', 'fisica', $name, $doc, $roles, 1, datetime('now','localtime')
                );
                """;
            ins.Parameters.AddWithValue("$name", displayName);
            ins.Parameters.AddWithValue("$doc", (object?)doc ?? DBNull.Value);
            ins.Parameters.AddWithValue("$roles", roles);
            ins.ExecuteNonQuery();
        }

        using var idCmd = conn.CreateCommand();
        idCmd.Transaction = tx;
        idCmd.CommandText = "SELECT last_insert_rowid();";
        var id = Convert.ToInt32(idCmd.ExecuteScalar());
        return id > 0 ? id : null;
    }

    private static void EnsurePersonWithId(
        SqliteConnection conn, SqliteTransaction tx, int id, string name)
    {
        if (id <= 0 || PersonExists(conn, tx, id))
            return;

        var (displayName, doc) = SplitPartyLabel(name);
        // CPF já usado por outro cadastro → cria sem documento para não quebrar UNIQUE
        if (doc is not null)
        {
            using var docCheck = conn.CreateCommand();
            docCheck.Transaction = tx;
            docCheck.CommandText = """
                SELECT id FROM people
                WHERE REPLACE(REPLACE(REPLACE(IFNULL(cpf_cnpj,''),'.',''),'-',''),'/','') = $d
                LIMIT 1;
                """;
            docCheck.Parameters.AddWithValue("$d", doc);
            if (docCheck.ExecuteScalar() is not null)
                doc = null;
        }

        var roles = PersonRoles.ForNewCliente().ToJson();
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO people (
                id, person_type, person_kind, name, cpf_cnpj, roles_json, active, created_at
            ) VALUES (
                $id, 'CLIENTE', 'fisica', $name, $doc, $roles, 1, datetime('now','localtime')
            );
            """;
        ins.Parameters.AddWithValue("$id", id);
        ins.Parameters.AddWithValue("$name", displayName);
        ins.Parameters.AddWithValue("$doc", (object?)doc ?? DBNull.Value);
        ins.Parameters.AddWithValue("$roles", roles);
        ins.ExecuteNonQuery();
    }

    private static int? FindPersonIdByPartyLabel(
        SqliteConnection conn, SqliteTransaction tx, string label)
    {
        var (displayName, doc) = SplitPartyLabel(label);
        if (doc is not null)
        {
            using var byDoc = conn.CreateCommand();
            byDoc.Transaction = tx;
            byDoc.CommandText = """
                SELECT id FROM people
                WHERE REPLACE(REPLACE(REPLACE(IFNULL(cpf_cnpj,''),'.',''),'-',''),'/','') = $d
                LIMIT 1;
                """;
            byDoc.Parameters.AddWithValue("$d", doc);
            var found = byDoc.ExecuteScalar();
            if (found is not null)
                return Convert.ToInt32(found);
        }

        using (var byName = conn.CreateCommand())
        {
            byName.Transaction = tx;
            byName.CommandText = """
                SELECT id FROM people
                WHERE UPPER(TRIM(name)) = UPPER(TRIM($name))
                LIMIT 1;
                """;
            byName.Parameters.AddWithValue("$name", displayName);
            var idObj = byName.ExecuteScalar();
            if (idObj is not null)
                return Convert.ToInt32(idObj);
        }

        // Fallback: compara só letras/números (ignora pontuação/espaços)
        var want = NormalizeNameKey(displayName);
        if (string.IsNullOrEmpty(want))
            return null;

        using var scan = conn.CreateCommand();
        scan.Transaction = tx;
        scan.CommandText = "SELECT id, name FROM people WHERE active = 1;";
        using var reader = scan.ExecuteReader();
        while (reader.Read())
        {
            if (NormalizeNameKey(reader.GetString(1)) == want)
                return reader.GetInt32(0);
        }
        return null;
    }

    private static string ResolvePartyKey(
        SqliteConnection conn, int saleId, SqliteTransaction? tx = null)
    {
        var label = ResolvePartyLabel(conn, saleId, tx);
        return string.IsNullOrWhiteSpace(label) ? "_" : label.Trim().ToUpperInvariant();
    }

    private static string ResolvePartyLabel(
        SqliteConnection conn, int saleId, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT IFNULL(party_name,''), IFNULL(description,'')
            FROM cash_movements
            WHERE ref_type = 'sale' AND ref_id = $id
            ORDER BY id DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return "_";

        var party = reader.GetString(0).Trim();
        if (!string.IsNullOrWhiteSpace(party))
            return party;

        var desc = reader.GetString(1);
        return ExtractNameFromFiadoDescription(desc) ?? "_";
    }

    private static string? ExtractNameFromFiadoDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        // "VENDA PDV #4 — FIADO — NOME" ou "… FIADO R$ 16.00 — NOME"
        var idx = description.IndexOf("FIADO", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var after = description[(idx + 5)..].Trim();
        // remove "R$ 16.00" se houver
        if (after.StartsWith("R$", StringComparison.OrdinalIgnoreCase))
        {
            var dash = after.IndexOfAny(['—', '-', '–']);
            if (dash >= 0)
                after = after[(dash + 1)..].Trim();
        }

        after = after.TrimStart('—', '-', '–', ' ', '\t');
        return string.IsNullOrWhiteSpace(after) ? null : after.Trim();
    }

    private static (string Name, string? DocDigits) SplitPartyLabel(string label)
    {
        var raw = label.Trim();
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length is 11 or 14)
        {
            // Nome depois do documento formatado no início
            var withoutDoc = System.Text.RegularExpressions.Regex.Replace(
                raw, @"^[\d.\-\/\s]+", "").Trim();
            if (string.IsNullOrWhiteSpace(withoutDoc))
                withoutDoc = raw;
            return (withoutDoc.ToUpperInvariant(), digits);
        }

        return (raw.ToUpperInvariant(), null);
    }

    private static string? GetDominantPartyName(SqliteConnection conn, int customerId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TRIM(m.party_name) AS party, COUNT(*) AS n
            FROM sales s
            JOIN cash_movements m ON m.ref_type = 'sale' AND m.ref_id = s.id
            WHERE s.customer_id = $id AND s.cancelled = 0
              AND lower(IFNULL(s.payment_type,'')) LIKE '%fiado%'
              AND IFNULL(TRIM(m.party_name),'') <> ''
            GROUP BY TRIM(m.party_name)
            ORDER BY n DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", customerId);
        return cmd.ExecuteScalar() as string;
    }

    private static double SaleFiadoCharge(
        SqliteConnection conn, int saleId, string paymentType, SqliteTransaction? tx = null)
    {
        var low = (paymentType ?? "").ToLowerInvariant();
        // Venda 100% fiado: usa o total da venda (mais confiável que amount_in antigo).
        if (low.Contains("fiado") && !low.Contains('+') && !low.Contains("din"))
        {
            using var sale = conn.CreateCommand();
            if (tx is not null) sale.Transaction = tx;
            sale.CommandText = "SELECT total FROM sales WHERE id = $id LIMIT 1;";
            sale.Parameters.AddWithValue("$id", saleId);
            return ProductPriceHelper.RoundPrice(Convert.ToDouble(sale.ExecuteScalar() ?? 0));
        }

        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT COALESCE(SUM(amount_in), 0) FROM cash_movements
            WHERE ref_type = 'sale' AND ref_id = $id AND lower(kind) = 'venda_fiado';
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var amt = Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
        if (amt > 0.009)
            return ProductPriceHelper.RoundPrice(amt);

        if (low.Contains("fiado"))
        {
            using var sale = conn.CreateCommand();
            if (tx is not null) sale.Transaction = tx;
            sale.CommandText = "SELECT total FROM sales WHERE id = $id LIMIT 1;";
            sale.Parameters.AddWithValue("$id", saleId);
            return ProductPriceHelper.RoundPrice(Convert.ToDouble(sale.ExecuteScalar() ?? 0));
        }
        return 0;
    }

    private static double CalcCharges(SqliteConnection conn, int customerId, SqliteTransaction? tx = null)
    {
        var raw = new List<(int SaleId, string PaymentType)>();
        using (var cmd = conn.CreateCommand())
        {
            if (tx is not null) cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id, payment_type FROM sales
                WHERE customer_id = $id AND cancelled = 0;
                """;
            cmd.Parameters.AddWithValue("$id", customerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                raw.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        double total = 0;
        foreach (var (saleId, paymentType) in raw)
            total += SaleFiadoCharge(conn, saleId, paymentType, tx);
        return ProductPriceHelper.RoundPrice(total);
    }

    private static double CalcPayments(SqliteConnection conn, int customerId, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT amount, interest_amount FROM fiado_payments
            WHERE customer_id = $id AND reversed = 0;
            """;
        cmd.Parameters.AddWithValue("$id", customerId);
        double total = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var amount = reader.GetDouble(0);
            var interest = reader.GetDouble(1);
            total += Math.Max(0, amount - interest);
        }
        return ProductPriceHelper.RoundPrice(total);
    }

    private static double CalcInterest(SqliteConnection conn, int customerId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(interest_amount), 0) FROM fiado_payments
            WHERE customer_id = $id AND reversed = 0;
            """;
        cmd.Parameters.AddWithValue("$id", customerId);
        return ProductPriceHelper.RoundPrice(Convert.ToDouble(cmd.ExecuteScalar() ?? 0));
    }

    private static List<FiadoSaleItemRow> LoadSaleItems(SqliteConnection conn, int saleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT product_code, product_name, quantity, unit_price, subtotal
            FROM sale_items WHERE sale_id = $id ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var items = new List<FiadoSaleItemRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new FiadoSaleItemRow
            {
                ProductCode = reader.GetString(0),
                ProductName = reader.GetString(1),
                Quantity = reader.GetDouble(2),
                UnitPrice = reader.GetDouble(3),
                Subtotal = reader.GetDouble(4),
            });
        }
        return items;
    }

    private static (string Name, string Phone)? LoadPerson(
        SqliteConnection conn, int personId, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT name,
                   COALESCE(NULLIF(TRIM(phone), ''), NULLIF(TRIM(cell1), ''), NULLIF(TRIM(whatsapp), ''), '')
            FROM people WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", personId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return (reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1));
    }

    private static string FormatBrDateTime(string? iso) =>
        DateBrHelper.FormatUtcToBrazil(iso, "dd/MM/yyyy HH:mm");

    private static void RequireFiadoReceber()
    {
        if (!AccessControl.AllowsLocalUser("FiadoReceber"))
            throw new FiadoException("Seu usuário não tem permissão para receber fiado.");
    }

    private static void RequireFiadoEstornar()
    {
        if (!AccessControl.AllowsLocalUser("FiadoEstornar"))
            throw new FiadoException("Seu usuário não tem permissão para estornar recebimento de fiado.");
    }

    private static void RequireFiadoExcluir()
    {
        if (!AccessControl.AllowsLocalUser("FiadoExcluir"))
            throw new FiadoException("Seu usuário não tem permissão para excluir ou limpar fiado.");
    }
}

public class FiadoException : Exception
{
    public FiadoException(string message) : base(message) { }
}
