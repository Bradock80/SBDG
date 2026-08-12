using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public sealed class VasilhameException : Exception
{
    public VasilhameException(string message) : base(message) { }
}

public static class VasilhameService
{
    public static VasilhameListResult List(
        bool somenteDevedor = true,
        bool somenteVencido = false,
        string? search = null,
        DateTime? movimentosFrom = null,
        DateTime? movimentosTo = null)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListVasilhame(somenteDevedor, somenteVencido, search, movimentosFrom, movimentosTo);
        return ListLocal(somenteDevedor, somenteVencido, search, movimentosFrom, movimentosTo);
    }

    public static VasilhameListResult ListLocal(
        bool somenteDevedor = true,
        bool somenteVencido = false,
        string? search = null,
        DateTime? movimentosFrom = null,
        DateTime? movimentosTo = null)
    {
        using var conn = DatabaseService.OpenConnection();
        var movimentos = LoadMovements(conn, search, movimentosFrom, movimentosTo, limit: 300);
        var saldos = AggregateSaldosFromDb(conn, search);

        if (somenteDevedor)
            saldos = saldos.Where(s => s.Balance > 0.009).ToList();
        if (somenteVencido)
            saldos = saldos.Where(s => s.IsOverdue).ToList();

        var resumo = saldos
            .Where(s => s.Balance > 0.009)
            .GroupBy(s => s.ContainerTypeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new VasilhameTypeSummary
            {
                TypeName = g.Key,
                Quantity = ProductPriceHelper.RoundPrice(g.Sum(x => x.Balance)),
            })
            .OrderByDescending(x => x.Quantity)
            .ToList();

        return new VasilhameListResult
        {
            Saldos = saldos,
            Movimentos = movimentos,
            ResumoPorTipo = resumo,
            Registros = saldos.Count,
            TotalItens = ProductPriceHelper.RoundPrice(saldos.Sum(s => s.Balance)),
            Vencidos = saldos.Count(s => s.IsOverdue),
            TotalCaucao = ProductPriceHelper.RoundPrice(saldos.Sum(s => s.CautionTotal)),
        };
    }

    public static int RegistrarSaida(
        int containerTypeId,
        double quantity,
        int? customerId,
        string? borrowerName,
        string? borrowerPhone,
        DateTime? dueDate,
        string? notes)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.CreateVasilhameMovement(
                "saida", containerTypeId, quantity, customerId, borrowerName, borrowerPhone, dueDate, notes);
        return InsertMovement("saida", containerTypeId, quantity, customerId, borrowerName, borrowerPhone, dueDate, notes);
    }

    public static int RegistrarDevolucao(
        int containerTypeId,
        double quantity,
        int? customerId,
        string? borrowerName,
        string? borrowerPhone,
        string? notes)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.CreateVasilhameMovement(
                "entrada", containerTypeId, quantity, customerId, borrowerName, borrowerPhone, null, notes);
        return InsertMovement("entrada", containerTypeId, quantity, customerId, borrowerName, borrowerPhone, null, notes);
    }

    public static void DeleteMovement(int id)
    {
        if (StoreNetworkMode.IsClient)
        {
            StoreNetworkClient.DeleteVasilhameMovement(id);
            return;
        }
        DeleteMovementLocal(id);
    }

    public static void DeleteMovementLocal(int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("excluir movimento de vasilhame");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM vasilhame_movements WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new VasilhameException("Lançamento não encontrado.");
    }

    public static void OpenWhatsAppCobrança(VasilhameSaldoRow saldo)
    {
        var digits = VasilhameSaldoRow.DigitsOnly(saldo.BorrowerPhone);
        if (digits.Length < 10)
            throw new VasilhameException("Telefone inválido ou ausente para WhatsApp.");

        if (digits.Length == 10 || digits.Length == 11)
            digits = "55" + digits;

        var desde = !string.IsNullOrEmpty(saldo.FirstLoanDisplay) && saldo.FirstLoanDisplay != "—"
            ? saldo.FirstLoanDisplay
            : saldo.DueDateDisplay;
        var msg =
            $"Olá {saldo.BorrowerName}, vimos que você está com {saldo.BalanceDisplay} {saldo.ContainerTypeName} em aberto desde {desde}. Qual a previsão de devolução?";
        var url = "https://wa.me/" + digits + "?text=" + Uri.EscapeDataString(msg);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void PrintComprovanteSaldo(VasilhameSaldoRow saldo)
    {
        var lines = new List<string>
        {
            "COMPROVANTE DE VASILHAME",
            DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture),
            "",
            "Cliente: " + saldo.BorrowerName,
            string.IsNullOrWhiteSpace(saldo.BorrowerPhone) ? "" : "Tel: " + saldo.BorrowerPhone,
            "Item: " + saldo.ContainerTypeName,
            "Qtd em aberto: " + saldo.BalanceDisplay,
            "Status: " + saldo.StatusBadge,
            "Desde: " + saldo.FirstLoanDisplay,
            "Vencimento: " + saldo.DueDateDisplay,
        };
        if (saldo.CautionTotal > 0.009)
            lines.Add("Caução (R$): " + ProductPriceHelper.FormatBr(saldo.CautionTotal));
        lines.Add("");
        lines.Add("Assinatura do cliente:");
        lines.Add("");
        lines.Add("______________________________");
        PeripheralService.PrintReceiptLines(lines.Where(l => l is not null)!);
    }

    public static void PrintComprovanteMovimento(VasilhameMovementRow mov)
    {
        var lines = new List<string>
        {
            mov.IsLoan ? "EMPRESTIMO DE VASILHAME" : "DEVOLUCAO DE VASILHAME",
            mov.CreatedDisplay,
            "",
            "Cliente: " + mov.BorrowerName,
            string.IsNullOrWhiteSpace(mov.BorrowerPhone) ? "" : "Tel: " + mov.BorrowerPhone,
            "Item: " + mov.ContainerTypeName,
            "Quantidade: " + mov.QtyDisplay,
            "Tipo: " + mov.KindDisplay,
        };
        if (!string.IsNullOrWhiteSpace(mov.DueDateDisplay) && mov.DueDateDisplay != "—")
            lines.Add("Vencimento: " + mov.DueDateDisplay);
        if (!string.IsNullOrWhiteSpace(mov.Notes))
            lines.Add("Obs: " + mov.Notes);
        lines.Add("");
        lines.Add("Assinatura do cliente:");
        lines.Add("");
        lines.Add("______________________________");
        PeripheralService.PrintReceiptLines(lines.Where(l => l is not null)!);
    }

    private static int InsertMovement(
        string kind,
        int containerTypeId,
        double quantity,
        int? customerId,
        string? borrowerName,
        string? borrowerPhone,
        DateTime? dueDate,
        string? notes) =>
        InsertMovementLocal(kind, containerTypeId, quantity, customerId, borrowerName, borrowerPhone, dueDate, notes);

    public static int InsertMovementLocal(
        string kind,
        int containerTypeId,
        double quantity,
        int? customerId,
        string? borrowerName,
        string? borrowerPhone,
        DateTime? dueDate,
        string? notes)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("movimento de vasilhame");
        if (containerTypeId <= 0)
            throw new VasilhameException("Selecione o tipo de vasilhame.");
        quantity = Math.Abs(ProductPriceHelper.RoundPrice(quantity));
        if (quantity < 0.009)
            throw new VasilhameException("Informe a quantidade.");

        var name = (borrowerName ?? "").Trim();
        if (customerId is null or <= 0 && string.IsNullOrEmpty(name))
            throw new VasilhameException("Informe o nome de quem pegou (não precisa ser cliente cadastrado).");

        using var conn = DatabaseService.OpenConnection();

        string? resolvedName = name;
        string? resolvedPhone = borrowerPhone?.Trim();
        if (customerId is > 0)
        {
            using var p = conn.CreateCommand();
            p.CommandText = """
                SELECT name,
                       COALESCE(NULLIF(TRIM(phone), ''), NULLIF(TRIM(cell1), ''), NULLIF(TRIM(whatsapp), ''), '')
                FROM people WHERE id = $id LIMIT 1;
                """;
            p.Parameters.AddWithValue("$id", customerId.Value);
            using var r = p.ExecuteReader();
            if (!r.Read())
                throw new VasilhameException("Cliente não encontrado.");
            if (string.IsNullOrEmpty(resolvedName))
                resolvedName = r.GetString(0);
            if (string.IsNullOrEmpty(resolvedPhone) && !r.IsDBNull(1))
                resolvedPhone = r.GetString(1);
        }

        double unitPrice = 0;
        using (var priceCmd = conn.CreateCommand())
        {
            priceCmd.CommandText = "SELECT IFNULL(sale_price, 0) FROM container_types WHERE id = $id LIMIT 1;";
            priceCmd.Parameters.AddWithValue("$id", containerTypeId);
            unitPrice = Convert.ToDouble(priceCmd.ExecuteScalar() ?? 0);
        }

        // Banco legado (Gestão): created_at NOT NULL sem DEFAULT — sempre preencher.
        var createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO vasilhame_movements
                (customer_id, borrower_name, borrower_phone, container_type_id, kind,
                 quantity, unit_price, due_date, notes, created_at)
            VALUES
                ($cid, $name, $phone, $tid, $kind, $qty, $price, $due, $notes, $created);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$cid", (object?)customerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", (object?)resolvedName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$phone", (object?)resolvedPhone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tid", containerTypeId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$qty", quantity);
        cmd.Parameters.AddWithValue("$price", unitPrice);
        cmd.Parameters.AddWithValue("$due", dueDate is DateTime d ? d.ToString("yyyy-MM-dd") : DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)notes?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", createdAt);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static List<VasilhameSaldoRow> AggregateSaldosFromDb(SqliteConnection conn, string? search)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(m.customer_id, 0),
                   COALESCE(NULLIF(TRIM(m.borrower_name), ''), pe.name, 'Sem nome'),
                   COALESCE(NULLIF(TRIM(m.borrower_phone), ''), ''),
                   m.container_type_id,
                   IFNULL(ct.name, '—'),
                   IFNULL(ct.sale_price, 0),
                   SUM(CASE
                         WHEN m.kind IN ('saida','emprestimo') THEN m.quantity
                         WHEN m.kind IN ('entrada','devolucao') THEN -m.quantity
                         ELSE 0 END) AS bal,
                   SUM(CASE WHEN m.kind IN ('saida','emprestimo') THEN m.quantity ELSE 0 END) AS loaned,
                   SUM(CASE WHEN m.kind IN ('entrada','devolucao') THEN m.quantity ELSE 0 END) AS returned,
                   MIN(CASE WHEN m.kind IN ('saida','emprestimo') THEN m.due_date END) AS due_min,
                   MIN(CASE WHEN m.kind IN ('saida','emprestimo') THEN date(m.created_at) END) AS first_loan
            FROM vasilhame_movements m
            LEFT JOIN people pe ON pe.id = m.customer_id
            LEFT JOIN container_types ct ON ct.id = m.container_type_id
            WHERE 1=1 
            """;

        if (!string.IsNullOrWhiteSpace(search))
        {
            cmd.CommandText += """
                 AND (
                    UPPER(IFNULL(m.borrower_name,'')) LIKE $like
                    OR UPPER(IFNULL(pe.name,'')) LIKE $like
                    OR UPPER(IFNULL(ct.name,'')) LIKE $like
                    OR IFNULL(m.borrower_phone,'') LIKE $like
                 ) 
                """;
            cmd.Parameters.AddWithValue("$like", $"%{search.Trim().ToUpperInvariant()}%");
        }

        cmd.CommandText += """
             GROUP BY IFNULL(m.customer_id, 0),
                     COALESCE(NULLIF(TRIM(m.borrower_name), ''), pe.name, 'Sem nome'),
                     m.container_type_id
            HAVING ABS(bal) > 0.009
            ORDER BY bal DESC, 2 ASC;
            """;

        var today = DateTime.Today;
        var list = new List<VasilhameSaldoRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var cid = reader.GetInt32(0);
            var bal = reader.GetDouble(6);
            var loaned = reader.GetDouble(7);
            var returned = reader.GetDouble(8);
            var due = reader.IsDBNull(9) ? null : reader.GetString(9);
            var firstLoan = reader.IsDBNull(10) ? null : reader.GetString(10);
            var overdue = false;
            if (!string.IsNullOrEmpty(due) && DateTime.TryParse(due, out var dd))
                overdue = bal > 0.009 && dd.Date < today;

            list.Add(new VasilhameSaldoRow
            {
                CustomerId = cid > 0 ? cid : null,
                BorrowerName = reader.GetString(1),
                BorrowerPhone = reader.GetString(2),
                ContainerTypeId = reader.GetInt32(3),
                ContainerTypeName = reader.GetString(4),
                UnitCautionPrice = reader.GetDouble(5),
                Balance = ProductPriceHelper.RoundPrice(bal),
                TotalLoaned = ProductPriceHelper.RoundPrice(loaned),
                TotalReturned = ProductPriceHelper.RoundPrice(returned),
                DueDate = due,
                FirstLoanDate = firstLoan,
                IsOverdue = overdue,
            });
        }
        return list;
    }

    private static List<VasilhameMovementRow> LoadMovements(
        SqliteConnection conn,
        string? search,
        DateTime? from,
        DateTime? to,
        int limit)
    {
        using var cmd = conn.CreateCommand();
        var sql = new StringBuilder("""
            SELECT m.id, m.customer_id, IFNULL(m.borrower_name,''), IFNULL(m.borrower_phone,''),
                   m.container_type_id, IFNULL(ct.name,''), m.kind, m.quantity,
                   IFNULL(m.unit_price, 0), m.due_date, m.notes, m.created_at,
                   IFNULL(pe.name,'')
            FROM vasilhame_movements m
            LEFT JOIN container_types ct ON ct.id = m.container_type_id
            LEFT JOIN people pe ON pe.id = m.customer_id
            WHERE 1=1 
            """);

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql.Append("""
                 AND (
                    UPPER(IFNULL(m.borrower_name,'')) LIKE $like
                    OR UPPER(IFNULL(pe.name,'')) LIKE $like
                    OR UPPER(IFNULL(ct.name,'')) LIKE $like
                    OR IFNULL(m.borrower_phone,'') LIKE $like
                 ) 
                """);
            cmd.Parameters.AddWithValue("$like", $"%{search.Trim().ToUpperInvariant()}%");
        }

        if (from is DateTime df)
        {
            sql.Append(" AND date(m.created_at) >= $from ");
            cmd.Parameters.AddWithValue("$from", df.ToString("yyyy-MM-dd"));
        }

        if (to is DateTime dt)
        {
            sql.Append(" AND date(m.created_at) <= $to ");
            cmd.Parameters.AddWithValue("$to", dt.ToString("yyyy-MM-dd"));
        }

        sql.Append(" ORDER BY m.created_at DESC, m.id DESC LIMIT $lim;");
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$lim", limit);

        var list = new List<VasilhameMovementRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var borrower = reader.GetString(2);
            if (string.IsNullOrWhiteSpace(borrower) && !reader.IsDBNull(12))
                borrower = reader.GetString(12);

            list.Add(new VasilhameMovementRow
            {
                Id = reader.GetInt32(0),
                CustomerId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                BorrowerName = borrower,
                BorrowerPhone = reader.GetString(3),
                ContainerTypeId = reader.GetInt32(4),
                ContainerTypeName = reader.GetString(5),
                Kind = reader.GetString(6),
                Quantity = reader.GetDouble(7),
                UnitPrice = reader.GetDouble(8),
                DueDate = reader.IsDBNull(9) ? null : reader.GetString(9),
                Notes = reader.IsDBNull(10) ? null : reader.GetString(10),
                CreatedAt = reader.IsDBNull(11) ? "" : reader.GetString(11),
            });
        }
        return list;
    }
}
