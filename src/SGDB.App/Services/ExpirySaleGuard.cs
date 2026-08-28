using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// 70I-B1 — motor compartilhado: quantidade comprovadamente vencida no depósito
/// não participa de saída normal (venda / transferência dep→gel).
/// Uninformed, untracked e geladeira NÃO são tratados como vencidos.
/// Não altera products.stock por conta própria; apenas decide e valida.
/// A barreira compara a PARTE FÍSICA da saída com a capacidade física
/// não vencida. requested &gt; stock não é, por si, prova de vencido.
/// </summary>
public static class ExpirySaleGuard
{
    private static double Tol => StockLotConsistencyService.Tolerance;

    /// <summary>
    /// Calcula decisão para a quantidade de depósito solicitada.
    /// Deve ser chamado com a mesma connection/transaction da mutação.
    /// </summary>
    public static ExpirySaleDecision Evaluate(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        double requestedWarehouseQty,
        DateTime? today = null)
    {
        requestedWarehouseQty = Math.Round(Math.Abs(requestedWarehouseQty), 4);
        var day = (today ?? DateTime.Today).Date;

        var (stock, fridge) = LoadProductStocks(conn, tx, productId);
        var (tracked, expired, valid, uninformed) = AggregateLots(conn, tx, productId, day);

        var untracked = Math.Max(Round4(stock - tracked), 0);
        var stockPositiveQty = Math.Max(stock, 0);
        var requestedPhysical = Round4(Math.Min(requestedWarehouseQty, stockPositiveQty));
        var nonExpiredCoverage = Round4(valid + uninformed + untracked);
        // Capacidade física conhecida não vencida (UI / sellable). Não inclui o
        // excedente que só iria a saldo negativo.
        var nonExpiredPhysicalCapacity = Round4(Math.Min(stockPositiveQty, nonExpiredCoverage));
        var sellable = nonExpiredPhysicalCapacity;

        // Bloqueio 70I: a PARTE FÍSICA da saída ultrapassa o que o depósito
        // consegue explicar como não vencido. O restante acima de stock é
        // política histórica de negativo, não prova de consumo de vencido.
        var blockedQty = Math.Max(Round4(requestedPhysical - nonExpiredPhysicalCapacity), 0);
        var hasExpired = expired > Tol;
        var hasUntracked = untracked > Tol;
        var hasUninformed = uninformed > Tol;

        var physicalNeedsExpired = requestedPhysical > nonExpiredPhysicalCapacity + Tol;
        var isBlocked = stockPositiveQty > Tol
            && hasExpired
            && physicalNeedsExpired
            && requestedWarehouseQty > Tol;

        var reason = "";
        var errorCode = "";
        if (isBlocked)
        {
            errorCode = ExpirySaleRules.InsufficientNonExpired;
            reason = ExpirySaleRules.InsufficientNonExpiredMessage;
        }

        return new ExpirySaleDecision
        {
            ProductId = productId,
            WarehouseStock = stock,
            FridgeStock = fridge,
            TrackedQty = tracked,
            ExpiredQty = expired,
            ValidQty = valid,
            UninformedQty = uninformed,
            UntrackedQty = untracked,
            RequestedWarehouseQty = requestedWarehouseQty,
            SellableWarehouseQty = sellable,
            BlockedQty = isBlocked ? blockedQty : 0,
            HasExpiredStock = hasExpired,
            HasUntrackedStock = hasUntracked,
            HasUninformedExpiry = hasUninformed,
            IsBlocked = isBlocked,
            Reason = reason,
            ErrorCode = errorCode,
        };
    }

    /// <summary>
    /// Valida saída de depósito. Lança <see cref="ExpirySaleException"/> se bloqueado.
    /// </summary>
    public static ExpirySaleDecision EnsureWarehouseSellable(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        double requestedWarehouseQty,
        string? errorCodeOverride = null,
        string? messageOverride = null,
        DateTime? today = null)
    {
        var decision = Evaluate(conn, tx, productId, requestedWarehouseQty, today);
        if (!decision.IsBlocked)
            return decision;

        var code = string.IsNullOrWhiteSpace(errorCodeOverride)
            ? decision.ErrorCode
            : errorCodeOverride!;
        var message = string.IsNullOrWhiteSpace(messageOverride)
            ? decision.Reason
            : messageOverride!;
        throw new ExpirySaleException(code, message, decision);
    }

    private static (double Stock, double Fridge) LoadProductStocks(
        SqliteConnection conn, SqliteTransaction tx, int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT IFNULL(stock,0), IFNULL(stock_fridge,0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return (0, 0);
        return (Round4(reader.GetDouble(0)), Round4(reader.GetDouble(1)));
    }

    /// <summary>
    /// Uma leitura agregada por produto. Quantidades &lt;= tolerância não entram.
    /// </summary>
    private static (double Tracked, double Expired, double Valid, double Uninformed) AggregateLots(
        SqliteConnection conn, SqliteTransaction tx, int productId, DateTime today)
    {
        var todayIso = today.ToString("yyyy-MM-dd");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT
              IFNULL(SUM(CASE WHEN quantity > $tol THEN quantity ELSE 0 END), 0),
              IFNULL(SUM(CASE
                  WHEN quantity > $tol
                   AND expiry_date IS NOT NULL AND TRIM(expiry_date) <> ''
                   AND expiry_date < $today
                  THEN quantity ELSE 0 END), 0),
              IFNULL(SUM(CASE
                  WHEN quantity > $tol
                   AND expiry_date IS NOT NULL AND TRIM(expiry_date) <> ''
                   AND expiry_date >= $today
                  THEN quantity ELSE 0 END), 0),
              IFNULL(SUM(CASE
                  WHEN quantity > $tol
                   AND (expiry_date IS NULL OR TRIM(expiry_date) = '')
                  THEN quantity ELSE 0 END), 0)
            FROM product_lots
            WHERE product_id = $pid;
            """;
        cmd.Parameters.AddWithValue("$tol", Tol);
        cmd.Parameters.AddWithValue("$today", todayIso);
        cmd.Parameters.AddWithValue("$pid", productId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return (0, 0, 0, 0);
        return (
            Round4(reader.GetDouble(0)),
            Round4(reader.GetDouble(1)),
            Round4(reader.GetDouble(2)),
            Round4(reader.GetDouble(3)));
    }

    private static double Round4(double v) => Math.Round(v, 4);
}
