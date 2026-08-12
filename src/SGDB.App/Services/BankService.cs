using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public sealed class BankException : Exception
{
    public BankException(string message) : base(message) { }
}

public static class BankService
{
    public static readonly string[] CommonOperators =
    [
        "Stone", "PagBank", "Mercado Pago", "Cielo", "Rede", "Getnet", "PicPay", "Outro",
    ];

    public static IReadOnlyList<BankAccountRow> ListAccounts(bool onlyActive = true)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = onlyActive
            ? """
                SELECT id, name, IFNULL(bank_name,''), IFNULL(agency,''), IFNULL(account_number,''),
                       account_type, IFNULL(pix_key,''), opening_balance, active, IFNULL(notes,''),
                       IFNULL(default_operator,'')
                FROM bank_accounts WHERE active = 1 ORDER BY name;
                """
            : """
                SELECT id, name, IFNULL(bank_name,''), IFNULL(agency,''), IFNULL(account_number,''),
                       account_type, IFNULL(pix_key,''), opening_balance, active, IFNULL(notes,''),
                       IFNULL(default_operator,'')
                FROM bank_accounts ORDER BY active DESC, name;
                """;

        var raw = new List<(int Id, string Name, string Bank, string Agency, string Acc, string Type, string Pix, double Opening, bool Active, string Notes, string Op)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                raw.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetDouble(7),
                    reader.GetInt32(8) != 0,
                    reader.GetString(9),
                    reader.GetString(10)));
            }
        }

        return raw.Select(r => new BankAccountRow
        {
            Id = r.Id,
            Name = r.Name,
            BankName = r.Bank,
            Agency = r.Agency,
            AccountNumber = r.Acc,
            AccountType = r.Type,
            PixKey = r.Pix,
            OpeningBalance = r.Opening,
            Active = r.Active,
            Notes = r.Notes,
            DefaultOperator = r.Op,
            Balance = CalcBalance(conn, r.Id, r.Opening),
        }).ToList();
    }

    public static BankAccountRow GetAccount(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, IFNULL(bank_name,''), IFNULL(agency,''), IFNULL(account_number,''),
                   account_type, IFNULL(pix_key,''), opening_balance, active, IFNULL(notes,''),
                   IFNULL(default_operator,'')
            FROM bank_accounts WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new BankException("Conta não encontrada.");
        var opening = reader.GetDouble(7);
        return new BankAccountRow
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            BankName = reader.GetString(2),
            Agency = reader.GetString(3),
            AccountNumber = reader.GetString(4),
            AccountType = reader.GetString(5),
            PixKey = reader.GetString(6),
            OpeningBalance = opening,
            Active = reader.GetInt32(8) != 0,
            Notes = reader.GetString(9),
            DefaultOperator = reader.GetString(10),
            Balance = CalcBalance(conn, id, opening),
        };
    }

    public static int SaveAccount(
        int? id,
        string name,
        string? bankName,
        string? agency,
        string? accountNumber,
        string accountType,
        string? pixKey,
        double openingBalance,
        bool active,
        string? notes,
        string? defaultOperator = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("contas bancárias");
        name = (name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            throw new BankException("Informe o nome da conta.");

        accountType = (accountType ?? "corrente").Trim().ToLowerInvariant();
        if (accountType is not ("corrente" or "poupanca" or "aplicacao"))
            accountType = "corrente";

        using var conn = DatabaseService.OpenConnection();
        if (id is > 0)
        {
            using var upd = conn.CreateCommand();
            upd.CommandText = """
                UPDATE bank_accounts SET
                    name = $name, bank_name = $bank, agency = $agency, account_number = $acc,
                    account_type = $type, pix_key = $pix, opening_balance = $open,
                    active = $active, notes = $notes, default_operator = $op
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$id", id.Value);
            BindAccount(upd, name, bankName, agency, accountNumber, accountType, pixKey, openingBalance, active, notes, defaultOperator);
            upd.ExecuteNonQuery();
            return id.Value;
        }

        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO bank_accounts
                (name, bank_name, agency, account_number, account_type, pix_key, opening_balance, active, notes, default_operator)
            VALUES ($name, $bank, $agency, $acc, $type, $pix, $open, $active, $notes, $op);
            SELECT last_insert_rowid();
            """;
        BindAccount(ins, name, bankName, agency, accountNumber, accountType, pixKey, openingBalance, active, notes, defaultOperator);
        return Convert.ToInt32(ins.ExecuteScalar());
    }

    private static void BindAccount(
        SqliteCommand cmd, string name, string? bankName, string? agency, string? accountNumber,
        string accountType, string? pixKey, double openingBalance, bool active, string? notes, string? defaultOperator)
    {
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$bank", (object?)bankName?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$agency", (object?)agency?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$acc", (object?)accountNumber?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$type", accountType);
        cmd.Parameters.AddWithValue("$pix", (object?)pixKey?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$open", ProductPriceHelper.RoundPrice(openingBalance));
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$notes", (object?)notes?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$op", (object?)defaultOperator?.Trim() ?? DBNull.Value);
    }

    public static BankMovementsResult ListMovements(
        int accountId,
        DateTime? from = null,
        DateTime? to = null,
        string status = "todas",
        string? paymentType = null,
        string? operatorName = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT id, account_id, movement_date, posted_date, kind, description,
                   IFNULL(party_name,''), IFNULL(payment_type,''),
                   amount_in, amount_out, fee_amount, reconciliation_status,
                   notes, ref_type, ref_id, IFNULL(operator_name,''), external_id
            FROM bank_movements
            WHERE account_id = $acc
            """;
        cmd.Parameters.AddWithValue("$acc", accountId);

        if (from is DateTime df)
        {
            sql += " AND movement_date >= $from";
            cmd.Parameters.AddWithValue("$from", df.ToString("yyyy-MM-dd"));
        }
        if (to is DateTime dt)
        {
            sql += " AND movement_date <= $to";
            cmd.Parameters.AddWithValue("$to", dt.ToString("yyyy-MM-dd"));
        }
        if (status is "pendente" or "conferido" or "divergente")
        {
            sql += " AND reconciliation_status = $st";
            cmd.Parameters.AddWithValue("$st", status);
        }
        if (!string.IsNullOrWhiteSpace(paymentType) &&
            !paymentType.Equals("Todas", StringComparison.OrdinalIgnoreCase) &&
            !paymentType.Equals("Todas as formas", StringComparison.OrdinalIgnoreCase))
        {
            sql += " AND IFNULL(payment_type,'') = $pay";
            cmd.Parameters.AddWithValue("$pay", paymentType.Trim());
        }
        if (!string.IsNullOrWhiteSpace(operatorName) &&
            !operatorName.Equals("Todas", StringComparison.OrdinalIgnoreCase) &&
            !operatorName.Equals("Todas operadoras", StringComparison.OrdinalIgnoreCase))
        {
            sql += " AND IFNULL(operator_name,'') = $op";
            cmd.Parameters.AddWithValue("$op", operatorName.Trim());
        }

        sql += " ORDER BY movement_date DESC, id DESC LIMIT 2000;";
        cmd.CommandText = sql;

        var rows = new List<BankMovementRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new BankMovementRow
            {
                Id = reader.GetInt32(0),
                AccountId = reader.GetInt32(1),
                MovementDate = reader.GetString(2),
                PostedDate = reader.IsDBNull(3) ? null : reader.GetString(3),
                Kind = reader.GetString(4),
                Description = reader.GetString(5),
                PartyName = reader.GetString(6),
                PaymentType = reader.GetString(7),
                AmountIn = reader.GetDouble(8),
                AmountOut = reader.GetDouble(9),
                FeeAmount = reader.GetDouble(10),
                ReconciliationStatus = reader.GetString(11),
                Notes = reader.IsDBNull(12) ? null : reader.GetString(12),
                RefType = reader.IsDBNull(13) ? null : reader.GetString(13),
                RefId = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                OperatorName = reader.GetString(15),
                ExternalId = reader.IsDBNull(16) ? null : reader.GetString(16),
            });
        }

        var totalIn = ProductPriceHelper.RoundPrice(rows.Sum(r => r.AmountIn));
        var totalOut = ProductPriceHelper.RoundPrice(rows.Sum(r => r.AmountOut));
        var totalFees = ProductPriceHelper.RoundPrice(rows.Sum(r => r.FeeAmount));
        return new BankMovementsResult
        {
            Rows = rows,
            TotalIn = totalIn,
            TotalOut = totalOut,
            TotalFees = totalFees,
            PeriodBalance = ProductPriceHelper.RoundPrice(totalIn - totalOut),
            Pendentes = rows.Count(r => r.ReconciliationStatus == "pendente"),
            Conferidos = rows.Count(r => r.ReconciliationStatus == "conferido"),
        };
    }

    public static IReadOnlyList<string> ListPaymentTypesUsed(int accountId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT TRIM(payment_type) FROM bank_movements
            WHERE account_id = $acc AND IFNULL(TRIM(payment_type),'') <> ''
            ORDER BY 1;
            """;
        cmd.Parameters.AddWithValue("$acc", accountId);
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public static IReadOnlyList<string> ListOperatorsUsed(int accountId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT TRIM(operator_name) FROM bank_movements
            WHERE account_id = $acc AND IFNULL(TRIM(operator_name),'') <> ''
            ORDER BY 1;
            """;
        cmd.Parameters.AddWithValue("$acc", accountId);
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public static int AddMovement(
        int accountId,
        DateTime movementDate,
        string kind,
        string description,
        double amountIn,
        double amountOut,
        double feeAmount = 0,
        string? paymentType = null,
        string? partyName = null,
        string? notes = null,
        string? refType = null,
        int? refId = null,
        string status = "pendente",
        string? operatorName = null,
        string? externalId = null,
        DateTime? postedDate = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("contas bancárias");
        if (accountId <= 0)
            throw new BankException("Selecione a conta bancária.");
        kind = (kind ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(kind))
            throw new BankException("Informe o tipo do lançamento.");
        amountIn = Math.Max(0, ProductPriceHelper.RoundPrice(amountIn));
        amountOut = Math.Max(0, ProductPriceHelper.RoundPrice(amountOut));
        feeAmount = Math.Max(0, ProductPriceHelper.RoundPrice(feeAmount));
        if (amountIn < 0.009 && amountOut < 0.009)
            throw new BankException("Informe um valor de entrada ou saída.");

        using var conn = DatabaseService.OpenConnection();
        EnsureAccountExists(conn, accountId);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO bank_movements
                (account_id, movement_date, posted_date, kind, description, party_name, payment_type,
                 amount_in, amount_out, fee_amount, reconciliation_status, ref_type, ref_id, notes,
                 operator_name, external_id)
            VALUES
                ($acc, $date, $posted, $kind, $desc, $party, $pay,
                 $in, $out, $fee, $st, $rtype, $rid, $notes,
                 $op, $ext);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$acc", accountId);
        cmd.Parameters.AddWithValue("$date", movementDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$posted",
            postedDate is DateTime pd ? pd.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$desc", (description ?? "").Trim());
        cmd.Parameters.AddWithValue("$party", (object?)partyName?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pay", (object?)paymentType?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$in", amountIn);
        cmd.Parameters.AddWithValue("$out", amountOut);
        cmd.Parameters.AddWithValue("$fee", feeAmount);
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$rtype", (object?)refType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rid", (object?)refId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)notes?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$op", (object?)operatorName?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ext", (object?)externalId?.Trim() ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void SetReconciliation(int movementId, string status, DateTime? postedDate = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("contas bancárias");
        status = (status ?? "").Trim().ToLowerInvariant();
        if (status is not ("pendente" or "conferido" or "divergente"))
            throw new BankException("Status de conciliação inválido.");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE bank_movements SET
                reconciliation_status = $st,
                posted_date = COALESCE($posted, posted_date),
                reconciled_at = CASE WHEN $st = 'conferido' THEN datetime('now','localtime') ELSE NULL END
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", movementId);
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$posted",
            postedDate is DateTime d ? d.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        if (cmd.ExecuteNonQuery() == 0)
            throw new BankException("Lançamento não encontrado.");
    }

    /// <summary>Marca todos os pendentes do filtro atual como conferidos.</summary>
    public static int ConferirTodos(
        int accountId,
        DateTime? from,
        DateTime? to,
        string? paymentType = null,
        string? operatorName = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("contas bancárias");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            UPDATE bank_movements SET
                reconciliation_status = 'conferido',
                posted_date = COALESCE(posted_date, movement_date),
                reconciled_at = datetime('now','localtime')
            WHERE account_id = $acc AND reconciliation_status = 'pendente'
            """;
        cmd.Parameters.AddWithValue("$acc", accountId);
        if (from is DateTime df)
        {
            sql += " AND movement_date >= $from";
            cmd.Parameters.AddWithValue("$from", df.ToString("yyyy-MM-dd"));
        }
        if (to is DateTime dt)
        {
            sql += " AND movement_date <= $to";
            cmd.Parameters.AddWithValue("$to", dt.ToString("yyyy-MM-dd"));
        }
        if (!string.IsNullOrWhiteSpace(paymentType) &&
            !paymentType.Equals("Todas", StringComparison.OrdinalIgnoreCase) &&
            !paymentType.Equals("Todas as formas", StringComparison.OrdinalIgnoreCase))
        {
            sql += " AND IFNULL(payment_type,'') = $pay";
            cmd.Parameters.AddWithValue("$pay", paymentType.Trim());
        }
        if (!string.IsNullOrWhiteSpace(operatorName) &&
            !operatorName.Equals("Todas", StringComparison.OrdinalIgnoreCase) &&
            !operatorName.Equals("Todas operadoras", StringComparison.OrdinalIgnoreCase))
        {
            sql += " AND IFNULL(operator_name,'') = $op";
            cmd.Parameters.AddWithValue("$op", operatorName.Trim());
        }
        cmd.CommandText = sql + ";";
        return cmd.ExecuteNonQuery();
    }

    public static void DeleteMovement(int movementId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("contas bancárias");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM bank_movements WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", movementId);
        if (cmd.ExecuteNonQuery() == 0)
            throw new BankException("Lançamento não encontrado.");
    }

    /// <summary>
    /// Gera créditos previstos (Pix/Débito/Crédito) a partir das vendas do período,
    /// usando taxa e prazo de Formas de Pagamento. Não duplica se já existir ref.
    /// </summary>
    public static int ImportExpectedFromSales(int accountId, DateTime from, DateTime to)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("contas bancárias");
        if (accountId <= 0)
            throw new BankException("Selecione a conta.");
        if (from > to)
            (from, to) = (to, from);

        var feeMap = PaymentMethodsService.FeeInfoByApiLabel();

        using var conn = DatabaseService.OpenConnection();
        EnsureAccountExists(conn, accountId);
        var opDefault = GetAccountOperator(conn, accountId);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cm.id, cm.ref_id, IFNULL(cm.payment_type,''), IFNULL(cm.amount_in,0),
                   IFNULL(s.session_date, cm.movement_date), IFNULL(cm.description,'')
            FROM cash_movements cm
            LEFT JOIN sales s ON s.id = cm.ref_id AND cm.ref_type = 'sale'
            WHERE cm.kind = 'venda'
              AND cm.amount_in > 0.009
              AND IFNULL(s.cancelled, 0) = 0
              AND cm.movement_date >= $from
              AND cm.movement_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));

        var candidates = new List<(int MovId, int? SaleId, string Pay, double Amt, string Date, string Desc)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var pay = reader.GetString(2);
                var forma = NormalizeForma(pay);
                if (forma is "Dinheiro" or "Fiado" or "—")
                    continue;
                candidates.Add((
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    forma,
                    reader.GetDouble(3),
                    reader.GetString(4),
                    reader.GetString(5)));
            }
        }

        var inserted = 0;
        foreach (var c in candidates)
        {
            if (ExistsRef(conn, "cash_movement", c.MovId))
                continue;

            feeMap.TryGetValue(c.Pay, out var feeInfo);
            // Se a forma tem conta de destino, só importa quando for a conta selecionada.
            if (feeInfo?.BankAccountId is int destId && destId != accountId)
                continue;

            var feePct = feeInfo?.FeePercent ?? 0;
            var feeFixed = feeInfo?.FeeFixed ?? 0;
            var days = feeInfo?.SettlementDays ?? 0;
            var fee = PaymentMethodsService.CalcFeeAmount(c.Amt, feePct, feeFixed);
            var liquido = ProductPriceHelper.RoundPrice(c.Amt - fee);
            var baseDate = DateTime.TryParse(c.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var bd)
                ? bd.Date : from;
            var creditDate = baseDate.AddDays(Math.Max(0, days));

            using var ins = conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO bank_movements
                    (account_id, movement_date, kind, description, payment_type,
                     amount_in, amount_out, fee_amount, reconciliation_status, ref_type, ref_id, notes,
                     operator_name)
                VALUES
                    ($acc, $date, 'prevista', $desc, $pay,
                     $in, 0, $fee, 'pendente', 'cash_movement', $rid, $notes,
                     $op);
                """;
            ins.Parameters.AddWithValue("$acc", accountId);
            ins.Parameters.AddWithValue("$date", creditDate.ToString("yyyy-MM-dd"));
            ins.Parameters.AddWithValue("$desc",
                string.IsNullOrWhiteSpace(c.Desc)
                    ? $"Crédito previsto — {c.Pay}" + (c.SaleId is int sid ? $" · Venda #{sid}" : "")
                    : c.Desc);
            ins.Parameters.AddWithValue("$pay", c.Pay);
            ins.Parameters.AddWithValue("$in", liquido);
            ins.Parameters.AddWithValue("$fee", fee);
            ins.Parameters.AddWithValue("$rid", c.MovId);
            var feeNote = fee > 0.009
                ? (feeFixed > 0.009
                    ? $"Bruto R$ {c.Amt:N2} − ({feePct:N2}% + R$ {feeFixed:N2}) = R$ {liquido:N2} · D+{days}"
                    : $"Bruto R$ {c.Amt:N2} − taxa {feePct:N2}% = R$ {liquido:N2} · D+{days}")
                : $"D+{days}";
            ins.Parameters.AddWithValue("$notes", feeNote);
            ins.Parameters.AddWithValue("$op",
                string.IsNullOrWhiteSpace(opDefault) ? (object)DBNull.Value : opDefault);
            ins.ExecuteNonQuery();
            inserted++;
        }

        return inserted;
    }

    /// <summary>
    /// Importa extrato OFX: cruza créditos/débitos com lançamentos pendentes (valor ± R$ 0,05 e data ± 3 dias)
    /// e cria os que não existirem. Usa FITID para não duplicar.
    /// </summary>
    public static OfxImportResult ImportOfx(int accountId, string filePath)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("contas bancárias");
        if (accountId <= 0)
            throw new BankException("Selecione a conta.");
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new BankException("Arquivo OFX não encontrado.");

        var txns = OfxParser.ParseFile(filePath);
        if (txns.Count == 0)
            throw new BankException("Nenhuma transação encontrada no arquivo OFX.");

        using var conn = DatabaseService.OpenConnection();
        EnsureAccountExists(conn, accountId);
        var opDefault = GetAccountOperator(conn, accountId);

        var matched = 0;
        var created = 0;
        var skipped = 0;

        // Pendentes candidatos (janela ampla)
        var pending = LoadPendingForMatch(conn, accountId);
        var usedIds = new HashSet<int>();

        foreach (var t in txns)
        {
            if (ExistsExternal(conn, accountId, t.FitId))
            {
                skipped++;
                continue;
            }

            var isCredit = t.Amount > 0.009;
            var isDebit = t.Amount < -0.009;
            if (!isCredit && !isDebit)
            {
                skipped++;
                continue;
            }

            var abs = Math.Abs(t.Amount);
            var match = pending.FirstOrDefault(p =>
                !usedIds.Contains(p.Id) &&
                Math.Abs((isCredit ? p.AmountIn : p.AmountOut) - abs) <= 0.05 &&
                Math.Abs((ParseIso(p.MovementDate) - t.PostedDate).TotalDays) <= 3 &&
                (isCredit ? p.AmountIn > 0.009 : p.AmountOut > 0.009));

            if (match is not null)
            {
                usedIds.Add(match.Id);
                using var upd = conn.CreateCommand();
                upd.CommandText = """
                    UPDATE bank_movements SET
                        reconciliation_status = 'conferido',
                        posted_date = $posted,
                        reconciled_at = datetime('now','localtime'),
                        external_id = $ext,
                        notes = CASE
                            WHEN IFNULL(notes,'') = '' THEN $memo
                            ELSE notes || ' · OFX: ' || $memo
                        END
                    WHERE id = $id;
                    """;
                upd.Parameters.AddWithValue("$id", match.Id);
                upd.Parameters.AddWithValue("$posted", t.PostedDate.ToString("yyyy-MM-dd"));
                upd.Parameters.AddWithValue("$ext", t.FitId);
                upd.Parameters.AddWithValue("$memo",
                    string.IsNullOrWhiteSpace(t.Memo) ? "bateu com extrato" : t.Memo);
                upd.ExecuteNonQuery();
                matched++;
                continue;
            }

            // Cria lançamento já conferido
            var desc = string.IsNullOrWhiteSpace(t.Memo)
                ? (isCredit ? "Crédito do extrato OFX" : "Débito do extrato OFX")
                : t.Memo;
            using var ins = conn.CreateCommand();
            ins.CommandText = """
                INSERT INTO bank_movements
                    (account_id, movement_date, posted_date, kind, description, payment_type,
                     amount_in, amount_out, fee_amount, reconciliation_status, notes,
                     operator_name, external_id, reconciled_at)
                VALUES
                    ($acc, $date, $date, $kind, $desc, $pay,
                     $in, $out, 0, 'conferido', 'Importado do OFX',
                     $op, $ext, datetime('now','localtime'));
                """;
            ins.Parameters.AddWithValue("$acc", accountId);
            ins.Parameters.AddWithValue("$date", t.PostedDate.ToString("yyyy-MM-dd"));
            ins.Parameters.AddWithValue("$kind", isCredit ? "credito" : "debito");
            ins.Parameters.AddWithValue("$desc", desc);
            ins.Parameters.AddWithValue("$pay", GuessPaymentFromMemo(t.Memo));
            ins.Parameters.AddWithValue("$in", isCredit ? ProductPriceHelper.RoundPrice(abs) : 0);
            ins.Parameters.AddWithValue("$out", isDebit ? ProductPriceHelper.RoundPrice(abs) : 0);
            ins.Parameters.AddWithValue("$op",
                string.IsNullOrWhiteSpace(opDefault) ? (object)DBNull.Value : opDefault);
            ins.Parameters.AddWithValue("$ext", t.FitId);
            ins.ExecuteNonQuery();
            created++;
        }

        return new OfxImportResult
        {
            TotalInFile = txns.Count,
            Matched = matched,
            Created = created,
            Skipped = skipped,
        };
    }

    private static List<BankMovementRow> LoadPendingForMatch(SqliteConnection conn, int accountId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, account_id, movement_date, posted_date, kind, description,
                   IFNULL(party_name,''), IFNULL(payment_type,''),
                   amount_in, amount_out, fee_amount, reconciliation_status,
                   notes, ref_type, ref_id, IFNULL(operator_name,''), external_id
            FROM bank_movements
            WHERE account_id = $acc AND reconciliation_status = 'pendente'
            ORDER BY movement_date, id;
            """;
        cmd.Parameters.AddWithValue("$acc", accountId);
        var rows = new List<BankMovementRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new BankMovementRow
            {
                Id = reader.GetInt32(0),
                AccountId = reader.GetInt32(1),
                MovementDate = reader.GetString(2),
                AmountIn = reader.GetDouble(8),
                AmountOut = reader.GetDouble(9),
                ReconciliationStatus = reader.GetString(11),
            });
        }
        return rows;
    }

    private static bool ExistsExternal(SqliteConnection conn, int accountId, string fitId)
    {
        if (string.IsNullOrWhiteSpace(fitId)) return false;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM bank_movements
            WHERE account_id = $acc AND external_id = $ext LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$acc", accountId);
        cmd.Parameters.AddWithValue("$ext", fitId);
        return cmd.ExecuteScalar() is not null;
    }

    private static string? GetAccountOperator(SqliteConnection conn, int accountId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(default_operator,'') FROM bank_accounts WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", accountId);
        var v = cmd.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static string GuessPaymentFromMemo(string memo)
    {
        var m = (memo ?? "").ToLowerInvariant();
        if (m.Contains("pix")) return "Pix";
        if (m.Contains("debito") || m.Contains("débito") || m.Contains("debit")) return "Cartão Débito";
        if (m.Contains("credito") || m.Contains("crédito") || m.Contains("credit")) return "Cartão Crédito";
        return "";
    }

    private static DateTime ParseIso(string iso)
    {
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.Date;
        if (DateTime.TryParse(iso, out d))
            return d.Date;
        return DateTime.Today;
    }

    private static bool ExistsRef(SqliteConnection conn, string refType, int refId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM bank_movements
            WHERE ref_type = $t AND ref_id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$t", refType);
        cmd.Parameters.AddWithValue("$id", refId);
        return cmd.ExecuteScalar() is not null;
    }

    private static void EnsureAccountExists(SqliteConnection conn, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM bank_accounts WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteScalar() is null)
            throw new BankException("Conta bancária não encontrada.");
    }

    private static double CalcBalance(SqliteConnection conn, int accountId, double opening)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(amount_in),0), COALESCE(SUM(amount_out),0)
            FROM bank_movements WHERE account_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", accountId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return ProductPriceHelper.RoundPrice(opening);
        var inn = reader.GetDouble(0);
        var outt = reader.GetDouble(1);
        return ProductPriceHelper.RoundPrice(opening + inn - outt);
    }

    private static string NormalizeForma(string? paymentType)
    {
        var s = (paymentType ?? "").Trim();
        if (string.IsNullOrEmpty(s)) return "—";
        var low = s.ToLowerInvariant();
        if (low is "dinheiro" or "cash" or "din") return "Dinheiro";
        if (low is "pix") return "Pix";
        if (low.Contains("debito") || low.Contains("débito")) return "Cartão Débito";
        if (low.Contains("credito") || low.Contains("crédito")) return "Cartão Crédito";
        if (low.Contains("fiado")) return "Fiado";
        return s;
    }
}
