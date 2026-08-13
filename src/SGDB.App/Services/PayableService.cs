using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class PayableService
{
    /// <summary>Gera Contas a Pagar para compra fechada que ainda não tem título (ex.: NF-e antiga).</summary>
    public static bool EnsurePayablesForClosedPurchase(int purchaseId)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.EnsurePayablesForPurchase(purchaseId);
        return EnsurePayablesForClosedPurchaseLocal(purchaseId);
    }

    public static bool EnsurePayablesForClosedPurchaseLocal(int purchaseId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("gerar títulos a pagar");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        using (var exists = conn.CreateCommand())
        {
            exists.Transaction = tx;
            exists.CommandText = "SELECT 1 FROM payable_titles WHERE purchase_id = $id LIMIT 1;";
            exists.Parameters.AddWithValue("$id", purchaseId);
            if (exists.ExecuteScalar() is not null)
            {
                tx.Commit();
                return false;
            }
        }

        SyncFromPurchase(conn, tx, purchaseId);
        tx.Commit();

        using var check = DatabaseService.OpenConnection();
        using var cmd = check.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM payable_titles WHERE purchase_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return cmd.ExecuteScalar() is not null;
    }

    public static void SyncFromPurchase(SqliteConnection conn, SqliteTransaction tx, int purchaseId)
    {
        using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = """
            SELECT p.id, p.supplier_id, p.number, p.emission_date, p.total, p.nfe_key, p.notes, p.status
            FROM purchases p WHERE p.id = $id LIMIT 1;
            """;
        check.Parameters.AddWithValue("$id", purchaseId);
        using var reader = check.ExecuteReader();
        if (!reader.Read())
            return;

        var status = reader.GetString(7);
        if (status != "fechada")
            return;

        var supplierId = reader.GetInt32(1);
        var number = reader.GetString(2);
        var emissionDate = reader.GetString(3);
        var total = reader.GetDouble(4);
        var nfeKey = reader.IsDBNull(5) ? null : reader.GetString(5);
        var notes = reader.IsDBNull(6) ? null : reader.GetString(6);
        reader.Close();

        var financeiro = PurchaseFinanceHelper.ExtractFinanceiroFromNotes(notes);
        // NF-e importada sem parcelas no JSON: gera 1 boleto pendente com o total da compra
        if (financeiro?.Parcelas is null || financeiro.Parcelas.Count == 0)
        {
            financeiro = new PurchaseFinanceiroMeta
            {
                Entrada = 0,
                Qtd = 1,
                Parcelas =
                [
                    new PurchaseParcelaDraft
                    {
                        Vencimento = DateBrHelper.FormatIso(emissionDate) is var br && br.Length >= 8
                            ? br
                            : DateBrHelper.TodayBr(),
                        Tipo = "Boleto",
                        Valor = ProductPriceHelper.RoundPrice(total),
                    },
                ],
            };
        }

        using var exists = conn.CreateCommand();
        exists.Transaction = tx;
        exists.CommandText = "SELECT 1 FROM payable_titles WHERE purchase_id = $id LIMIT 1;";
        exists.Parameters.AddWithValue("$id", purchaseId);
        if (exists.ExecuteScalar() is not null)
            return;

        LegacySupplierBridge.EnsureMirrored(supplierId, conn, tx);

        int titleId;
        using (var insertTitle = conn.CreateCommand())
        {
            insertTitle.Transaction = tx;
            insertTitle.CommandText = """
                INSERT INTO payable_titles (
                    supplier_id, purchase_id, number, emission_date, total_amount,
                    discount, interest, doc_ref, expense_category, notes, created_at
                ) VALUES (
                    $supplier, $purchase, $number, $emission, $total,
                    0, 0, $doc, $cat, $notes, datetime('now','localtime')
                );
                SELECT last_insert_rowid();
                """;
            insertTitle.Parameters.AddWithValue("$supplier", supplierId);
            insertTitle.Parameters.AddWithValue("$purchase", purchaseId);
            insertTitle.Parameters.AddWithValue("$number", number);
            insertTitle.Parameters.AddWithValue("$emission", emissionDate);
            insertTitle.Parameters.AddWithValue("$total", total);
            insertTitle.Parameters.AddWithValue("$doc", (object?)(nfeKey?.Length > 60 ? nfeKey[..60] : nfeKey) ?? DBNull.Value);
            insertTitle.Parameters.AddWithValue("$cat", ExpenseCategories.Default);
            insertTitle.Parameters.AddWithValue("$notes", $"Compra #{purchaseId}");
            titleId = Convert.ToInt32(insertTitle.ExecuteScalar());
        }

        var seq = 1;
        foreach (var parc in financeiro.Parcelas)
        {
            var dueIso = DateBrHelper.ToIso(parc.Vencimento) ?? emissionDate;
            using var insertInst = conn.CreateCommand();
            insertInst.Transaction = tx;
            insertInst.CommandText = """
                INSERT INTO payable_installments (
                    title_id, seq, due_date, amount, discount, interest,
                    payment_type, status, paid_amount
                ) VALUES (
                    $title, $seq, $due, $amount, 0, 0,
                    $type, 'pendente', 0
                );
                """;
            insertInst.Parameters.AddWithValue("$title", titleId);
            insertInst.Parameters.AddWithValue("$seq", seq++);
            insertInst.Parameters.AddWithValue("$due", dueIso);
            insertInst.Parameters.AddWithValue("$amount", parc.Valor);
            insertInst.Parameters.AddWithValue("$type", PurchaseFinanceHelper.NormalizeTipoCobranca(parc.Tipo));
            insertInst.ExecuteNonQuery();
        }
    }

    public static IReadOnlyList<PayableTitleRow> ListTitles(
        string situacao = "pendentes",
        int? supplierId = null,
        string? dateFromBr = null,
        string? dateToBr = null,
        int? purchaseId = null)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListPayableTitles(situacao, supplierId, dateFromBr, dateToBr, purchaseId);
        return ListTitlesLocal(situacao, supplierId, dateFromBr, dateToBr, purchaseId);
    }

    public static IReadOnlyList<PayableTitleRow> ListTitlesLocal(
        string situacao = "pendentes",
        int? supplierId = null,
        string? dateFromBr = null,
        string? dateToBr = null,
        int? purchaseId = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.id, t.purchase_id, t.number, t.emission_date, t.supplier_id, p.name,
                   COALESCE(t.doc_ref, ''), t.total_amount, t.discount, t.interest
            FROM payable_titles t
            JOIN people p ON p.id = t.supplier_id
            WHERE 1 = 1
            """;
        if (supplierId is int sid)
        {
            cmd.CommandText += " AND t.supplier_id = $supplier";
            cmd.Parameters.AddWithValue("$supplier", sid);
        }
        var isoFrom = DateBrHelper.ToIso(dateFromBr);
        if (isoFrom is not null)
        {
            cmd.CommandText += " AND t.emission_date >= $from";
            cmd.Parameters.AddWithValue("$from", isoFrom);
        }
        var isoTo = DateBrHelper.ToIso(dateToBr);
        if (isoTo is not null)
        {
            cmd.CommandText += " AND t.emission_date <= $to";
            cmd.Parameters.AddWithValue("$to", isoTo);
        }
        if (purchaseId is int pid)
        {
            cmd.CommandText += " AND t.purchase_id = $purchase";
            cmd.Parameters.AddWithValue("$purchase", pid);
        }
        cmd.CommandText += " ORDER BY t.emission_date DESC, t.id DESC LIMIT 500;";

        var raw = new List<(int Id, int? PurchaseId, string Number, string Emission, int SupplierId,
            string SupplierName, string DocRef, double Total, double Discount, double Interest)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                raw.Add((
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.IsDBNull(5) ? "" : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetDouble(7),
                    reader.GetDouble(8),
                    reader.GetDouble(9)));
            }
        }

        var today = DateTime.Today;
        var rows = new List<PayableTitleRow>();
        foreach (var t in raw)
        {
            var insts = LoadInstallmentsForTitle(conn, t.Id);
            var sit = TitleSituacao(insts, today);
            if (situacao == "pagas" && sit != "pago")
                continue;
            if (situacao == "pendentes" && sit == "pago")
                continue;

            rows.Add(new PayableTitleRow
            {
                Id = t.Id,
                PurchaseId = t.PurchaseId,
                Number = t.Number,
                EmissionDate = t.Emission,
                SupplierId = t.SupplierId,
                SupplierName = t.SupplierName,
                DocRef = t.DocRef,
                TotalAmount = t.Total,
                Discount = t.Discount,
                Interest = t.Interest,
                PaidAmount = insts.Sum(i => i.PaidAmount),
                PaidDate = insts.Where(i => i.PaidDate is not null).Select(i => i.PaidDate).Max(),
                Situacao = sit,
                InstallmentCount = insts.Count,
            });
        }
        return rows;
    }

    public static IReadOnlyList<PayableInstallmentRow> ListInstallments(
        string situacao = "pendentes",
        int? supplierId = null,
        string? dateFromBr = null,
        string? dateToBr = null,
        int? purchaseId = null)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListPayableInstallments(situacao, supplierId, dateFromBr, dateToBr, purchaseId);
        return ListInstallmentsLocal(situacao, supplierId, dateFromBr, dateToBr, purchaseId);
    }

    public static IReadOnlyList<PayableInstallmentRow> ListInstallmentsLocal(
        string situacao = "pendentes",
        int? supplierId = null,
        string? dateFromBr = null,
        string? dateToBr = null,
        int? purchaseId = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT pi.id, pi.title_id, t.purchase_id, t.number, pi.seq, t.emission_date, pi.due_date,
                   t.supplier_id, p.name, COALESCE(t.doc_ref, ''),
                   pi.amount, pi.discount, pi.interest, pi.paid_amount, pi.paid_date,
                   pi.payment_type, pi.status
            FROM payable_installments pi
            JOIN payable_titles t ON t.id = pi.title_id
            JOIN people p ON p.id = t.supplier_id
            WHERE 1 = 1
            """;
        if (supplierId is int sid)
        {
            cmd.CommandText += " AND t.supplier_id = $supplier";
            cmd.Parameters.AddWithValue("$supplier", sid);
        }
        var isoFrom = DateBrHelper.ToIso(dateFromBr);
        if (isoFrom is not null)
        {
            cmd.CommandText += " AND pi.due_date >= $from";
            cmd.Parameters.AddWithValue("$from", isoFrom);
        }
        var isoTo = DateBrHelper.ToIso(dateToBr);
        if (isoTo is not null)
        {
            cmd.CommandText += " AND pi.due_date <= $to";
            cmd.Parameters.AddWithValue("$to", isoTo);
        }
        if (purchaseId is int pid)
        {
            cmd.CommandText += " AND t.purchase_id = $purchase";
            cmd.Parameters.AddWithValue("$purchase", pid);
        }
        cmd.CommandText += " ORDER BY pi.due_date ASC, pi.id ASC LIMIT 1000;";

        var today = DateTime.Today;
        var rows = new List<PayableInstallmentRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var status = reader.GetString(16);
            var due = reader.GetString(6);
            var sit = InstallmentSituacao(status, due, today);
            if (situacao == "pagas" && sit != "pago")
                continue;
            if (situacao == "pendentes" && sit == "pago")
                continue;

            rows.Add(new PayableInstallmentRow
            {
                Id = reader.GetInt32(0),
                TitleId = reader.GetInt32(1),
                PurchaseId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Number = reader.GetString(3),
                Seq = reader.GetInt32(4),
                EmissionDate = reader.GetString(5),
                DueDate = due,
                SupplierId = reader.GetInt32(7),
                SupplierName = reader.IsDBNull(8) ? "" : reader.GetString(8),
                DocRef = reader.GetString(9),
                Amount = reader.GetDouble(10),
                Discount = reader.GetDouble(11),
                Interest = reader.GetDouble(12),
                PaidAmount = reader.GetDouble(13),
                PaidDate = reader.IsDBNull(14) ? null : reader.GetString(14),
                PaymentType = reader.GetString(15),
                Status = status,
                Situacao = sit,
            });
        }
        return rows;
    }

    public static PayableInstallmentDetail? GetInstallment(int installmentId)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.GetPayableInstallment(installmentId);
        using var conn = DatabaseService.OpenConnection();
        return GetInstallment(conn, null, installmentId);
    }

    public static IReadOnlyList<PayableInstallmentDetail> ListInstallmentsOfTitle(int titleId)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListPayableInstallmentsOfTitle(titleId);
        using var conn = DatabaseService.OpenConnection();
        return LoadInstallmentsForTitle(conn, titleId);
    }

    public static IReadOnlyList<PayableInstallmentDetail> ListInstallmentsOfTitleLocal(int titleId)
    {
        using var conn = DatabaseService.OpenConnection();
        return LoadInstallmentsForTitle(conn, titleId);
    }

    public static PayableInstallmentDetail? GetInstallmentLocal(int installmentId)
    {
        using var conn = DatabaseService.OpenConnection();
        return GetInstallment(conn, null, installmentId);
    }

    public static int CreateTitle(PayableTitleCreateInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("título a pagar avulso");
        if (input.SupplierId <= 0)
            throw new PayableException("Selecione o fornecedor.");
        if (string.IsNullOrWhiteSpace(input.Number))
            throw new PayableException("Informe o número/documento.");
        if (string.IsNullOrEmpty(DateBrHelper.ToIso(input.EmissionDate)))
            throw new PayableException("Informe emissão (DD/MM/AAAA).");
        if (string.IsNullOrEmpty(DateBrHelper.ToIso(input.DueDate)))
            throw new PayableException("Informe o vencimento (DD/MM/AAAA).");
        if (input.TotalAmount < 0)
            throw new PayableException("Informe o valor.");

        var emission = DateBrHelper.ToIso(input.EmissionDate)!;
        var due = DateBrHelper.ToIso(input.DueDate)!;
        var category = NormalizeCategory(input.ExpenseCategory);

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT 1 FROM people WHERE id = $id LIMIT 1;";
            check.Parameters.AddWithValue("$id", input.SupplierId);
            if (check.ExecuteScalar() is null)
                throw new PayableException("Fornecedor não encontrado.");
        }

        LegacySupplierBridge.EnsureMirrored(input.SupplierId, conn, tx);

        int titleId;
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO payable_titles (
                    supplier_id, number, emission_date, total_amount,
                    discount, interest, expense_category, created_at
                ) VALUES (
                    $supplier, $number, $emission, $total,
                    0, 0, $cat, datetime('now','localtime')
                );
                SELECT last_insert_rowid();
                """;
            ins.Parameters.AddWithValue("$supplier", input.SupplierId);
            ins.Parameters.AddWithValue("$number", input.Number.Trim());
            ins.Parameters.AddWithValue("$emission", emission);
            ins.Parameters.AddWithValue("$total", ProductPriceHelper.RoundPrice(input.TotalAmount));
            ins.Parameters.AddWithValue("$cat", (object?)category ?? DBNull.Value);
            titleId = Convert.ToInt32(ins.ExecuteScalar());
        }

        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO payable_installments (
                    title_id, seq, due_date, amount, discount, interest,
                    payment_type, status, paid_amount
                ) VALUES (
                    $title, 1, $due, $amount, 0, 0,
                    $type, 'pendente', 0
                );
                """;
            ins.Parameters.AddWithValue("$title", titleId);
            ins.Parameters.AddWithValue("$due", due);
            ins.Parameters.AddWithValue("$amount", ProductPriceHelper.RoundPrice(input.TotalAmount));
            ins.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(input.PaymentType) ? "Boleto" : input.PaymentType.Trim());
            ins.ExecuteNonQuery();
        }

        tx.Commit();
        return titleId;
    }

    public static void PayInstallment(int installmentId, PayablePayInput input)
    {
        if (StoreNetworkMode.IsClient)
        {
            StoreNetworkClient.PayPayableInstallment(installmentId, input);
            return;
        }
        PayInstallmentLocal(installmentId, input);
    }

    public static void PayInstallmentLocal(int installmentId, PayablePayInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("baixar parcela a pagar");
        if (string.IsNullOrEmpty(DateBrHelper.ToIso(input.PaidDate)))
            throw new PayableException("Informe a data do pagamento (DD/MM/AAAA).");
        if (input.PaidAmount < 0)
            throw new PayableException("Informe o valor pago.");

        var paidIso = DateBrHelper.ToIso(input.PaidDate)!;

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var inst = GetInstallment(conn, tx, installmentId)
            ?? throw new PayableException("Parcela não encontrada.");

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE payable_installments SET
                    paid_amount = $paid,
                    paid_date = $date,
                    discount = $disc,
                    interest = $juros,
                    multa = $multa,
                    notes = $notes,
                    financial_account = $conta,
                    payment_type = $type,
                    status = 'pago'
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$id", installmentId);
            upd.Parameters.AddWithValue("$paid", ProductPriceHelper.RoundPrice(input.PaidAmount));
            upd.Parameters.AddWithValue("$date", paidIso);
            upd.Parameters.AddWithValue("$disc", ProductPriceHelper.RoundPrice(input.Discount));
            upd.Parameters.AddWithValue("$juros", ProductPriceHelper.RoundPrice(input.Interest));
            upd.Parameters.AddWithValue("$multa", ProductPriceHelper.RoundPrice(input.Multa));
            upd.Parameters.AddWithValue("$notes",
                string.IsNullOrWhiteSpace(input.Notes) ? DBNull.Value : input.Notes.Trim());
            upd.Parameters.AddWithValue("$conta",
                string.IsNullOrWhiteSpace(input.FinancialAccount) ? DBNull.Value : input.FinancialAccount.Trim());
            upd.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(input.PaymentType) ? inst.PaymentType : input.PaymentType.Trim());
            upd.ExecuteNonQuery();
        }

        var paymentType = string.IsNullOrWhiteSpace(input.PaymentType) ? inst.PaymentType : input.PaymentType.Trim();
        CashService.RegisterPayableCashPayment(conn, tx, installmentId, inst.SupplierName,
            ProductPriceHelper.RoundPrice(input.PaidAmount), paidIso, paymentType);

        tx.Commit();
    }

    public static void ReversePayment(int installmentId)
    {
        if (StoreNetworkMode.IsClient)
        {
            StoreNetworkClient.ReversePayablePayment(installmentId);
            return;
        }
        ReversePaymentLocal(installmentId);
    }

    public static void ReversePaymentLocal(int installmentId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("estornar parcela a pagar");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var inst = GetInstallment(conn, tx, installmentId)
            ?? throw new PayableException("Parcela não encontrada.");
        if (inst.Status != "pago")
            throw new PayableException("Esta parcela não está paga — nada a estornar.");

        CashService.RemovePayableCashPayment(conn, tx, installmentId);

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = """
            UPDATE payable_installments SET
                status = 'pendente',
                paid_amount = 0,
                paid_date = NULL,
                discount = 0,
                interest = 0,
                multa = 0
            WHERE id = $id;
            """;
        upd.Parameters.AddWithValue("$id", installmentId);
        upd.ExecuteNonQuery();
        tx.Commit();
    }

    public static void UpdateInstallment(int installmentId, PayableInstallmentUpdateInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("editar parcela local");
        if (string.IsNullOrEmpty(DateBrHelper.ToIso(input.DueDate)))
            throw new PayableException("Informe o vencimento (DD/MM/AAAA).");
        if (input.Amount < 0)
            throw new PayableException("Informe o valor da parcela.");

        var due = DateBrHelper.ToIso(input.DueDate)!;

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var inst = GetInstallment(conn, tx, installmentId)
            ?? throw new PayableException("Parcela não encontrada.");
        if (inst.Status == "pago")
            throw new PayableException("Parcela já paga. Estorne a baixa (F7) antes de alterar.");

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE payable_installments SET
                    due_date = $due,
                    amount = $amount,
                    discount = $disc,
                    interest = $juros,
                    payment_type = $type
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$id", installmentId);
            upd.Parameters.AddWithValue("$due", due);
            upd.Parameters.AddWithValue("$amount", ProductPriceHelper.RoundPrice(input.Amount));
            upd.Parameters.AddWithValue("$disc", ProductPriceHelper.RoundPrice(input.Discount));
            upd.Parameters.AddWithValue("$juros", ProductPriceHelper.RoundPrice(input.Interest));
            upd.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(input.PaymentType) ? "Boleto" : input.PaymentType.Trim());
            upd.ExecuteNonQuery();
        }

        RecalcTitleTotal(conn, tx, inst.TitleId);
        tx.Commit();
    }

    public static void DeleteTitle(int titleId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("excluir título a pagar");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT 1 FROM payable_titles WHERE id = $id LIMIT 1;";
            check.Parameters.AddWithValue("$id", titleId);
            if (check.ExecuteScalar() is null)
                throw new PayableException("Título não encontrado.");
        }

        EnsureNoPaidInstallments(conn, tx, titleId);

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM payable_titles WHERE id = $id;";
        del.Parameters.AddWithValue("$id", titleId);
        del.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// Remove títulos a pagar da compra (somente se nenhuma parcela estiver paga).
    /// Usado ao cancelar/reabrir compra fechada.
    /// </summary>
    public static void ThrowIfPaidInstallmentsForPurchase(
        SqliteConnection conn, SqliteTransaction tx, int purchaseId)
    {
        using var paid = conn.CreateCommand();
        paid.Transaction = tx;
        paid.CommandText = """
            SELECT 1
            FROM payable_installments pi
            JOIN payable_titles t ON t.id = pi.title_id
            WHERE t.purchase_id = $id AND lower(IFNULL(pi.status,'')) = 'pago'
            LIMIT 1;
            """;
        paid.Parameters.AddWithValue("$id", purchaseId);
        if (paid.ExecuteScalar() is not null)
            throw new PayableException(
                "Há parcela paga vinculada a esta compra. Estorne a baixa no Contas a Pagar (F7) antes de reabrir/cancelar.");
    }

    public static void RemoveUnpaidTitlesForPurchase(
        SqliteConnection conn, SqliteTransaction tx, int purchaseId)
    {
        ThrowIfPaidInstallmentsForPurchase(conn, tx, purchaseId);

        // Bancos legados: FK das parcelas é NO ACTION (sem CASCADE).
        // Precisa apagar parcelas antes dos títulos.
        using (var delInst = conn.CreateCommand())
        {
            delInst.Transaction = tx;
            delInst.CommandText = """
                DELETE FROM payable_installments
                WHERE title_id IN (
                    SELECT id FROM payable_titles WHERE purchase_id = $id
                );
                """;
            delInst.Parameters.AddWithValue("$id", purchaseId);
            delInst.ExecuteNonQuery();
        }

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM payable_titles WHERE purchase_id = $id;";
        del.Parameters.AddWithValue("$id", purchaseId);
        del.ExecuteNonQuery();
    }

    public static void DeleteInstallment(int installmentId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("excluir parcela a pagar");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var inst = GetInstallment(conn, tx, installmentId)
            ?? throw new PayableException("Parcela não encontrada.");
        if (inst.Status == "pago")
            throw new PayableException("Parcela já paga. Estorne a baixa (F7) antes de excluir.");

        var titleId = inst.TitleId;
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM payable_installments WHERE id = $id;";
            del.Parameters.AddWithValue("$id", installmentId);
            del.ExecuteNonQuery();
        }

        using var count = conn.CreateCommand();
        count.Transaction = tx;
        count.CommandText = "SELECT COUNT(*) FROM payable_installments WHERE title_id = $id;";
        count.Parameters.AddWithValue("$id", titleId);
        var remaining = Convert.ToInt32(count.ExecuteScalar());
        if (remaining == 0)
        {
            using var delTitle = conn.CreateCommand();
            delTitle.Transaction = tx;
            delTitle.CommandText = "DELETE FROM payable_titles WHERE id = $id;";
            delTitle.Parameters.AddWithValue("$id", titleId);
            delTitle.ExecuteNonQuery();
        }
        else
        {
            RecalcTitleTotal(conn, tx, titleId);
        }

        tx.Commit();
    }

    private static void EnsureNoPaidInstallments(SqliteConnection conn, SqliteTransaction tx, int titleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM payable_installments WHERE title_id = $id AND lower(status) = 'pago' LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", titleId);
        if (cmd.ExecuteScalar() is not null)
            throw new PayableException("Há parcela paga neste título. Estorne a baixa (F7) antes de excluir.");
    }

    private static void RecalcTitleTotal(SqliteConnection conn, SqliteTransaction tx, int titleId)
    {
        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = """
            UPDATE payable_titles SET total_amount = (
                SELECT COALESCE(SUM(amount), 0) FROM payable_installments WHERE title_id = $id
            ) WHERE id = $id;
            """;
        upd.Parameters.AddWithValue("$id", titleId);
        upd.ExecuteNonQuery();
    }

    private static List<PayableInstallmentDetail> LoadInstallmentsForTitle(SqliteConnection conn, int titleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT pi.id, pi.title_id, pi.seq, pi.due_date, pi.amount, pi.discount, pi.interest,
                   pi.paid_amount, pi.paid_date, pi.payment_type, pi.status,
                   p.name, t.number, t.purchase_id
            FROM payable_installments pi
            JOIN payable_titles t ON t.id = pi.title_id
            JOIN people p ON p.id = t.supplier_id
            WHERE pi.title_id = $id
            ORDER BY pi.seq ASC;
            """;
        cmd.Parameters.AddWithValue("$id", titleId);
        var today = DateTime.Today;
        var list = new List<PayableInstallmentDetail>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var status = reader.GetString(10);
            var due = reader.GetString(3);
            list.Add(new PayableInstallmentDetail
            {
                Id = reader.GetInt32(0),
                TitleId = reader.GetInt32(1),
                Seq = reader.GetInt32(2),
                DueDate = due,
                Amount = reader.GetDouble(4),
                Discount = reader.GetDouble(5),
                Interest = reader.GetDouble(6),
                PaidAmount = reader.GetDouble(7),
                PaidDate = reader.IsDBNull(8) ? null : reader.GetString(8),
                PaymentType = reader.GetString(9),
                Status = status,
                SupplierName = reader.IsDBNull(11) ? "" : reader.GetString(11),
                Number = reader.GetString(12),
                PurchaseId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                Situacao = InstallmentSituacao(status, due, today),
            });
        }
        return list;
    }

    private static PayableInstallmentDetail? GetInstallment(
        SqliteConnection conn, SqliteTransaction? tx, int installmentId)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT pi.id, pi.title_id, pi.seq, pi.due_date, pi.amount, pi.discount, pi.interest,
                   pi.paid_amount, pi.paid_date, pi.payment_type, pi.status,
                   p.name, t.number, t.purchase_id, IFNULL(pi.multa, 0), pi.notes, pi.financial_account
            FROM payable_installments pi
            JOIN payable_titles t ON t.id = pi.title_id
            JOIN people p ON p.id = t.supplier_id
            WHERE pi.id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", installmentId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var status = reader.GetString(10);
        var due = reader.GetString(3);
        return new PayableInstallmentDetail
        {
            Id = reader.GetInt32(0),
            TitleId = reader.GetInt32(1),
            Seq = reader.GetInt32(2),
            DueDate = due,
            Amount = reader.GetDouble(4),
            Discount = reader.GetDouble(5),
            Interest = reader.GetDouble(6),
            PaidAmount = reader.GetDouble(7),
            PaidDate = reader.IsDBNull(8) ? null : reader.GetString(8),
            PaymentType = reader.GetString(9),
            Status = status,
            SupplierName = reader.IsDBNull(11) ? "" : reader.GetString(11),
            Number = reader.GetString(12),
            PurchaseId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
            Multa = reader.GetDouble(14),
            Notes = reader.IsDBNull(15) ? null : reader.GetString(15),
            FinancialAccount = reader.IsDBNull(16) ? null : reader.GetString(16),
            Situacao = InstallmentSituacao(status, due, DateTime.Today),
        };
    }

    /// <summary>
    /// Normaliza status legado (PAGO/PENDENTE) para minúsculas usadas pelo nativo.
    /// </summary>
    public static int NormalizeLegacyStatuses()
    {
        if (StoreNetworkMode.IsClient)
            return 0;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE payable_installments
            SET status = lower(status)
            WHERE status IS NOT NULL
              AND status != lower(status);
            """;
        return cmd.ExecuteNonQuery();
    }

    private static string InstallmentSituacao(string status, string dueIso, DateTime today)
    {
        if (IsPaidStatus(status))
            return "pago";
        if (DateTime.TryParse(dueIso, out var due) && due.Date < today)
            return "vencido";
        return "pendente";
    }

    /// <summary>Gestão antigo gravava PAGO/PENDENTE em maiúsculas.</summary>
    private static bool IsPaidStatus(string? status) =>
        string.Equals((status ?? "").Trim(), "pago", StringComparison.OrdinalIgnoreCase);

    private static string TitleSituacao(IReadOnlyList<PayableInstallmentDetail> insts, DateTime today)
    {
        if (insts.Count == 0)
            return "pendente";
        var sits = insts.Select(i => InstallmentSituacao(i.Status, i.DueDate, today)).ToHashSet();
        if (sits.Count == 1 && sits.Contains("pago"))
            return "pago";
        if (sits.Contains("vencido"))
            return "vencido";
        return "pendente";
    }

    private static string? NormalizeCategory(string? value)
    {
        var s = (value ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return null;

        try
        {
            foreach (var cat in ExpenseCategoriesService.ListActiveNames())
            {
                if (string.Equals(cat, s, StringComparison.OrdinalIgnoreCase))
                    return cat;
            }
        }
        catch
        {
            // banco ainda migrando — mantém texto informado
        }

        return s.Length > 80 ? s[..80] : s;
    }
}

public class PayableException : Exception
{
    public PayableException(string message) : base(message) { }
}
