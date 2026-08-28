using System.Data;
using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// 70B3A-B — motor de cobertura validade/lote sobre estoque já existente.
/// <para>
/// Invariante: nenhuma operação altera <c>products.stock</c> nem <c>stock_fridge</c>.
/// Lotes são cobertura/rastreabilidade, não o estoque físico canônico.
/// Geladeira é informativa e não entra na capacidade rastreável.
/// </para>
/// <para>
/// Decisão de escrita: <see cref="ProductLotService.Receive(SqliteConnection, SqliteTransaction, ProductLotReceiveInput)"/>
/// NÃO é alterado nesta etapa (o recebimento de compra depende de receber lote sem teto de stock;
/// o stock sobe no ApplyStock da compra). AddCoverage valida o teto <c>products.stock</c>
/// na mesma transação BEGIN IMMEDIATE e só então chama Receive(conn, tx) com unit_cost = 0.
/// Split/edit/qty/remove escrevem SQL próprio para não mesclar IDs em colisão
/// (purchase_item_lots.product_lot_id não pode ser apagado em silêncio).
/// </para>
/// Transação: Microsoft.Data.Sqlite mapeia <see cref="IsolationLevel.Serializable"/> para
/// <c>BEGIN IMMEDIATE</c>, compatível com a infraestrutura atual.
/// </summary>
public static class LotCoverageService
{
    public const double QtyEpsilon = 0.0001;

    /// <summary>Somente testes: depois de reduzir a origem no split e antes de gravar o destino.</summary>
    public static Action? TestBeforeSplitDestination { get; set; }

    public static LotCoverageSnapshot GetSnapshot(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        return ReadSnapshot(conn, tx: null, productId);
    }

    public static LotCoverageMutationResult AddCoverage(LotCoverageAddInput input)
    {
        EnsureCanMutate();
        var expiry = input.ExpiryDate.Date;
        if (expiry == default)
            throw new LotCoverageException(LotCoverageRules.ExpiryRequired, LotCoverageRules.ExpiryRequiredMessage);

        var qty = Math.Round(input.Quantity, 4);
        if (qty <= QtyEpsilon)
            throw new LotCoverageException(LotCoverageRules.QuantityInvalid, LotCoverageRules.QuantityInvalidMessage);

        var lot = NormalizeLot(input.LotNumber);
        var reason = ResolveAddReason(input.Reason);
        var origin = string.IsNullOrWhiteSpace(input.Origin)
            ? LotCoverageRules.OriginLegacyConference
            : input.Origin.Trim();

        return InImmediateTransaction((conn, tx) =>
        {
            var product = RequireMutableProduct(conn, tx, input.ProductId, forNewCoverage: true);
            var snap = ReadSnapshot(conn, tx, product.Id);
            RefuseIfOverTracked(snap);
            if (qty > snap.UntrackedQuantity + StockLotConsistencyService.Tolerance)
                throw new LotCoverageException(
                    LotCoverageRules.QuantityExceedsUntracked,
                    LotCoverageRules.QuantityExceedsUntrackedMessage);

            var lotId = ProductLotService.Receive(conn, tx, new ProductLotReceiveInput
            {
                ProductId = product.Id,
                Quantity = qty,
                LotNumber = lot,
                ExpiryDate = expiry,
                UnitCost = 0,
                Notes = reason,
            });

            var after = ReadSnapshot(conn, tx, product.Id);
            AssertStockUnchanged(snap.Stock, after.Stock);
            if (after.TrackedQuantity > after.Stock + StockLotConsistencyService.Tolerance)
                throw new LotCoverageException(
                    LotCoverageRules.QuantityExceedsUntracked,
                    LotCoverageRules.QuantityExceedsUntrackedMessage);

            var line = after.Lines.FirstOrDefault(l => l.Id == lotId);
            Audit(conn, tx, LotCoverageRules.ActionAdd, lotId, product.Id, reason, origin,
                "Cobertura de lote adicionada",
                new
                {
                    operation = "add",
                    product_id = product.Id,
                    product_lot_id = lotId,
                    quantity = qty,
                    expiry_date = Iso(expiry),
                    lot_number = lot,
                    reason,
                    origin,
                    before = (object?)null,
                    after = LinePayload(line),
                });

            return new LotCoverageMutationResult
            {
                ProductId = product.Id,
                ProductLotId = lotId,
                Snapshot = after,
            };
        });
    }

    public static LotCoverageMutationResult EditCoverage(LotCoverageEditInput input)
    {
        EnsureCanMutate();
        var reason = RequireReason(input.Reason);
        var expiry = input.ExpiryDate.Date;
        if (expiry == default)
            throw new LotCoverageException(LotCoverageRules.ExpiryRequired, LotCoverageRules.ExpiryRequiredMessage);
        var lot = NormalizeLot(input.LotNumber);

        return InImmediateTransaction((conn, tx) =>
        {
            var row = RequireLotRow(conn, tx, input.ProductLotId);
            var product = RequireMutableProduct(conn, tx, row.ProductId, forNewCoverage: false);
            var beforeSnap = ReadSnapshot(conn, tx, product.Id);

            var newIso = Iso(expiry);
            var oldExpired = IsExpired(row.ExpiryDate);
            var newExpired = expiry < DateTime.Today;
            var expiryChanged = !string.Equals(row.ExpiryIso ?? "", newIso, StringComparison.Ordinal);
            var lotChanged = !string.Equals(row.LotNumber, lot, StringComparison.Ordinal);
            if (expiryChanged || lotChanged)
            {
                var otherId = FindLotIdByKey(conn, tx, row.ProductId, lot, newIso);
                if (otherId is int oid && oid != row.Id)
                    throw new LotCoverageException(LotCoverageRules.KeyCollision, LotCoverageRules.KeyCollisionMessage);
            }

            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = """
                    UPDATE product_lots
                    SET lot_number = $lot, expiry_date = $exp
                    WHERE id = $id;
                    """;
                upd.Parameters.AddWithValue("$lot", lot);
                upd.Parameters.AddWithValue("$exp", newIso);
                upd.Parameters.AddWithValue("$id", row.Id);
                upd.ExecuteNonQuery();
            }

            var afterSnap = ReadSnapshot(conn, tx, product.Id);
            AssertStockUnchanged(beforeSnap.Stock, afterSnap.Stock);
            var afterLine = afterSnap.Lines.FirstOrDefault(l => l.Id == row.Id);
            var sensitive = oldExpired || (expiryChanged && newExpired);

            Audit(conn, tx, LotCoverageRules.ActionEdit, row.Id, product.Id, reason, origin: "edit",
                "Cobertura de lote editada",
                new
                {
                    operation = "edit",
                    product_id = product.Id,
                    product_lot_id = row.Id,
                    expiry_date = newIso,
                    lot_number = lot,
                    reason,
                    sensitive_expiry_correction = sensitive,
                    before = LotPayload(row),
                    after = LinePayload(afterLine),
                });

            return new LotCoverageMutationResult
            {
                ProductId = product.Id,
                ProductLotId = row.Id,
                SensitiveExpiryCorrection = sensitive,
                Snapshot = afterSnap,
            };
        });
    }

    public static LotCoverageMutationResult CorrectQuantity(LotCoverageQuantityInput input)
    {
        EnsureCanMutate();
        var reason = RequireReason(input.Reason);
        var newQty = Math.Round(input.Quantity, 4);
        if (newQty <= QtyEpsilon)
            throw new LotCoverageException(LotCoverageRules.QuantityInvalid, LotCoverageRules.QuantityInvalidMessage);

        return InImmediateTransaction((conn, tx) =>
        {
            var row = RequireLotRow(conn, tx, input.ProductLotId);
            var product = RequireMutableProduct(conn, tx, row.ProductId, forNewCoverage: false);
            var beforeSnap = ReadSnapshot(conn, tx, product.Id);
            var delta = Math.Round(newQty - row.Quantity, 4);

            if (delta > QtyEpsilon)
            {
                RefuseIfOverTracked(beforeSnap);
                if (delta > beforeSnap.UntrackedQuantity + StockLotConsistencyService.Tolerance)
                    throw new LotCoverageException(
                        LotCoverageRules.QuantityExceedsUntracked,
                        LotCoverageRules.QuantityExceedsUntrackedMessage);
            }

            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE product_lots SET quantity = $qty WHERE id = $id;";
                upd.Parameters.AddWithValue("$qty", newQty);
                upd.Parameters.AddWithValue("$id", row.Id);
                upd.ExecuteNonQuery();
            }

            var afterSnap = ReadSnapshot(conn, tx, product.Id);
            AssertStockUnchanged(beforeSnap.Stock, afterSnap.Stock);
            if (afterSnap.TrackedQuantity > afterSnap.Stock + StockLotConsistencyService.Tolerance)
                throw new LotCoverageException(
                    LotCoverageRules.QuantityExceedsUntracked,
                    LotCoverageRules.QuantityExceedsUntrackedMessage);

            var afterLine = afterSnap.Lines.FirstOrDefault(l => l.Id == row.Id);
            Audit(conn, tx, LotCoverageRules.ActionQuantityCorrect, row.Id, product.Id, reason, origin: "quantity",
                "Quantidade de cobertura corrigida",
                new
                {
                    operation = "quantity_correct",
                    product_id = product.Id,
                    product_lot_id = row.Id,
                    quantity = newQty,
                    quantity_before = row.Quantity,
                    reason,
                    before = LotPayload(row),
                    after = LinePayload(afterLine),
                });

            return new LotCoverageMutationResult
            {
                ProductId = product.Id,
                ProductLotId = row.Id,
                Snapshot = afterSnap,
            };
        });
    }

    public static LotCoverageMutationResult SplitCoverage(LotCoverageSplitInput input)
    {
        EnsureCanMutate();
        var reason = RequireReason(input.Reason);
        var destQty = Math.Round(input.DestinationQuantity, 4);
        var destExpiry = input.DestinationExpiryDate.Date;
        if (destExpiry == default)
            throw new LotCoverageException(LotCoverageRules.ExpiryRequired, LotCoverageRules.ExpiryRequiredMessage);
        if (destQty <= QtyEpsilon)
            throw new LotCoverageException(LotCoverageRules.QuantityInvalid, LotCoverageRules.QuantityInvalidMessage);
        var destLot = NormalizeLot(input.DestinationLotNumber);

        return InImmediateTransaction((conn, tx) =>
        {
            var row = RequireLotRow(conn, tx, input.ProductLotId);
            var product = RequireMutableProduct(conn, tx, row.ProductId, forNewCoverage: false);
            var beforeSnap = ReadSnapshot(conn, tx, product.Id);
            RefuseIfOverTracked(beforeSnap);

            var remain = Math.Round(row.Quantity - destQty, 4);
            if (remain <= QtyEpsilon)
                throw new LotCoverageException(LotCoverageRules.SplitInvalid, LotCoverageRules.SplitInvalidMessage);

            var destIso = Iso(destExpiry);
            var originIso = row.ExpiryIso ?? "";
            if (string.Equals(destLot, row.LotNumber, StringComparison.Ordinal)
                && string.Equals(destIso, originIso, StringComparison.Ordinal))
                throw new LotCoverageException(LotCoverageRules.SplitInvalid, LotCoverageRules.SplitSameIdentityMessage);

            var collide = FindLotIdByKey(conn, tx, row.ProductId, destLot, destIso);
            if (collide is int cid && cid != row.Id)
                throw new LotCoverageException(LotCoverageRules.KeyCollision, LotCoverageRules.KeyCollisionMessage);

            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE product_lots SET quantity = $qty WHERE id = $id;";
                upd.Parameters.AddWithValue("$qty", remain);
                upd.Parameters.AddWithValue("$id", row.Id);
                upd.ExecuteNonQuery();
            }

            TestBeforeSplitDestination?.Invoke();

            int destId;
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO product_lots (
                      product_id, lot_number, expiry_date, quantity, purchase_id, unit_cost, notes, created_at
                    ) VALUES (
                      $pid, $lot, $exp, $qty, NULL, $cost, $notes, datetime('now','localtime')
                    );
                    SELECT last_insert_rowid();
                    """;
                ins.Parameters.AddWithValue("$pid", row.ProductId);
                ins.Parameters.AddWithValue("$lot", destLot);
                ins.Parameters.AddWithValue("$exp", destIso);
                ins.Parameters.AddWithValue("$qty", destQty);
                ins.Parameters.AddWithValue("$cost", row.UnitCost);
                ins.Parameters.AddWithValue("$notes", reason);
                destId = Convert.ToInt32(ins.ExecuteScalar());
            }

            var afterSnap = ReadSnapshot(conn, tx, product.Id);
            AssertStockUnchanged(beforeSnap.Stock, afterSnap.Stock);
            var trackedDelta = Math.Abs(afterSnap.TrackedQuantity - beforeSnap.TrackedQuantity);
            if (trackedDelta > StockLotConsistencyService.Tolerance)
                throw new LotCoverageException(LotCoverageRules.SplitInvalid, LotCoverageRules.SplitInvalidMessage);

            var originAfter = afterSnap.Lines.FirstOrDefault(l => l.Id == row.Id);
            var destAfter = afterSnap.Lines.FirstOrDefault(l => l.Id == destId);
            Audit(conn, tx, LotCoverageRules.ActionSplit, row.Id, product.Id, reason, origin: "split",
                "Cobertura de lote dividida",
                new
                {
                    operation = "split",
                    product_id = product.Id,
                    product_lot_id = row.Id,
                    destination_lot_id = destId,
                    quantity = destQty,
                    expiry_date = destIso,
                    lot_number = destLot,
                    reason,
                    before = LotPayload(row),
                    after = new { origin = LinePayload(originAfter), destination = LinePayload(destAfter) },
                });

            return new LotCoverageMutationResult
            {
                ProductId = product.Id,
                ProductLotId = row.Id,
                DestinationLotId = destId,
                Snapshot = afterSnap,
            };
        });
    }

    public static LotCoverageMutationResult RemoveCoverage(LotCoverageRemoveInput input)
    {
        EnsureCanMutate();
        var reason = RequireReason(input.Reason);

        return InImmediateTransaction((conn, tx) =>
        {
            var row = RequireLotRow(conn, tx, input.ProductLotId);
            var product = RequireMutableProduct(conn, tx, row.ProductId, forNewCoverage: false);
            var beforeSnap = ReadSnapshot(conn, tx, product.Id);

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM product_lots WHERE id = $id;";
                del.Parameters.AddWithValue("$id", row.Id);
                del.ExecuteNonQuery();
            }

            var afterSnap = ReadSnapshot(conn, tx, product.Id);
            AssertStockUnchanged(beforeSnap.Stock, afterSnap.Stock);

            Audit(conn, tx, LotCoverageRules.ActionRemove, row.Id, product.Id, reason, origin: "remove",
                "Cobertura de lote removida",
                new
                {
                    operation = "remove",
                    product_id = product.Id,
                    product_lot_id = row.Id,
                    quantity = row.Quantity,
                    expiry_date = row.ExpiryIso,
                    lot_number = row.LotNumber,
                    reason,
                    before = LotPayload(row),
                    after = (object?)null,
                });

            return new LotCoverageMutationResult
            {
                ProductId = product.Id,
                ProductLotId = row.Id,
                Snapshot = afterSnap,
            };
        });
    }

    private static LotCoverageMutationResult InImmediateTransaction(
        Func<SqliteConnection, SqliteTransaction, LotCoverageMutationResult> body)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var result = body(conn, tx);
            tx.Commit();
            return result;
        }
        finally
        {
            TestBeforeSplitDestination = null;
        }
    }

    private static void EnsureCanMutate()
    {
        if (!AccessControl.CanMutateLotCoverage())
            throw new LotCoverageException(LotCoverageRules.AccessDenied, LotCoverageRules.AccessDeniedMessage);
        StoreNetworkMode.EnsureLocalMutationAllowed("cobertura de lote");
    }

    private static string NormalizeLot(string? lotNumber) => (lotNumber ?? "").Trim();

    private static string ResolveAddReason(string? reason)
    {
        var trimmed = (reason ?? "").Trim();
        return string.IsNullOrEmpty(trimmed)
            ? LotCoverageRules.PhysicalConferenceReason
            : trimmed;
    }

    private static string RequireReason(string? reason)
    {
        var trimmed = (reason ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new LotCoverageException(LotCoverageRules.ReasonRequired, LotCoverageRules.ReasonRequiredMessage);
        return trimmed;
    }

    private static string Iso(DateTime date) => date.Date.ToString("yyyy-MM-dd");

    private static bool IsExpired(DateTime? expiry) =>
        expiry is DateTime d && d.Date < DateTime.Today;

    private static void RefuseIfOverTracked(LotCoverageSnapshot snap)
    {
        if (snap.ConsistencyStatus == LotCoverageConsistencyStatus.OverTracked)
            throw new LotCoverageException(LotCoverageRules.OverTracked, LotCoverageRules.OverTrackedMessage);
    }

    private static void AssertStockUnchanged(double before, double after)
    {
        if (Math.Abs(before - after) > StockLotConsistencyService.Tolerance)
            throw new InvalidOperationException("Invariante violado: products.stock foi alterado na manutenção de cobertura.");
    }

    private static void EnsureNoOpenInventory(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id FROM inventory_sessions WHERE status = 'aberta'
            ORDER BY id DESC LIMIT 1;
            """;
        var o = cmd.ExecuteScalar();
        if (o is not null and not DBNull)
            throw new LotCoverageException(LotCoverageRules.OpenInventory, LotCoverageRules.OpenInventoryMessage);
    }

    private sealed record ProductRow(
        int Id, string Name, double Stock, double Fridge, bool Active, double CostPrice);

    private static ProductRow RequireMutableProduct(
        SqliteConnection conn, SqliteTransaction tx, int productId, bool forNewCoverage)
    {
        EnsureNoOpenInventory(conn, tx);
        var product = LoadProduct(conn, tx, productId);
        if (product is null)
            throw new LotCoverageException(LotCoverageRules.ProductNotFound, LotCoverageRules.ProductNotFoundMessage);
        if (!product.Active)
        {
            if (IsAbsorbed(conn, tx, productId))
                throw new LotCoverageException(LotCoverageRules.AbsorbedProduct, LotCoverageRules.AbsorbedProductMessage);
            throw new LotCoverageException(LotCoverageRules.InactiveProduct, LotCoverageRules.InactiveProductMessage);
        }

        var tol = StockLotConsistencyService.Tolerance;
        if (product.Stock < -tol)
            throw new LotCoverageException(LotCoverageRules.NegativeStock, LotCoverageRules.NegativeStockMessage);
        if (forNewCoverage && product.Stock <= tol)
            throw new LotCoverageException(LotCoverageRules.ZeroStock, LotCoverageRules.ZeroStockMessage);
        return product;
    }

    /// <summary>
    /// Absorb detectável: cadastro inativo cujo id aparece como absorb em auditoria de merge,
    /// ou inativo com stock zerado após unificação. Sem coluna nova.
    /// </summary>
    private static bool IsAbsorbed(SqliteConnection conn, SqliteTransaction tx, int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT 1 FROM audit_log
            WHERE action = 'unificar'
              AND entity = 'produto'
              AND (
                    IFNULL(details,'') LIKE $likeComma
                    OR IFNULL(details,'') LIKE $likeEnd
                  )
            LIMIT 1;
            """;
        var token = "\"absorb_id\":" + productId;
        cmd.Parameters.AddWithValue("$likeComma", "%" + token + ",%");
        cmd.Parameters.AddWithValue("$likeEnd", "%" + token + "}%");
        var o = cmd.ExecuteScalar();
        return o is not null and not DBNull;
    }

    private static ProductRow? LoadProduct(SqliteConnection conn, SqliteTransaction tx, int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT IFNULL(name,''), IFNULL(stock,0), IFNULL(stock_fridge,0),
                   IFNULL(active,1), IFNULL(cost_price,0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return new ProductRow(
            productId,
            reader.GetString(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetInt32(3) != 0,
            reader.GetDouble(4));
    }

    private sealed record LotRow(
        int Id, int ProductId, string LotNumber, string? ExpiryIso, DateTime? ExpiryDate,
        double Quantity, double UnitCost, int? PurchaseId);

    private static LotRow RequireLotRow(SqliteConnection conn, SqliteTransaction tx, int lotId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT product_id, IFNULL(lot_number,''), expiry_date, quantity,
                   IFNULL(unit_cost,0), purchase_id
            FROM product_lots WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", lotId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new LotCoverageException(LotCoverageRules.LotNotFound, LotCoverageRules.LotNotFoundMessage);
        string? iso = reader.IsDBNull(2) ? null : reader.GetString(2);
        DateTime? expiry = null;
        if (!string.IsNullOrWhiteSpace(iso) && DateTime.TryParse(iso, out var d))
            expiry = d.Date;
        return new LotRow(
            lotId,
            reader.GetInt32(0),
            reader.GetString(1),
            string.IsNullOrWhiteSpace(iso) ? null : iso,
            expiry,
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5));
    }

    private static int? FindLotIdByKey(
        SqliteConnection conn, SqliteTransaction tx, int productId, string lot, string expiryIso)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id FROM product_lots
            WHERE product_id = $pid
              AND IFNULL(lot_number,'') = $lot
              AND IFNULL(expiry_date,'') = IFNULL($exp,'')
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$lot", lot);
        cmd.Parameters.AddWithValue("$exp", string.IsNullOrEmpty(expiryIso) ? DBNull.Value : expiryIso);
        var o = cmd.ExecuteScalar();
        return o is null or DBNull ? null : Convert.ToInt32(o);
    }

    private static LotCoverageSnapshot ReadSnapshot(SqliteConnection conn, SqliteTransaction? tx, int productId)
    {
        if (productId <= 0)
        {
            return new LotCoverageSnapshot
            {
                ProductId = productId,
                ConsistencyStatus = LotCoverageConsistencyStatus.ProductNotFound,
            };
        }

        using var prod = conn.CreateCommand();
        prod.Transaction = tx;
        prod.CommandText = """
            SELECT IFNULL(name,''), IFNULL(stock,0), IFNULL(stock_fridge,0),
                   IFNULL(active,1), IFNULL(cost_price,0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        prod.Parameters.AddWithValue("$id", productId);
        using var reader = prod.ExecuteReader();
        if (!reader.Read())
        {
            return new LotCoverageSnapshot
            {
                ProductId = productId,
                ConsistencyStatus = LotCoverageConsistencyStatus.ProductNotFound,
            };
        }

        var name = reader.GetString(0);
        var stock = reader.GetDouble(1);
        var fridge = reader.GetDouble(2);
        var active = reader.GetInt32(3) != 0;
        var costPrice = reader.GetDouble(4);
        reader.Close();

        var lines = new List<LotCoverageLine>();
        using (var lots = conn.CreateCommand())
        {
            lots.Transaction = tx;
            lots.CommandText = """
                SELECT id, IFNULL(lot_number,''), expiry_date, quantity,
                       IFNULL(unit_cost,0), purchase_id
                FROM product_lots
                WHERE product_id = $pid
                ORDER BY
                  CASE WHEN expiry_date IS NULL OR TRIM(expiry_date)='' THEN 1 ELSE 0 END,
                  expiry_date ASC, id ASC;
                """;
            lots.Parameters.AddWithValue("$pid", productId);
            using var lr = lots.ExecuteReader();
            while (lr.Read())
            {
                var qty = lr.GetDouble(3);
                if (qty <= QtyEpsilon)
                    continue;
                var lotNumber = lr.IsDBNull(1) ? "" : lr.GetString(1);
                DateTime? expiry = null;
                if (!lr.IsDBNull(2))
                {
                    var iso = lr.GetString(2);
                    if (!string.IsNullOrWhiteSpace(iso) && DateTime.TryParse(iso, out var d))
                        expiry = d.Date;
                }
                var unitCost = lr.GetDouble(4);
                var cost = ValidityControlEngine.ResolveLotCost(unitCost, costPrice);
                lines.Add(new LotCoverageLine
                {
                    Id = lr.GetInt32(0),
                    LotNumber = lotNumber,
                    ExpiryDate = expiry,
                    Quantity = qty,
                    UnitCost = unitCost,
                    PurchaseId = lr.IsDBNull(5) ? null : lr.GetInt32(5),
                    Traceability = DeriveTraceability(lotNumber, expiry),
                    CostSource = cost.Source,
                    UsedCost = cost.UsedCost,
                    IsExpired = IsExpired(expiry),
                });
            }
        }

        var tracked = Math.Round(lines.Where(l => l.Quantity > 0).Sum(l => l.Quantity), 4);
        var untracked = Math.Round(Math.Max(stock - tracked, 0), 4);
        var over = Math.Round(Math.Max(tracked - stock, 0), 4);

        return new LotCoverageSnapshot
        {
            ProductId = productId,
            ProductName = name,
            ProductActive = active,
            Stock = stock,
            StockFridge = fridge,
            TrackedQuantity = tracked,
            UntrackedQuantity = untracked,
            OverCoverage = over,
            CostPrice = costPrice,
            ConsistencyStatus = Classify(stock, tracked, untracked, over),
            Lines = lines,
        };
    }

    internal static LotCoverageTraceability DeriveTraceability(string lotNumber, DateTime? expiry)
    {
        if (expiry is null)
            return LotCoverageTraceability.UninformedExpiry;
        if (string.IsNullOrWhiteSpace(lotNumber))
            return LotCoverageTraceability.Partial;
        return LotCoverageTraceability.Complete;
    }

    private static LotCoverageConsistencyStatus Classify(
        double stock, double tracked, double untracked, double over)
    {
        var tol = StockLotConsistencyService.Tolerance;
        if (stock < -tol)
            return LotCoverageConsistencyStatus.NegativeStock;
        if (over > tol || tracked > stock + tol)
            return LotCoverageConsistencyStatus.OverTracked;
        if (stock <= tol)
            return LotCoverageConsistencyStatus.ZeroStock;
        if (untracked > tol)
            return LotCoverageConsistencyStatus.UnderTracked;
        return LotCoverageConsistencyStatus.Consistent;
    }

    private static void Audit(
        SqliteConnection conn,
        SqliteTransaction tx,
        string action,
        int lotId,
        int productId,
        string reason,
        string origin,
        string summary,
        object payload)
    {
        _ = reason;
        _ = origin;
        AuditService.LogJson(conn, tx, action, LotCoverageRules.Entity, lotId.ToString(), payload, summary);
        _ = productId;
    }

    private static object LotPayload(LotRow row) => new
    {
        id = row.Id,
        lot_number = row.LotNumber,
        expiry_date = row.ExpiryIso,
        quantity = row.Quantity,
        unit_cost = row.UnitCost,
        purchase_id = row.PurchaseId,
    };

    private static object? LinePayload(LotCoverageLine? line) =>
        line is null
            ? null
            : new
            {
                id = line.Id,
                lot_number = line.LotNumber,
                expiry_date = line.ExpiryDate?.ToString("yyyy-MM-dd"),
                quantity = line.Quantity,
                unit_cost = line.UnitCost,
                traceability = line.Traceability.ToString(),
            };
}
