using Microsoft.Data.Sqlite;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69T-B — múltiplos códigos por produto (unidade, embalagem, alias de merge).
/// </summary>
public static class ProductBarcodeService
{
    public sealed record Entry(
        string Barcode,
        string Kind,
        double PackFactor,
        string Source);

    public static Product? FindActiveProductByBarcode(string? barcode)
    {
        var digits = TextNorm.NormalizeBarcode(barcode);
        if (digits is null)
            return null;

        using var conn = DatabaseService.OpenConnection();
        return FindActiveProductByBarcode(conn, null, digits);
    }

    public static Product? FindActiveProductByBarcode(
        SqliteConnection conn, SqliteTransaction? tx, string digits)
    {
        foreach (var cand in CandidateDigits(digits))
        {
            using var cmd = conn.CreateCommand();
            if (tx is not null)
                cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT p.id, p.code, p.barcode, p.name, p.group_name, p.unit, p.cost_price, p.sale_price,
                       p.min_stock, p.stock, p.location, p.extra_json, p.active, p.created_at,
                       IFNULL(p.stock_fridge, 0), IFNULL(p.stock_fridge_min, 0),
                       IFNULL(pb.kind, 'ALIAS')
                FROM product_barcodes pb
                INNER JOIN products p ON p.id = pb.product_id
                WHERE pb.active = 1 AND p.active = 1 AND pb.barcode = $b
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$b", cand);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                continue;
            return ReadProduct(reader);
        }
        return null;
    }

    public static string? FindKind(SqliteConnection conn, SqliteTransaction? tx, int productId, string? barcode)
    {
        var digits = TextNorm.NormalizeBarcode(barcode);
        if (digits is null || productId <= 0)
            return null;
        foreach (var cand in CandidateDigits(digits))
        {
            using var cmd = conn.CreateCommand();
            if (tx is not null)
                cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT kind FROM product_barcodes
                WHERE product_id = $p AND active = 1 AND barcode = $b
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$p", productId);
            cmd.Parameters.AddWithValue("$b", cand);
            var kind = cmd.ExecuteScalar() as string;
            if (!string.IsNullOrWhiteSpace(kind))
                return kind;
        }
        return null;
    }

    public static List<string> ListActiveBarcodes(int productId)
    {
        var list = new List<string>();
        if (productId <= 0)
            return list;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT barcode FROM product_barcodes
            WHERE product_id = $p AND active = 1
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    /// <summary>
    /// Bloqueia se o barcode estiver em outro produto ativo (coluna, embalagem ou alias).
    /// </summary>
    public static void ThrowIfBarcodeOwnedByOther(
        SqliteConnection conn,
        SqliteTransaction tx,
        string? barcode,
        int keepId,
        int absorbId)
    {
        var digits = TextNorm.NormalizeBarcode(barcode);
        if (digits is null)
            return;

        foreach (var cand in CandidateDigits(digits))
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT id FROM products
                    WHERE active = 1 AND barcode = $b AND id <> $keep AND id <> $absorb
                    LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$b", cand);
                cmd.Parameters.AddWithValue("$keep", keepId);
                cmd.Parameters.AddWithValue("$absorb", absorbId);
                if (cmd.ExecuteScalar() is not null)
                    throw new InvalidOperationException(
                        $"O código de barras {cand} já está em outro produto. Ajuste antes de unificar.");
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT p.id FROM product_barcodes pb
                    INNER JOIN products p ON p.id = pb.product_id
                    WHERE pb.active = 1 AND p.active = 1 AND pb.barcode = $b
                      AND p.id <> $keep AND p.id <> $absorb
                    LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$b", cand);
                cmd.Parameters.AddWithValue("$keep", keepId);
                cmd.Parameters.AddWithValue("$absorb", absorbId);
                if (cmd.ExecuteScalar() is not null)
                    throw new InvalidOperationException(
                        $"O código de barras {cand} já está em outro produto (alias). Ajuste antes de unificar.");
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT id, IFNULL(extra_json,'') FROM products
                    WHERE active = 1 AND id <> $keep AND id <> $absorb
                      AND IFNULL(extra_json,'') LIKE $like
                    LIMIT 40;
                    """;
                cmd.Parameters.AddWithValue("$keep", keepId);
                cmd.Parameters.AddWithValue("$absorb", absorbId);
                cmd.Parameters.AddWithValue("$like", "%\"barcode_embalagem\":\"" + cand + "%");
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var pack = ProductExtra.Parse(reader.IsDBNull(1) ? "" : reader.GetString(1)).BarcodeEmbalagem;
                    if (TextNorm.NormalizeBarcode(pack) == cand)
                        throw new InvalidOperationException(
                            $"O código de barras {cand} já é embalagem de outro produto. Ajuste antes de unificar.");
                }
            }
        }
    }

    public static void Upsert(
        SqliteConnection conn,
        SqliteTransaction? tx,
        int productId,
        string? barcode,
        string kind,
        double packFactor,
        string source)
    {
        var digits = TextNorm.NormalizeBarcode(barcode);
        if (digits is null || productId <= 0)
            return;

        using var cmd = conn.CreateCommand();
        if (tx is not null)
            cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO product_barcodes (product_id, barcode, kind, pack_factor, active, created_at, source)
            VALUES ($p, $b, $k, $f, 1, datetime('now','localtime'), $s)
            ON CONFLICT(barcode) DO UPDATE SET
                product_id = excluded.product_id,
                kind = excluded.kind,
                pack_factor = excluded.pack_factor,
                active = 1,
                source = excluded.source;
            """;
        // SQLite needs unique on barcode for ON CONFLICT — we use unique index on barcode when active.
        // Partial unique indexes don't work with ON CONFLICT in older SQLite the same way.
        // Use explicit upsert pattern instead.
        cmd.CommandText = """
            UPDATE product_barcodes
            SET product_id = $p, kind = $k, pack_factor = $f, active = 1, source = $s
            WHERE barcode = $b;
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        cmd.Parameters.AddWithValue("$b", digits);
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$f", packFactor);
        cmd.Parameters.AddWithValue("$s", source);
        var updated = cmd.ExecuteNonQuery();
        if (updated > 0)
            return;

        using var ins = conn.CreateCommand();
        if (tx is not null)
            ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO product_barcodes (product_id, barcode, kind, pack_factor, active, created_at, source)
            VALUES ($p, $b, $k, $f, 1, datetime('now','localtime'), $s);
            """;
        ins.Parameters.AddWithValue("$p", productId);
        ins.Parameters.AddWithValue("$b", digits);
        ins.Parameters.AddWithValue("$k", kind);
        ins.Parameters.AddWithValue("$f", packFactor);
        ins.Parameters.AddWithValue("$s", source);
        try
        {
            ins.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Concorrência rara: tenta update de novo.
            using var retry = conn.CreateCommand();
            if (tx is not null)
                retry.Transaction = tx;
            retry.CommandText = """
                UPDATE product_barcodes
                SET product_id = $p, kind = $k, pack_factor = $f, active = 1, source = $s
                WHERE barcode = $b;
                """;
            retry.Parameters.AddWithValue("$p", productId);
            retry.Parameters.AddWithValue("$b", digits);
            retry.Parameters.AddWithValue("$k", kind);
            retry.Parameters.AddWithValue("$f", packFactor);
            retry.Parameters.AddWithValue("$s", source);
            retry.ExecuteNonQuery();
        }
    }

    public static List<Entry> CollectProductCodes(
        string? unitBarcode,
        ProductExtra extra,
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId)
    {
        var result = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? bc, string kind, double factor, string source)
        {
            var d = TextNorm.NormalizeBarcode(bc);
            if (d is null || !seen.Add(d))
                return;
            result.Add(new Entry(d, kind, factor, source));
        }

        Add(unitBarcode, ProductBarcodeKinds.Unit, 1, "products.barcode");
        Add(extra.BarcodeEmbalagem, ProductBarcodeKinds.Pack,
            extra.FatorEmbalagem >= 2 ? extra.FatorEmbalagem
                : (extra.QtdAtacado >= 2 ? extra.QtdAtacado : 1),
            "extra.barcode_embalagem");

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT barcode, kind, IFNULL(pack_factor,1), IFNULL(source,'')
            FROM product_barcodes
            WHERE product_id = $p AND active = 1;
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Add(reader.GetString(0),
                reader.IsDBNull(1) ? ProductBarcodeKinds.Alias : reader.GetString(1),
                reader.IsDBNull(2) ? 1 : reader.GetDouble(2),
                reader.IsDBNull(3) ? "product_barcodes" : reader.GetString(3));
        }

        return result;
    }

    /// <summary>
    /// Move códigos do absorb para o keep. UNIT do absorb → ALIAS se keep já tem UNIT.
    /// PACK do absorb → PACK se keep sem pack, senão ALIAS com pack_factor.
    /// </summary>
    public static List<Entry> TransferAbsorbCodesToKeep(
        SqliteConnection conn,
        SqliteTransaction tx,
        int keepId,
        int absorbId,
        string? keepUnitBarcode,
        ProductExtra keepExtra,
        string? absorbUnitBarcode,
        ProductExtra absorbExtra)
    {
        var moved = new List<Entry>();
        var keepUnit = TextNorm.NormalizeBarcode(keepUnitBarcode);
        var keepPack = TextNorm.NormalizeBarcode(keepExtra.BarcodeEmbalagem);
        var absorbCodes = CollectProductCodes(absorbUnitBarcode, absorbExtra, conn, tx, absorbId);

        foreach (var entry in absorbCodes)
        {
            ThrowIfBarcodeOwnedByOther(conn, tx, entry.Barcode, keepId, absorbId);

            // Já é o barcode principal/pack do keep — só garante linha no keep.
            if (entry.Barcode == keepUnit)
            {
                Upsert(conn, tx, keepId, entry.Barcode, ProductBarcodeKinds.Unit, 1, "merge_keep_unit");
                continue;
            }
            if (keepPack is not null && entry.Barcode == keepPack)
            {
                Upsert(conn, tx, keepId, entry.Barcode, ProductBarcodeKinds.Pack,
                    keepExtra.FatorEmbalagem >= 2 ? keepExtra.FatorEmbalagem : entry.PackFactor,
                    "merge_keep_pack");
                continue;
            }

            var kind = entry.Kind;
            var factor = entry.PackFactor;
            if (kind == ProductBarcodeKinds.Unit)
            {
                // Unidade do absorb → alias do keep (keep mantém o UNIT principal).
                kind = ProductBarcodeKinds.Alias;
                factor = 1;
            }
            else if (kind == ProductBarcodeKinds.Pack)
            {
                // Mantém PACK mesmo se keep já tem outra embalagem (EANs distintos).
                if (factor < 2)
                    factor = absorbExtra.FatorEmbalagem >= 2 ? absorbExtra.FatorEmbalagem
                        : (absorbExtra.QtdAtacado >= 2 ? absorbExtra.QtdAtacado : 1);
            }
            else
            {
                // ALIAS herdado: preserva kind/fator.
                if (factor < 1)
                    factor = 1;
            }

            Upsert(conn, tx, keepId, entry.Barcode, kind, factor,
                $"merge_from_{absorbId}");
            moved.Add(new Entry(entry.Barcode, kind, factor, $"merge_from_{absorbId}"));
        }

        // Garante UNIT/PACK do keep na tabela.
        Upsert(conn, tx, keepId, keepUnitBarcode, ProductBarcodeKinds.Unit, 1, "merge_keep");
        if (!string.IsNullOrWhiteSpace(keepExtra.BarcodeEmbalagem))
        {
            Upsert(conn, tx, keepId, keepExtra.BarcodeEmbalagem, ProductBarcodeKinds.Pack,
                keepExtra.FatorEmbalagem >= 2 ? keepExtra.FatorEmbalagem
                    : (keepExtra.QtdAtacado >= 2 ? keepExtra.QtdAtacado : 1),
                "merge_keep");
        }

        DeactivateProduct(conn, tx, absorbId);
        return moved;
    }

    public static void DeactivateProduct(SqliteConnection conn, SqliteTransaction tx, int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE product_barcodes SET active = 0 WHERE product_id = $p;";
        cmd.Parameters.AddWithValue("$p", productId);
        cmd.ExecuteNonQuery();
    }

    public static void SyncFromProduct(
        SqliteConnection conn,
        SqliteTransaction? tx,
        int productId,
        string? unitBarcode,
        ProductExtra extra,
        string source = "catalog")
    {
        Upsert(conn, tx, productId, unitBarcode, ProductBarcodeKinds.Unit, 1, source);
        if (!string.IsNullOrWhiteSpace(extra.BarcodeEmbalagem))
        {
            Upsert(conn, tx, productId, extra.BarcodeEmbalagem, ProductBarcodeKinds.Pack,
                extra.FatorEmbalagem >= 2 ? extra.FatorEmbalagem
                    : (extra.QtdAtacado >= 2 ? extra.QtdAtacado : 1),
                source);
        }
    }

    static IEnumerable<string> CandidateDigits(string digits)
    {
        yield return digits;
        var stripped = digits.TrimStart('0');
        if (!string.IsNullOrEmpty(stripped) && stripped != digits)
            yield return stripped;
    }

    static Product ReadProduct(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(0),
            Code = reader.IsDBNull(1) ? null : reader.GetString(1),
            Barcode = reader.IsDBNull(2) ? null : reader.GetString(2),
            Name = reader.IsDBNull(3) ? "" : reader.GetString(3),
            GroupName = reader.IsDBNull(4) ? null : reader.GetString(4),
            Unit = reader.IsDBNull(5) ? "UN" : reader.GetString(5),
            CostPrice = reader.GetDouble(6),
            SalePrice = reader.GetDouble(7),
            MinStock = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8)),
            Stock = reader.GetDouble(9),
            Location = reader.IsDBNull(10) ? null : reader.GetString(10),
            ExtraJson = reader.IsDBNull(11) ? "{}" : reader.GetString(11),
            Active = !reader.IsDBNull(12) && Convert.ToInt32(reader.GetValue(12)) == 1,
            CreatedAt = reader.IsDBNull(13) ? "" : reader.GetString(13),
            StockFridge = reader.GetDouble(14),
            StockFridgeMin = Convert.ToInt32(reader.GetValue(15)),
        };
}
