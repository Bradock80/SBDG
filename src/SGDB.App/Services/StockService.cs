using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public static class StockService
{
    /// <summary>
    /// Somente testes: invocado no início de <see cref="Adjust(SqliteConnection, SqliteTransaction, int, StockAdjustMode, double?, double?, string?, double?)"/>.
    /// Deve permanecer null em produção.
    /// </summary>
    public static Action<int>? TestBeforeTransactionalAdjust { get; set; }

    public static StockAdjustResult Adjust(
        int productId,
        StockAdjustMode mode,
        double? quantity = null,
        double? newStock = null,
        string? notes = null,
        double? unitCost = null)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.AdjustStock(productId, mode, quantity, newStock, notes, unitCost);
        return AdjustLocal(productId, mode, quantity, newStock, notes, unitCost);
    }

    public static StockAdjustResult AdjustLocal(
        int productId,
        StockAdjustMode mode,
        double? quantity = null,
        double? newStock = null,
        string? notes = null,
        double? unitCost = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("ajustar estoque");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        var result = AdjustCore(conn, tx, productId, mode, quantity, newStock, notes, unitCost);
        tx.Commit();
        return result;
    }

    /// <summary>
    /// Ajuste na conexão/transação externas. Não abre conexão, não faz Commit nem Rollback.
    /// </summary>
    public static StockAdjustResult Adjust(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        StockAdjustMode mode,
        double? quantity = null,
        double? newStock = null,
        string? notes = null,
        double? unitCost = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("ajustar estoque");
        TestBeforeTransactionalAdjust?.Invoke(productId);
        return AdjustCore(conn, tx, productId, mode, quantity, newStock, notes, unitCost);
    }

    private static StockAdjustResult AdjustCore(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        StockAdjustMode mode,
        double? quantity,
        double? newStock,
        string? notes,
        double? unitCost)
    {
        var product = GetProductWithExtra(conn, tx, productId)
            ?? throw new InvalidOperationException("Produto não encontrado.");

        var stockBefore = product.Stock;
        var stockBeforeForAverage = PurchaseAverageCostRules.PhysicalStock(
            product.Stock, product.StockFridge);
        string movType;
        double qty;
        string finalNotes;

        if (mode == StockAdjustMode.Saldo)
        {
            if (newStock is null)
                throw new InvalidOperationException("Informe o novo saldo.");
            var target = Math.Round(newStock.Value, 4);
            var delta = Math.Round(target - stockBefore, 4);
            if (Math.Abs(delta) < 1e-9)
            {
                return new StockAdjustResult
                {
                    ProductId = productId,
                    StockBefore = stockBefore,
                    StockAfter = stockBefore,
                    Quantity = 0,
                };
            }

            movType = delta > 0 ? "entrada" : "saida";
            qty = Math.Abs(delta);
            var auto = $"Ajuste saldo: {stockBefore:G} → {target:G}";
            finalNotes = string.IsNullOrWhiteSpace(notes) ? auto : $"{auto}. {notes.Trim()}";
        }
        else if (mode == StockAdjustMode.Entrada)
        {
            if (quantity is null || quantity <= 0)
                throw new InvalidOperationException("Informe a quantidade.");
            movType = "entrada";
            qty = quantity.Value;
            finalNotes = string.IsNullOrWhiteSpace(notes) ? "Entrada manual de estoque" : notes.Trim();
        }
        else
        {
            if (quantity is null || quantity <= 0)
                throw new InvalidOperationException("Informe a quantidade.");
            movType = "saida";
            qty = quantity.Value;
            if (qty > stockBefore + 1e-9)
                throw new InvalidOperationException(
                    $"Saída ({qty:N3}) maior que o estoque atual ({stockBefore:N3}).");
            finalNotes = string.IsNullOrWhiteSpace(notes) ? "Saída manual de estoque" : notes.Trim();
        }

        var stockAfter = movType == "entrada"
            ? stockBefore + qty
            : stockBefore - qty;

        var movementUnitPrice = product.CostPrice;
        var applyCost = movType == "entrada" && unitCost is >= 0;
        if (applyCost)
        {
            var incoming = Math.Max(0, unitCost!.Value);
            PurchaseAverageCostRules.RequireUsableStockBefore(stockBeforeForAverage, product.Name);
            var newCost = ProductPriceHelper.WeightedAverageCost(
                stockBeforeForAverage, product.CostPrice, qty, incoming);
            movementUnitPrice = incoming;

            var extra = ProductExtra.Parse(product.ExtraJson);
            extra.PrecoCompra = Math.Round(incoming, 4);

            using var updCost = conn.CreateCommand();
            updCost.Transaction = tx;
            updCost.CommandText = """
                UPDATE products
                SET stock = $stock, cost_price = $cost, extra_json = $extra
                WHERE id = $id;
                """;
            updCost.Parameters.AddWithValue("$stock", stockAfter);
            updCost.Parameters.AddWithValue("$cost", newCost);
            updCost.Parameters.AddWithValue("$extra", extra.ToJson());
            updCost.Parameters.AddWithValue("$id", productId);
            updCost.ExecuteNonQuery();
        }
        else
        {
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE products SET stock = $stock WHERE id = $id;";
            upd.Parameters.AddWithValue("$stock", stockAfter);
            upd.Parameters.AddWithValue("$id", productId);
            upd.ExecuteNonQuery();
        }

        var movId = InsertMovement(conn, tx, productId, movType, qty, movementUnitPrice, finalNotes,
            stockBefore: stockBefore, stockAfter: stockAfter,
            operation: mode == StockAdjustMode.Saldo ? "ajuste_manual"
                : mode == StockAdjustMode.Entrada ? "entrada_manual" : "saida_manual",
            unit: null);

        return new StockAdjustResult
        {
            ProductId = productId,
            StockBefore = stockBefore,
            StockAfter = stockAfter,
            MovementType = movType,
            Quantity = qty,
            MovementId = movId,
        };
    }

    /// <summary>
    /// Somente testes: invocado após o UPDATE de stock_fridge e antes do movement em
    /// <see cref="AdjustFridge"/>. Deve permanecer null em produção.
    /// </summary>
    public static Action<int>? TestBeforeFridgeAdjustMovement { get; set; }

    /// <summary>
    /// Corrige a quantidade física da geladeira (stock_fridge). Não altera o depósito.
    /// Motivo/observação é obrigatório. Não atualiza custo médio nem lotes.
    /// </summary>
    public static StockAdjustResult AdjustFridge(
        int productId,
        StockAdjustMode mode,
        double? quantity = null,
        double? newStock = null,
        string? notes = null)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.AdjustFridgeStock(productId, mode, quantity, newStock, notes);
        return AdjustFridgeLocal(productId, mode, quantity, newStock, notes);
    }

    public static StockAdjustResult AdjustFridgeLocal(
        int productId,
        StockAdjustMode mode,
        double? quantity = null,
        double? newStock = null,
        string? notes = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("ajustar geladeira");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        var result = AdjustFridgeCore(conn, tx, productId, mode, quantity, newStock, notes);
        tx.Commit();
        return result;
    }

    /// <summary>
    /// Ajuste da geladeira na conexão/transação externas. Não abre conexão, não faz Commit nem Rollback.
    /// </summary>
    public static StockAdjustResult AdjustFridge(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        StockAdjustMode mode,
        double? quantity = null,
        double? newStock = null,
        string? notes = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("ajustar geladeira");
        return AdjustFridgeCore(conn, tx, productId, mode, quantity, newStock, notes);
    }

    private static StockAdjustResult AdjustFridgeCore(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        StockAdjustMode mode,
        double? quantity,
        double? newStock,
        string? notes)
    {
        var product = GetProductFridge(conn, tx, productId)
            ?? throw new InvalidOperationException("Produto não encontrado.");

        var warehouse = product.Stock;
        var fridgeBefore = product.StockFridge;
        string movType;
        double qty;
        double fridgeAfter;

        if (mode == StockAdjustMode.Saldo)
        {
            if (newStock is null)
                throw new InvalidOperationException("Informe o novo saldo da geladeira.");
            if (!double.IsFinite(newStock.Value))
                throw new InvalidOperationException("Informe um saldo válido da geladeira.");
            if (newStock.Value < 0)
                throw new InvalidOperationException("O saldo da geladeira não pode ser negativo.");

            var target = Math.Round(newStock.Value, 4);
            if (target < 0)
                throw new InvalidOperationException("O saldo da geladeira não pode ser negativo.");

            var delta = Math.Round(target - fridgeBefore, 4);
            if (delta > -1e-9 && delta < 1e-9)
            {
                return new StockAdjustResult
                {
                    ProductId = productId,
                    StockBefore = fridgeBefore,
                    StockAfter = fridgeBefore,
                    Quantity = 0,
                };
            }

            RequireFridgeReason(notes);
            movType = delta > 0 ? "entrada" : "saida";
            qty = delta > 0 ? delta : -delta;
            fridgeAfter = target;
        }
        else if (mode == StockAdjustMode.Entrada)
        {
            qty = RequirePositiveFiniteQty(quantity);
            RequireFridgeReason(notes);
            movType = "entrada";
            fridgeAfter = Math.Round(fridgeBefore + qty, 4);
        }
        else
        {
            qty = RequirePositiveFiniteQty(quantity);
            RequireFridgeReason(notes);
            movType = "saida";
            if (qty > fridgeBefore + 1e-9)
                throw new InvalidOperationException(
                    $"Saída ({qty:N3}) maior que a geladeira atual ({fridgeBefore:N3}).");
            fridgeAfter = Math.Round(fridgeBefore - qty, 4);
        }

        if (fridgeAfter < -1e-9)
            throw new InvalidOperationException("O saldo da geladeira não pode ficar negativo.");
        if (fridgeAfter < 0)
            fridgeAfter = 0;

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE products SET stock_fridge = $fridge WHERE id = $id;";
            upd.Parameters.AddWithValue("$fridge", fridgeAfter);
            upd.Parameters.AddWithValue("$id", productId);
            upd.ExecuteNonQuery();
        }

        TestBeforeFridgeAdjustMovement?.Invoke(productId);

        var totalBefore = Math.Round(warehouse + fridgeBefore, 4);
        var totalAfter = Math.Round(warehouse + fridgeAfter, 4);
        var user = AppSession.CurrentUser is null
            ? "Sistema"
            : (AppSession.CurrentUser.Nome ?? AppSession.CurrentUser.Login ?? "Sistema");
        var finalNotes =
            $"Ajuste geladeira: {fridgeBefore:G} → {fridgeAfter:G}. Motivo: {notes!.Trim()}. Usuário: {user}";

        var movId = InsertMovement(conn, tx, productId, movType, qty, product.CostPrice, finalNotes,
            stockBefore: totalBefore, stockAfter: totalAfter,
            operation: "ajuste_geladeira",
            unit: null);

        return new StockAdjustResult
        {
            ProductId = productId,
            StockBefore = fridgeBefore,
            StockAfter = fridgeAfter,
            MovementType = movType,
            Quantity = qty,
            MovementId = movId,
        };
    }

    private static double RequirePositiveFiniteQty(double? quantity)
    {
        if (quantity is null || !double.IsFinite(quantity.Value) || quantity.Value <= 0)
            throw new InvalidOperationException("Informe a quantidade.");
        var qty = Math.Round(quantity.Value, 4);
        if (qty <= 0)
            throw new InvalidOperationException("Informe a quantidade.");
        return qty;
    }

    private static void RequireFridgeReason(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            throw new InvalidOperationException("Informe o motivo do ajuste da geladeira.");
    }

    /// <summary>Atualiza só o estoque mínimo do produto (cadastro).</summary>
    public static void UpdateMinStock(int productId, int minStock)
    {
        minStock = Math.Max(0, minStock);
        if (StoreNetworkMode.IsClient)
        {
            var p = ProductService.GetById(productId)
                ?? throw new InvalidOperationException("Produto não encontrado.");
            ProductService.Update(productId, new ProductInput
            {
                Code = p.Code,
                Barcode = p.Barcode,
                Name = p.Name,
                GroupName = p.GroupName,
                Unit = p.Unit,
                CostPrice = p.CostPrice,
                SalePrice = p.SalePrice,
                MinStock = minStock,
                Stock = p.Stock,
                StockFridge = p.StockFridge,
                StockFridgeMin = p.StockFridgeMin,
                Location = p.Location,
                Extra = ProductExtra.Parse(p.ExtraJson),
                Active = p.Active,
            });
            return;
        }

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET min_stock = $min WHERE id = $id;";
        cmd.Parameters.AddWithValue("$min", minStock);
        cmd.Parameters.AddWithValue("$id", productId);
        var n = cmd.ExecuteNonQuery();
        if (n == 0)
            throw new InvalidOperationException("Produto não encontrado.");
    }

    /// <summary>
    /// Define o estoque total desejado (depósito + geladeira), ajustando só o depósito.
    /// </summary>
    public static void SetTotalStock(int productId, double desiredTotal, string? notes = null)
    {
        desiredTotal = Math.Round(Math.Max(0, desiredTotal), 4);
        var p = ProductService.GetById(productId)
            ?? throw new InvalidOperationException("Produto não encontrado.");
        var fridge = Math.Max(0, p.StockFridge);
        var newWarehouse = Math.Round(Math.Max(0, desiredTotal - fridge), 4);
        Adjust(productId, StockAdjustMode.Saldo, newStock: newWarehouse,
            notes: notes ?? "Correção pela tela de Estoque Mínimo");
    }

    /// <summary>
    /// Move quantidade do depósito (stock) para a geladeira (stock_fridge).
    /// Não altera o estoque total.
    /// </summary>
    public static StockAdjustResult TransferWarehouseToFridge(int productId, double quantity)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("transferência geladeira");
        quantity = Math.Round(Math.Abs(quantity), 4);
        if (quantity < 0.0001)
            throw new InvalidOperationException("Informe a quantidade a transferir.");

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var row = GetProductFridge(conn, tx, productId)
            ?? throw new InvalidOperationException("Produto não encontrado.");

        if (row.Stock < -0.0001)
            throw new InvalidOperationException(
                "Depósito com saldo negativo. Ajuste o estoque antes de repor a geladeira.");
        if (quantity > row.Stock + 1e-9)
            throw new InvalidOperationException(
                $"Quantidade ({quantity:N3}) maior que o depósito ({row.Stock:N3}).");

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE products
                SET stock = stock - $qty,
                    stock_fridge = IFNULL(stock_fridge, 0) + $qty
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$qty", quantity);
            upd.Parameters.AddWithValue("$id", productId);
            upd.ExecuteNonQuery();
        }

        var stockBefore = row.Stock + row.StockFridge;
        var movId = InsertMovement(conn, tx, productId, "saida", quantity, row.CostPrice,
            $"Transferência depósito→geladeira ({quantity:G})",
            stockBefore: stockBefore, stockAfter: stockBefore,
            operation: "transferencia_geladeira",
            unit: null);
        tx.Commit();

        return new StockAdjustResult
        {
            ProductId = productId,
            StockBefore = row.Stock,
            StockAfter = row.Stock - quantity,
            MovementType = "transfer_fridge",
            Quantity = quantity,
            MovementId = movId,
        };
    }

    /// <summary>
    /// Somente testes: invocado após o UPDATE e antes do movement em
    /// <see cref="TransferFridgeToWarehouse"/>. Deve permanecer null em produção.
    /// </summary>
    public static Action<int>? TestBeforeFridgeReturnMovement { get; set; }

    /// <summary>
    /// Move quantidade da geladeira (stock_fridge) para o depósito (stock).
    /// Não altera o estoque total nem os lotes.
    /// </summary>
    public static StockAdjustResult TransferFridgeToWarehouse(int productId, double quantity)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("retorno geladeira");
        if (!double.IsFinite(quantity) || quantity <= 0)
            throw new InvalidOperationException("Informe a quantidade a retornar.");
        quantity = Math.Round(quantity, 4);
        if (quantity < 0.0001)
            throw new InvalidOperationException("Informe a quantidade a retornar.");

        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var row = GetProductFridge(conn, tx, productId)
            ?? throw new InvalidOperationException("Produto não encontrado.");

        if (quantity > row.StockFridge + 1e-9)
            throw new InvalidOperationException(
                $"Quantidade ({quantity:N3}) maior que a geladeira ({row.StockFridge:N3}).");

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE products
                SET stock = stock + $qty,
                    stock_fridge = IFNULL(stock_fridge, 0) - $qty
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$qty", quantity);
            upd.Parameters.AddWithValue("$id", productId);
            upd.ExecuteNonQuery();
        }

        TestBeforeFridgeReturnMovement?.Invoke(productId);

        var stockBefore = row.Stock + row.StockFridge;
        var movId = InsertMovement(conn, tx, productId, "entrada", quantity, row.CostPrice,
            $"Retorno geladeira→depósito ({quantity:G})",
            stockBefore: stockBefore, stockAfter: stockBefore,
            operation: "retorno_geladeira",
            unit: null);
        tx.Commit();

        return new StockAdjustResult
        {
            ProductId = productId,
            StockBefore = row.Stock,
            StockAfter = row.Stock + quantity,
            MovementType = "return_fridge",
            Quantity = quantity,
            MovementId = movId,
        };
    }

    /// <summary>Baixa de venda: geladeira primeiro; resto do depósito. Opcional por produto.</summary>
    public static void ApplySaleDeduction(
        SqliteConnection conn, SqliteTransaction tx, int productId, double qty,
        string? notes = null, string? refType = null, int? refId = null)
    {
        qty = Math.Round(Math.Abs(qty), 4);
        if (qty < 0.0001) return;

        var row = GetProductFridge(conn, tx, productId);
        if (row is null) return;

        var stockBefore = Round4(row.Stock + row.StockFridge);

        double fromFridge = 0;
        double fromWh = qty;
        if (UsesFridge(row.StockFridge, row.StockFridgeMin))
        {
            fromFridge = Math.Min(qty, Math.Max(0, row.StockFridge));
            fromWh = Math.Round(qty - fromFridge, 4);
        }

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = """
            UPDATE products SET
                stock_fridge = IFNULL(stock_fridge, 0) - $fridge,
                stock = stock - $wh
            WHERE id = $id;
            """;
        upd.Parameters.AddWithValue("$fridge", fromFridge);
        upd.Parameters.AddWithValue("$wh", fromWh);
        upd.Parameters.AddWithValue("$id", productId);
        upd.ExecuteNonQuery();

        // FEFO: baixa lotes do depósito (geladeira não tem lote separado)
        if (fromWh > 0.0001)
            ProductLotService.DeductFefo(conn, tx, productId, fromWh);

        var stockAfter = Round4(stockBefore - qty);
        InsertMovement(conn, tx, productId, "saida", qty, row.CostPrice,
            notes ?? "Venda",
            stockBefore: stockBefore, stockAfter: stockAfter,
            operation: "venda",
            unit: null,
            refType: refType, refId: refId);
    }

    /// <summary>Devolve venda: se o produto usa geladeira, devolve para a geladeira.</summary>
    public static void ApplySaleRestore(
        SqliteConnection conn, SqliteTransaction tx, int productId, double qty,
        string? notes = null, string? refType = null, int? refId = null,
        string? operation = null)
    {
        qty = Math.Round(Math.Abs(qty), 4);
        if (qty < 0.0001) return;

        var row = GetProductFridge(conn, tx, productId);
        if (row is null) return;

        var stockBefore = Round4(row.Stock + row.StockFridge);

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        if (UsesFridge(row.StockFridge, row.StockFridgeMin))
        {
            upd.CommandText = """
                UPDATE products
                SET stock_fridge = IFNULL(stock_fridge, 0) + $qty
                WHERE id = $id;
                """;
        }
        else
        {
            upd.CommandText = "UPDATE products SET stock = stock + $qty WHERE id = $id;";
        }
        upd.Parameters.AddWithValue("$qty", qty);
        upd.Parameters.AddWithValue("$id", productId);
        upd.ExecuteNonQuery();

        if (!UsesFridge(row.StockFridge, row.StockFridgeMin))
            ProductLotService.RestoreToNearestLot(conn, tx, productId, qty);

        var stockAfter = Round4(stockBefore + qty);
        var op = string.IsNullOrWhiteSpace(operation) ? "cancelamento_venda" : operation.Trim();
        InsertMovement(conn, tx, productId, "entrada", qty, row.CostPrice,
            notes ?? "Cancelamento / devolução de venda",
            stockBefore: stockBefore, stockAfter: stockAfter,
            operation: op,
            unit: null,
            refType: refType, refId: refId);
    }

    public static bool UsesFridge(double stockFridge, int stockFridgeMin) =>
        stockFridgeMin > 0 || stockFridge > 0.0001;

    public static bool NeedsFridgeRestock(double stockFridge, int stockFridgeMin) =>
        stockFridgeMin > 0 && stockFridge <= stockFridgeMin + 1e-9;

    public static int ZeroNegativeStock()
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("zerar estoque negativo");
        using var conn = DatabaseService.OpenConnection();
        using var listCmd = conn.CreateCommand();
        listCmd.CommandText = """
            SELECT id, IFNULL(stock, 0), IFNULL(cost_price, 0)
            FROM products
            WHERE IFNULL(active, 1) = 1 AND IFNULL(stock, 0) < 0
            ORDER BY stock ASC;
            """;
        var items = new List<(int Id, double Stock, double Cost)>();
        using (var reader = listCmd.ExecuteReader())
        {
            while (reader.Read())
                items.Add((reader.GetInt32(0), reader.GetDouble(1), reader.GetDouble(2)));
        }

        if (items.Count == 0)
            return 0;

        using var tx = conn.BeginTransaction();
        foreach (var item in items)
        {
            var qty = Math.Abs(item.Stock);
            var note = $"Ajuste saldo: {item.Stock:G} → 0. Zera estoque negativo";
            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE products SET stock = 0 WHERE id = $id;";
                upd.Parameters.AddWithValue("$id", item.Id);
                upd.ExecuteNonQuery();
            }
            InsertMovement(conn, tx, item.Id, "entrada", qty, item.Cost, note,
                stockBefore: item.Stock, stockAfter: 0,
                operation: "ajuste_manual");
        }
        tx.Commit();
        return items.Count;
    }

    public static IReadOnlyList<StockMovementRow> ListRecentMovements(int limit = 80)
        => ListMovements(productId: null, limit);

    public static IReadOnlyList<StockMovementRow> ListMovementsByProduct(int productId, int limit = 40)
        => ListMovements(productId, limit);

    private static IReadOnlyList<StockMovementRow> ListMovements(int? productId, int limit)
    {
        var lim = Math.Clamp(limit, 1, 300);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT m.id, m.product_id, IFNULL(p.code, ''), IFNULL(p.name, ''),
                   m.movement_type, m.quantity, m.unit_price, m.notes, m.created_at
            FROM movements m
            LEFT JOIN products p ON p.id = m.product_id
            WHERE 1=1
            """;
        if (productId is int pid)
        {
            sql += " AND m.product_id = $pid";
            cmd.Parameters.AddWithValue("$pid", pid);
        }
        sql += " ORDER BY m.id DESC LIMIT $limit;";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$limit", lim);

        var rows = new List<StockMovementRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new StockMovementRow
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                ProductCode = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ProductName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MovementType = reader.GetString(4),
                Quantity = reader.GetDouble(5),
                UnitPrice = reader.GetDouble(6),
                Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt = reader.IsDBNull(8) ? "" : reader.GetString(8),
            });
        }
        return rows;
    }

    public static StockReportResult ListReport(StockReportKind kind, DateTime? dateFrom = null, DateTime? dateTo = null, int limit = 500)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.StockReport(kind, dateFrom, dateTo, limit);
        return ListReportLocal(kind, dateFrom, dateTo, limit);
    }

    public static StockReportResult ListReportLocal(StockReportKind kind, DateTime? dateFrom = null, DateTime? dateTo = null, int limit = 500)
    {
        return kind switch
        {
            StockReportKind.Negativo => ListStockFilter(kind,
                "(IFNULL(stock,0) + IFNULL(stock_fridge,0)) < 0 OR IFNULL(stock,0) < 0", limit),
            StockReportKind.Minimo => ListStockFilter(kind,
                "(IFNULL(stock,0) + IFNULL(stock_fridge,0)) <= IFNULL(min_stock,0)", limit),
            StockReportKind.FridgeRestock => ListFridgeRestock(limit),
            StockReportKind.Validade7d => ListValidade7d(limit),
            StockReportKind.MaisVendidos => ListRanking(kind, orderByQty: true, descending: true, dateFrom, dateTo, limit),
            StockReportKind.MenosVendidos => ListRanking(kind, orderByQty: true, descending: false, dateFrom, dateTo, limit),
            StockReportKind.MaisLucrativos => ListRanking(kind, orderByQty: false, descending: true, dateFrom, dateTo, limit),
            StockReportKind.MenosLucrativos => ListRanking(kind, orderByQty: false, descending: false, dateFrom, dateTo, limit),
            StockReportKind.ZeraNegativo => ListStockFilter(StockReportKind.Negativo,
                "IFNULL(stock,0) < 0 OR IFNULL(stock_fridge,0) < 0", limit),
            StockReportKind.CurvaAbc => ListCurvaAbc(dateFrom, dateTo, limit),
            _ => new StockReportResult { Kind = kind },
        };
    }

    public static string ReportTitle(StockReportKind kind) => kind switch
    {
        StockReportKind.Negativo => "Estoque Negativo",
        StockReportKind.Minimo => "Estoque Mínimo",
        StockReportKind.FridgeRestock => "Reposição Geladeira",
        StockReportKind.Validade7d => "Validade — próximos 7 dias",
        StockReportKind.MaisVendidos => "Mais Vendidos",
        StockReportKind.MenosVendidos => "Menos Vendidos",
        StockReportKind.MaisLucrativos => "Mais Lucrativos",
        StockReportKind.MenosLucrativos => "Menos Lucrativos",
        StockReportKind.ZeraNegativo => "Zera Estoque Negativo",
        StockReportKind.CurvaAbc => "Curva ABC",
        _ => "Relatório de Estoque",
    };

    private static StockReportResult ListFridgeRestock(int limit)
    {
        var lim = Math.Clamp(limit, 1, 500);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, IFNULL(code,''), IFNULL(name,''), IFNULL(group_name,''),
                   IFNULL(stock_fridge,0), IFNULL(stock_fridge_min,0), IFNULL(unit,'UN'),
                   IFNULL(location,''), IFNULL(cost_price,0), IFNULL(stock,0)
            FROM products
            WHERE IFNULL(active,1) = 1
              AND IFNULL(stock_fridge_min,0) > 0
              AND IFNULL(stock_fridge,0) <= IFNULL(stock_fridge_min,0)
            ORDER BY (IFNULL(stock_fridge_min,0) - IFNULL(stock_fridge,0)) DESC, name ASC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", lim);

        var rows = new List<StockReportRow>();
        double totalStock = 0, totalValor = 0;
        using var reader = cmd.ExecuteReader();
        var pos = 0;
        while (reader.Read())
        {
            pos++;
            var fridge = reader.GetDouble(4);
            var cost = reader.GetDouble(8);
            var warehouse = reader.GetDouble(9);
            var valor = ProductPriceCalculator.RoundPrice(fridge * cost);
            totalStock += fridge;
            totalValor += valor;
            var min = reader.GetDouble(5);
            var sugestao = Math.Max(0, Math.Ceiling(min - fridge));
            rows.Add(new StockReportRow
            {
                Posicao = pos,
                ProductId = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                GroupName = reader.GetString(3),
                Stock = fridge,
                MinStock = min,
                Unit = reader.GetString(6),
                Location = warehouse > 0.0001
                    ? $"Depósito: {warehouse:G} · sugerido: {sugestao:G}"
                    : "Depósito sem saldo",
                StockValue = valor,
            });
        }

        return new StockReportResult
        {
            Kind = StockReportKind.FridgeRestock,
            Rows = rows,
            Registros = rows.Count,
            TotalStock = totalStock,
            TotalValor = totalValor,
        };
    }

    private static StockReportResult ListStockFilter(StockReportKind kind, string whereExtra, int limit)
    {
        var lim = Math.Clamp(limit, 1, 500);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, IFNULL(code,''), IFNULL(name,''), IFNULL(group_name,''),
                   (IFNULL(stock,0) + IFNULL(stock_fridge,0)), IFNULL(min_stock,0), IFNULL(unit,'UN'),
                   IFNULL(location,''), IFNULL(cost_price,0)
            FROM products
            WHERE IFNULL(active,1) = 1 AND ({whereExtra})
            ORDER BY (IFNULL(stock,0) + IFNULL(stock_fridge,0)) ASC, name ASC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", lim);

        var rows = new List<StockReportRow>();
        double totalStock = 0, totalValor = 0;
        using var reader = cmd.ExecuteReader();
        var pos = 0;
        while (reader.Read())
        {
            pos++;
            var stock = reader.GetDouble(4);
            var cost = reader.GetDouble(8);
            var valor = ProductPriceCalculator.RoundPrice(stock * cost);
            totalStock += stock;
            totalValor += valor;
            rows.Add(new StockReportRow
            {
                Posicao = pos,
                ProductId = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                GroupName = reader.GetString(3),
                Stock = stock,
                MinStock = reader.GetDouble(5),
                Unit = reader.GetString(6),
                Location = reader.GetString(7),
                StockValue = valor,
            });
        }

        return new StockReportResult
        {
            Kind = kind,
            Rows = rows,
            Registros = rows.Count,
            TotalStock = Math.Round(totalStock, 3),
            TotalValor = ProductPriceCalculator.RoundPrice(totalValor),
        };
    }

    private static StockReportResult ListValidade7d(int limit)
    {
        var lim = Math.Clamp(limit, 1, 500);
        var today = DateTime.Today;
        var deadline = today.AddDays(7);
        var products = ProductService.List(ativo: "ativos");
        var matched = new List<(Product P, DateTime V)>();

        foreach (var p in products)
        {
            var validade = ParseValidade(p.ExtraJson);
            if (validade is null) continue;
            if (validade.Value >= today && validade.Value <= deadline)
                matched.Add((p, validade.Value));
        }

        matched.Sort((a, b) =>
        {
            var c = a.V.CompareTo(b.V);
            return c != 0 ? c : string.Compare(a.P.Name, b.P.Name, StringComparison.OrdinalIgnoreCase);
        });

        var rows = matched.Take(lim).Select((item, i) => new StockReportRow
        {
            Posicao = i + 1,
            ProductId = item.P.Id,
            Code = item.P.Code ?? "",
            Name = item.P.Name,
            GroupName = item.P.GroupName ?? "",
            Stock = item.P.Stock,
            MinStock = item.P.MinStock,
            Unit = item.P.Unit,
            Location = item.P.Location ?? "",
            StockValue = ProductPriceCalculator.RoundPrice(item.P.Stock * item.P.CostPrice),
            DataValidade = item.V.ToString("dd/MM/yyyy"),
            DiasValidade = (item.V - today).Days,
        }).ToList();

        return new StockReportResult
        {
            Kind = StockReportKind.Validade7d,
            Rows = rows,
            Registros = rows.Count,
            TotalStock = Math.Round(rows.Sum(r => r.Stock), 3),
            TotalValor = ProductPriceCalculator.RoundPrice(rows.Sum(r => r.StockValue)),
        };
    }

    private static StockReportResult ListRanking(
        StockReportKind kind,
        bool orderByQty,
        bool descending,
        DateTime? dateFrom,
        DateTime? dateTo,
        int limit)
    {
        var dFrom = (dateFrom ?? DateTime.Today.AddDays(-30)).Date;
        var dTo = (dateTo ?? DateTime.Today).Date;
        if (dFrom > dTo) (dFrom, dTo) = (dTo, dFrom);
        var lim = Math.Clamp(limit, 1, 500);

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.product_id,
                   IFNULL(si.product_code, ''),
                   IFNULL(si.product_name, ''),
                   IFNULL(p.group_name, ''),
                   si.quantity,
                   si.unit_price,
                   si.subtotal,
                   IFNULL(p.cost_price, 0),
                   IFNULL(p.extra_json, '')
            FROM sale_items si
            JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE s.cancelled = 0
              AND s.session_date >= $from
              AND s.session_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        var agg = new Dictionary<int, (string Code, string Name, string Group, double Qty, double Total, double Cost)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var pid = reader.GetInt32(0);
                var name = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var group = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var qty = reader.GetDouble(4);
                var unitSale = reader.GetDouble(5);
                var subtotal = reader.GetDouble(6);
                var catalogCost = reader.IsDBNull(7) ? 0 : reader.GetDouble(7);
                var extra = ProductExtra.Parse(reader.IsDBNull(8) ? null : reader.GetString(8));
                var unitCost = ProductPriceHelper.UnitCostForSoldLine(
                    catalogCost, unitSale, extra, name, group);
                var lineCost = ProductPriceHelper.RoundPrice(qty * unitCost);

                if (agg.TryGetValue(pid, out var prev))
                {
                    agg[pid] = (prev.Code, prev.Name, prev.Group,
                        prev.Qty + qty, prev.Total + subtotal, prev.Cost + lineCost);
                }
                else
                {
                    agg[pid] = (
                        reader.IsDBNull(1) ? "" : reader.GetString(1),
                        name, group, qty, subtotal, lineCost);
                }
            }
        }

        var ordered = orderByQty
            ? (descending
                ? agg.Select(ToRow).OrderByDescending(r => r.Qty).ThenByDescending(r => r.Total).ThenBy(r => r.Name)
                : agg.Select(ToRow).OrderBy(r => r.Qty).ThenByDescending(r => r.Total).ThenBy(r => r.Name))
            : (descending
                ? agg.Select(ToRow).OrderByDescending(r => r.Lucro).ThenByDescending(r => r.Total).ThenBy(r => r.Name)
                : agg.Select(ToRow).OrderBy(r => r.Lucro).ThenByDescending(r => r.Total).ThenBy(r => r.Name));

        (int Pid, string Code, string Name, string Group, double Qty, double Total, double Cost, double Lucro) ToRow(
            KeyValuePair<int, (string Code, string Name, string Group, double Qty, double Total, double Cost)> kv)
        {
            var lucro = ProductPriceHelper.RoundPrice(kv.Value.Total - kv.Value.Cost);
            return (kv.Key, kv.Value.Code, kv.Value.Name, kv.Value.Group,
                kv.Value.Qty, kv.Value.Total, kv.Value.Cost, lucro);
        }

        var rows = new List<StockReportRow>();
        double totalQty = 0, totalValor = 0, totalLucro = 0;
        var pos = 0;
        foreach (var item in ordered.Take(lim))
        {
            pos++;
            totalQty += item.Qty;
            totalValor += item.Total;
            totalLucro += item.Lucro;
            rows.Add(new StockReportRow
            {
                Posicao = pos,
                ProductId = item.Pid,
                Code = item.Code,
                Name = item.Name,
                GroupName = item.Group,
                Qty = ProductPriceHelper.RoundPrice(item.Qty),
                Total = ProductPriceHelper.RoundPrice(item.Total),
                CostTotal = ProductPriceHelper.RoundPrice(item.Cost),
                Lucro = ProductPriceHelper.RoundPrice(item.Lucro),
            });
        }

        return new StockReportResult
        {
            Kind = kind,
            Rows = rows,
            Registros = rows.Count,
            TotalQty = ProductPriceHelper.RoundPrice(totalQty),
            TotalValor = ProductPriceHelper.RoundPrice(totalValor),
            TotalLucro = ProductPriceHelper.RoundPrice(totalLucro),
            DateFrom = dFrom,
            DateTo = dTo,
        };
    }

    /// <summary>
    /// Curva ABC: ranqueia produtos vendidos no período pelo faturamento e classifica
    /// A (até 80% acumulado), B (até 95%) e C (restante). Também estima dias de estoque
    /// (saldo atual / venda média diária) e capital parado (saldo * custo).
    /// </summary>
    private static StockReportResult ListCurvaAbc(DateTime? dateFrom, DateTime? dateTo, int limit)
    {
        var dFrom = (dateFrom ?? DateTime.Today.AddDays(-30)).Date;
        var dTo = (dateTo ?? DateTime.Today).Date;
        if (dFrom > dTo) (dFrom, dTo) = (dTo, dFrom);
        var lim = Math.Clamp(limit, 1, 500);
        var daysInPeriod = Math.Max(1, (dTo - dFrom).Days + 1);

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.product_id,
                   IFNULL(si.product_code, ''),
                   IFNULL(si.product_name, ''),
                   IFNULL(p.group_name, ''),
                   IFNULL(p.unit, 'UN'),
                   IFNULL(p.location, ''),
                   IFNULL(p.stock, 0),
                   IFNULL(p.cost_price, 0),
                   SUM(si.quantity) AS qty,
                   SUM(si.subtotal) AS total
            FROM sale_items si
            JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE s.cancelled = 0
              AND s.session_date >= $from
              AND s.session_date <= $to
            GROUP BY si.product_id, si.product_code, si.product_name, p.group_name
            ORDER BY total DESC, si.product_name ASC;
            """;
        cmd.Parameters.AddWithValue("$from", dFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", dTo.ToString("yyyy-MM-dd"));

        var raw = new List<(int ProductId, string Code, string Name, string Group, string Unit,
            string Location, double Stock, double Cost, double Qty, double Total)>();
        double grandTotal = 0;
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var total = reader.GetDouble(9);
                grandTotal += total;
                raw.Add((
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetDouble(6), reader.GetDouble(7),
                    reader.GetDouble(8), total));
            }
        }

        var rows = new List<StockReportRow>();
        double cumulative = 0;
        double totalStock = 0, totalValor = 0, totalCapital = 0;
        var pos = 0;
        foreach (var item in raw.Take(lim))
        {
            pos++;
            cumulative += item.Total;
            var cumulativePercent = grandTotal > 0 ? cumulative / grandTotal * 100.0 : 0;
            var abcClass = cumulativePercent <= 80.0 ? "A" : cumulativePercent <= 95.0 ? "B" : "C";

            var avgDaily = ProductPriceHelper.RoundPrice(item.Qty / daysInPeriod);
            double? daysOfStock = avgDaily > 0.0009 ? Math.Round(item.Stock / avgDaily, 1) : null;
            var capital = ProductPriceCalculator.RoundPrice(item.Stock * item.Cost);
            var stockValue = capital;

            totalStock += item.Stock;
            totalValor += stockValue;
            totalCapital += capital;

            rows.Add(new StockReportRow
            {
                Posicao = pos,
                ProductId = item.ProductId,
                Code = item.Code,
                Name = item.Name,
                GroupName = item.Group,
                Stock = item.Stock,
                Unit = item.Unit,
                Location = item.Location,
                StockValue = stockValue,
                Qty = ProductPriceHelper.RoundPrice(item.Qty),
                Total = ProductPriceHelper.RoundPrice(item.Total),
                CostTotal = ProductPriceCalculator.RoundPrice(item.Qty * item.Cost),
                AbcClass = abcClass,
                DaysOfStock = daysOfStock,
                CapitalParado = capital,
                AvgDailySales = avgDaily,
            });
        }

        return new StockReportResult
        {
            Kind = StockReportKind.CurvaAbc,
            Rows = rows,
            Registros = rows.Count,
            TotalStock = Math.Round(totalStock, 3),
            TotalValor = ProductPriceCalculator.RoundPrice(totalValor),
            TotalQty = ProductPriceHelper.RoundPrice(rows.Sum(r => r.Qty)),
            TotalLucro = ProductPriceCalculator.RoundPrice(totalCapital),
            DateFrom = dFrom,
            DateTo = dTo,
        };
    }

    private static DateTime? ParseValidade(string extraJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(extraJson) ? "{}" : extraJson);
            if (!doc.RootElement.TryGetProperty("data_validade", out var el))
                return null;
            var text = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            if (DateTime.TryParseExact(text, ["dd/MM/yyyy", "yyyy-MM-dd"],
                    CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var d))
                return d.Date;
            if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out d))
                return d.Date;
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static ProductStock? GetProduct(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, IFNULL(stock,0), IFNULL(cost_price,0) FROM products WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new ProductStock(reader.GetInt32(0), reader.GetDouble(1), reader.GetDouble(2));
    }

    private static ProductStockExtra? GetProductWithExtra(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, IFNULL(stock,0), IFNULL(cost_price,0), IFNULL(extra_json,''),
                   IFNULL(name,''), IFNULL(stock_fridge,0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new ProductStockExtra(
            reader.GetInt32(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.IsDBNull(3) ? "" : reader.GetString(3),
            reader.IsDBNull(4) ? "" : reader.GetString(4),
            reader.GetDouble(5));
    }

    private static ProductFridgeStock? GetProductFridge(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, IFNULL(stock,0), IFNULL(cost_price,0),
                   IFNULL(stock_fridge,0), IFNULL(stock_fridge_min,0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new ProductFridgeStock(
            reader.GetInt32(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            Convert.ToInt32(reader.GetValue(4)));
    }

    /// <summary>Registra movimentação de estoque (uso interno por compras/outros módulos).</summary>
    public static int RegisterMovement(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        int productId, string type, double qty, double unitPrice, string? notes,
        double? stockBefore = null, double? stockAfter = null,
        string? operation = null, string? unit = null,
        string? refType = null, int? refId = null)
        => InsertMovement(conn, tx, productId, type, qty, unitPrice, notes,
            stockBefore, stockAfter, operation, unit, refType, refId);

    private static int InsertMovement(
        SqliteConnection conn, SqliteTransaction tx,
        int productId, string type, double qty, double unitPrice, string? notes,
        double? stockBefore = null, double? stockAfter = null,
        string? operation = null, string? unit = null,
        string? refType = null, int? refId = null)
    {
        var noteVal = string.IsNullOrWhiteSpace(notes)
            ? null
            : notes.Trim()[..Math.Min(500, notes.Trim().Length)];
        var user = AppSession.CurrentUser is null
            ? "Sistema"
            : (AppSession.CurrentUser.Nome ?? AppSession.CurrentUser.Login ?? "Sistema");
        var unitVal = unit;
        if (string.IsNullOrWhiteSpace(unitVal))
            unitVal = LookupUnit(conn, tx, productId);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at,
              stock_before, stock_after, operation, user_name, unit, ref_type, ref_id
            ) VALUES (
              $pid, $type, $qty, $price, $notes, datetime('now','localtime'),
              $before, $after, $op, $user, $unit, $refType, $refId
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$qty", qty);
        cmd.Parameters.AddWithValue("$price", unitPrice);
        cmd.Parameters.AddWithValue("$notes", (object?)noteVal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$before", stockBefore is double b ? b : DBNull.Value);
        cmd.Parameters.AddWithValue("$after", stockAfter is double a ? a : DBNull.Value);
        cmd.Parameters.AddWithValue("$op", (object?)operation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$user", user);
        cmd.Parameters.AddWithValue("$unit", (object?)unitVal ?? "UN");
        cmd.Parameters.AddWithValue("$refType", (object?)refType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$refId", refId is int rid ? rid : DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string LookupUnit(SqliteConnection conn, SqliteTransaction tx, int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT IFNULL(unit,'UN') FROM products WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "UN";
    }

    private static double Round4(double v) => Math.Round(v, 4);

    private sealed record ProductStock(int Id, double Stock, double CostPrice);
    private sealed record ProductStockExtra(
        int Id, double Stock, double CostPrice, string ExtraJson, string Name, double StockFridge);
    private sealed record ProductFridgeStock(
        int Id, double Stock, double CostPrice, double StockFridge, int StockFridgeMin);
}
