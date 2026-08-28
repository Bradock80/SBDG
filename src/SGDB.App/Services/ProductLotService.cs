using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Estoque por lote/validade (FEFO). A quantidade em products.stock continua sendo o total;
/// product_lots guarda o detalhe por vencimento para alertas e baixa FEFO.
/// </summary>
public static class ProductLotService
{
    public static void Receive(ProductLotReceiveInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("lotes/validade");
        if (input.ProductId <= 0 || input.Quantity <= 0.0001)
            return;

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        Receive(conn, tx, input);
        tx.Commit();
    }

    /// <summary>
    /// Recebe quantidade no lote (merge por produto + lote + validade).
    /// Retorna o id da linha em product_lots; 0 se não houve recebimento.
    /// </summary>
    public static int Receive(
        SqliteConnection conn, SqliteTransaction tx, ProductLotReceiveInput input)
    {
        if (input.ProductId <= 0 || input.Quantity <= 0.0001)
            return 0;

        var lot = (input.LotNumber ?? "").Trim();
        var expiry = input.ExpiryDate?.Date.ToString("yyyy-MM-dd");

        // Soma no mesmo lote/validade se já existir (mesmo produto + lote + validade).
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = """
                SELECT id, quantity FROM product_lots
                WHERE product_id = $pid
                  AND IFNULL(lot_number,'') = $lot
                  AND IFNULL(expiry_date,'') = IFNULL($exp,'')
                LIMIT 1;
                """;
            find.Parameters.AddWithValue("$pid", input.ProductId);
            find.Parameters.AddWithValue("$lot", lot);
            find.Parameters.AddWithValue("$exp", (object?)expiry ?? DBNull.Value);
            using var reader = find.ExecuteReader();
            if (reader.Read())
            {
                var id = reader.GetInt32(0);
                var qty = reader.GetDouble(1) + input.Quantity;
                reader.Close();
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = """
                    UPDATE product_lots
                    SET quantity = $qty,
                        unit_cost = CASE WHEN $cost > 0 THEN $cost ELSE unit_cost END,
                        purchase_id = COALESCE($purchase, purchase_id),
                        notes = COALESCE($notes, notes)
                    WHERE id = $id;
                    """;
                upd.Parameters.AddWithValue("$qty", Math.Round(qty, 4));
                upd.Parameters.AddWithValue("$cost", input.UnitCost);
                upd.Parameters.AddWithValue("$purchase", input.PurchaseId is int p ? p : DBNull.Value);
                upd.Parameters.AddWithValue("$notes", (object?)input.Notes ?? DBNull.Value);
                upd.Parameters.AddWithValue("$id", id);
                upd.ExecuteNonQuery();
                return id;
            }
        }

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO product_lots (
              product_id, lot_number, expiry_date, quantity, purchase_id, unit_cost, notes, created_at
            ) VALUES (
              $pid, $lot, $exp, $qty, $purchase, $cost, $notes, datetime('now','localtime')
            );
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$pid", input.ProductId);
        ins.Parameters.AddWithValue("$lot", lot);
        ins.Parameters.AddWithValue("$exp", (object?)expiry ?? DBNull.Value);
        ins.Parameters.AddWithValue("$qty", Math.Round(input.Quantity, 4));
        ins.Parameters.AddWithValue("$purchase", input.PurchaseId is int pid ? pid : DBNull.Value);
        ins.Parameters.AddWithValue("$cost", input.UnitCost);
        ins.Parameters.AddWithValue("$notes", (object?)input.Notes ?? DBNull.Value);
        return Convert.ToInt32(ins.ExecuteScalar());
    }

    /// <summary>
    /// Baixa FEFO em product_lots.
    /// <paramref name="skipExpired"/>=true (venda 70I): nunca consome expiry_date &lt; hoje;
    /// ordem = válidos datados ASC, depois sem validade, id ASC.
    /// Default false preserva callers legados/testes que ainda usam a ordem histórica
    /// (validade mais próxima, inclusive vencida).
    /// </summary>
    public static void DeductFefo(
        SqliteConnection conn, SqliteTransaction tx, int productId, double qty,
        bool skipExpired = false)
    {
        qty = Math.Round(Math.Abs(qty), 4);
        if (productId <= 0 || qty < 0.0001)
            return;

        var todayIso = DateTime.Today.ToString("yyyy-MM-dd");
        var lots = new List<(int Id, double Qty)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            if (skipExpired)
            {
                // Venda normal 70I: ignora comprovadamente vencidos.
                cmd.CommandText = """
                    SELECT id, quantity FROM product_lots
                    WHERE product_id = $pid
                      AND quantity > 0.0001
                      AND (
                        expiry_date IS NULL OR TRIM(expiry_date) = ''
                        OR expiry_date >= $today
                      )
                    ORDER BY
                      CASE WHEN expiry_date IS NULL OR TRIM(expiry_date)='' THEN 1 ELSE 0 END,
                      expiry_date ASC,
                      id ASC;
                    """;
                cmd.Parameters.AddWithValue("$today", todayIso);
            }
            else
            {
                cmd.CommandText = """
                    SELECT id, quantity FROM product_lots
                    WHERE product_id = $pid AND quantity > 0.0001
                    ORDER BY
                      CASE WHEN expiry_date IS NULL OR TRIM(expiry_date)='' THEN 1 ELSE 0 END,
                      expiry_date ASC,
                      id ASC;
                    """;
            }
            cmd.Parameters.AddWithValue("$pid", productId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                lots.Add((reader.GetInt32(0), reader.GetDouble(1)));
        }

        var remaining = qty;
        foreach (var (id, lotQty) in lots)
        {
            if (remaining < 0.0001) break;
            var take = Math.Min(lotQty, remaining);
            var left = Math.Round(lotQty - take, 4);
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            if (left < 0.0001)
            {
                upd.CommandText = "DELETE FROM product_lots WHERE id = $id;";
                upd.Parameters.AddWithValue("$id", id);
            }
            else
            {
                upd.CommandText = "UPDATE product_lots SET quantity = $qty WHERE id = $id;";
                upd.Parameters.AddWithValue("$qty", left);
                upd.Parameters.AddWithValue("$id", id);
            }
            upd.ExecuteNonQuery();
            remaining = Math.Round(remaining - take, 4);
        }
        // Se ainda sobrar (estoque legado sem lote), não cria lote negativo — só baixa o stock.
    }

    /// <summary>70I — FEFO de venda: nunca consome lote comprovadamente vencido.</summary>
    public static void DeductFefoForSale(
        SqliteConnection conn, SqliteTransaction tx, int productId, double qty)
        => DeductFefo(conn, tx, productId, qty, skipExpired: true);

    /// <summary>
    /// Baixa quantidade exata de um lote físico. Não usa FEFO e não recorre a outro lote.
    /// Se productLotId não existir, tenta a chave produto+lote+validade somente quando houver
    /// exatamente uma linha (lote agregado sem ambiguidade).
    /// </summary>
    public static void DeductExact(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        int? productLotId,
        string? lotNumber,
        DateTime? expiryDate,
        double qty)
    {
        qty = Math.Round(Math.Abs(qty), 4);
        if (productId <= 0 || qty < 0.0001)
            return;

        var resolvedId = ResolveExactLotId(conn, tx, productId, productLotId, lotNumber, expiryDate);
        double available;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT quantity FROM product_lots
                WHERE id = $id AND product_id = $pid
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", resolvedId);
            cmd.Parameters.AddWithValue("$pid", productId);
            var o = cmd.ExecuteScalar();
            if (o is null or DBNull)
                throw new InvalidOperationException(
                    "Não é possível cancelar: o lote originado por esta compra não foi encontrado. Não é seguro adivinhar outro lote.");
            available = Convert.ToDouble(o);
        }

        if (available + 1e-4 < qty)
        {
            var lot = string.IsNullOrWhiteSpace(lotNumber) ? "(sem número)" : lotNumber.Trim();
            throw new InvalidOperationException(
                $"Não é possível cancelar: parte do estoque desta compra já foi vendida ou movimentada (lote {lot}: disponível {available:0.####}, origem {qty:0.####}).");
        }

        var left = Math.Round(available - qty, 4);
        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        if (left < 0.0001)
        {
            upd.CommandText = "DELETE FROM product_lots WHERE id = $id;";
            upd.Parameters.AddWithValue("$id", resolvedId);
        }
        else
        {
            upd.CommandText = "UPDATE product_lots SET quantity = $qty WHERE id = $id;";
            upd.Parameters.AddWithValue("$qty", left);
            upd.Parameters.AddWithValue("$id", resolvedId);
        }
        upd.ExecuteNonQuery();
    }

    public static int ResolveExactLotId(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        int? productLotId,
        string? lotNumber,
        DateTime? expiryDate)
    {
        if (productLotId is int id && id > 0)
        {
            using var byId = conn.CreateCommand();
            byId.Transaction = tx;
            byId.CommandText = "SELECT product_id FROM product_lots WHERE id = $id LIMIT 1;";
            byId.Parameters.AddWithValue("$id", id);
            var o = byId.ExecuteScalar();
            if (o is not null and not DBNull)
            {
                if (Convert.ToInt32(o) != productId)
                    throw new InvalidOperationException(
                        "Não é possível cancelar: o lote registrado não pertence ao produto da compra.");
                return id;
            }
        }

        var lot = (lotNumber ?? "").Trim();
        var expiry = expiryDate?.Date.ToString("yyyy-MM-dd");
        var matches = new List<int>();
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = """
                SELECT id FROM product_lots
                WHERE product_id = $pid
                  AND IFNULL(lot_number,'') = $lot
                  AND IFNULL(expiry_date,'') = IFNULL($exp,'')
                """;
            find.Parameters.AddWithValue("$pid", productId);
            find.Parameters.AddWithValue("$lot", lot);
            find.Parameters.AddWithValue("$exp", (object?)expiry ?? DBNull.Value);
            using var reader = find.ExecuteReader();
            while (reader.Read())
                matches.Add(reader.GetInt32(0));
        }

        if (matches.Count == 1)
            return matches[0];

        throw new InvalidOperationException(
            "Não é possível cancelar: o lote originado por esta compra não foi encontrado com segurança. Não é seguro adivinhar outro lote.");
    }

    /// <summary>Devolve quantidade ao lote de validade mais próxima (ou cria lote sem número).</summary>
    public static void RestoreToNearestLot(
        SqliteConnection conn, SqliteTransaction tx, int productId, double qty)
    {
        qty = Math.Round(Math.Abs(qty), 4);
        if (productId <= 0 || qty < 0.0001)
            return;

        int? targetId = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id FROM product_lots
                WHERE product_id = $pid
                ORDER BY
                  CASE WHEN expiry_date IS NULL OR TRIM(expiry_date)='' THEN 1 ELSE 0 END,
                  expiry_date ASC,
                  id ASC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$pid", productId);
            var o = cmd.ExecuteScalar();
            if (o is not null and not DBNull)
                targetId = Convert.ToInt32(o);
        }

        if (targetId is int id)
        {
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE product_lots SET quantity = quantity + $qty WHERE id = $id;";
            upd.Parameters.AddWithValue("$qty", qty);
            upd.Parameters.AddWithValue("$id", id);
            upd.ExecuteNonQuery();
            return;
        }

        Receive(conn, tx, new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = qty,
            LotNumber = "",
            Notes = "Devolução sem lote cadastrado",
        });
    }

    public static IReadOnlyList<ProductLot> ListExpiring(int withinDays, int limit = 500)
    {
        var lim = Math.Clamp(limit, 1, 2000);
        var days = Math.Clamp(withinDays, 1, 365);
        var today = DateTime.Today;
        var to = today.AddDays(days).ToString("yyyy-MM-dd");
        var from = today.ToString("yyyy-MM-dd");

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT l.id, l.product_id, IFNULL(p.code,''), IFNULL(p.name,''), IFNULL(p.unit,'UN'),
                   IFNULL(l.lot_number,''), l.expiry_date, l.quantity, l.purchase_id,
                   IFNULL(l.unit_cost,0), IFNULL(l.created_at,''), l.notes
            FROM product_lots l
            JOIN products p ON p.id = l.product_id
            WHERE l.quantity > 0.0001
              AND l.expiry_date IS NOT NULL AND TRIM(l.expiry_date) <> ''
              AND l.expiry_date <= $to
            ORDER BY l.expiry_date ASC, p.name ASC
            LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$to", to);
        cmd.Parameters.AddWithValue("$lim", lim);

        var rows = new List<ProductLot>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(ReadLot(reader));
        }
        return rows;
    }

    public static int CountExpiring(int withinDays)
    {
        var days = Math.Clamp(withinDays, 1, 365);
        var to = DateTime.Today.AddDays(days).ToString("yyyy-MM-dd");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM product_lots
            WHERE quantity > 0.0001
              AND expiry_date IS NOT NULL AND TRIM(expiry_date) <> ''
              AND expiry_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$to", to);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public static IReadOnlyList<ProductLot> ListByProduct(int productId, int limit = 100)
    {
        if (productId <= 0)
            return [];
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListProductLots(productId);
        return ListByProductLocal(productId, limit);
    }

    public static IReadOnlyList<ProductLot> ListByProductLocal(int productId, int limit = 100)
    {
        if (productId <= 0)
            return [];
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT l.id, l.product_id, IFNULL(p.code,''), IFNULL(p.name,''), IFNULL(p.unit,'UN'),
                   IFNULL(l.lot_number,''), l.expiry_date, l.quantity, l.purchase_id,
                   IFNULL(l.unit_cost,0), IFNULL(l.created_at,''), l.notes
            FROM product_lots l
            JOIN products p ON p.id = l.product_id
            WHERE l.product_id = $pid AND l.quantity > 0.0001
            ORDER BY
              CASE WHEN l.expiry_date IS NULL OR TRIM(l.expiry_date)='' THEN 1 ELSE 0 END,
              l.expiry_date ASC, l.id ASC
            LIMIT $lim;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$lim", Math.Clamp(limit, 1, 500));
        var rows = new List<ProductLot>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add(ReadLot(reader));
        return rows;
    }

    private static ProductLot ReadLot(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        ProductId = reader.GetInt32(1),
        ProductCode = reader.IsDBNull(2) ? "" : reader.GetString(2),
        ProductName = reader.IsDBNull(3) ? "" : reader.GetString(3),
        Unit = reader.IsDBNull(4) ? "UN" : reader.GetString(4),
        LotNumber = reader.IsDBNull(5) ? "" : reader.GetString(5),
        ExpiryDateIso = reader.IsDBNull(6) ? null : reader.GetString(6),
        Quantity = reader.GetDouble(7),
        PurchaseId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
        UnitCost = reader.GetDouble(9),
        CreatedAt = reader.IsDBNull(10) ? "" : reader.GetString(10),
        Notes = reader.IsDBNull(11) ? null : reader.GetString(11),
    };
}
