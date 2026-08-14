using Microsoft.Data.Sqlite;

namespace SGDB.Services;

public sealed class PixIntent
{
    public int Id { get; init; }
    public int? SaleId { get; init; }
    public long MpPaymentId { get; init; }
    public string? IdempotencyKey { get; init; }
    public double Amount { get; init; }
    public string Status { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public string? ApprovedAt { get; init; }
    public string? CancelledAt { get; init; }
    public string? RefundedAt { get; init; }
    public string? LastError { get; init; }
}

public static class PixIntentService
{
    public static int Create(long mpPaymentId, double amount, string? idempotencyKey, string status)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pix_intents (mp_payment_id, amount, idempotency_key, status)
            VALUES ($mp, $amount, $key, $status);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$mp", mpPaymentId);
        cmd.Parameters.AddWithValue("$amount", amount);
        cmd.Parameters.AddWithValue("$key", (object?)idempotencyKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", status.Trim().ToLowerInvariant());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void MarkApproved(long mpPaymentId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE pix_intents
            SET status = 'approved',
                approved_at = COALESCE(approved_at, datetime('now','localtime')),
                last_error = NULL
            WHERE mp_payment_id = $mp;
            """;
        cmd.Parameters.AddWithValue("$mp", mpPaymentId);
        cmd.ExecuteNonQuery();
    }

    public static void MarkCancelled(long mpPaymentId, string? error = null) =>
        Stamp(mpPaymentId, "cancelled", cancelled: true, error: Sanitize(error));

    public static void MarkRefunded(long mpPaymentId, string? error = null) =>
        Stamp(mpPaymentId, "refunded", refunded: true, error: Sanitize(error));

    public static void MarkRefundPending(long mpPaymentId, string? error)
    {
        Stamp(mpPaymentId, "refund_pending", error: Sanitize(error));
    }

    public static void MarkStatus(long mpPaymentId, string status, string? error = null) =>
        Stamp(mpPaymentId, status, error: Sanitize(error));

    public static void AttachSale(long mpPaymentId, int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE pix_intents SET sale_id = $sale WHERE mp_payment_id = $mp;";
        cmd.Parameters.AddWithValue("$sale", saleId);
        cmd.Parameters.AddWithValue("$mp", mpPaymentId);
        cmd.ExecuteNonQuery();
    }

    public static PixIntent? GetBySaleId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, sale_id, mp_payment_id, idempotency_key, amount, status,
                   created_at, approved_at, cancelled_at, refunded_at, last_error
            FROM pix_intents WHERE sale_id = $sale
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$sale", saleId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return ReadIntent(reader);
    }

    public static PixIntent? GetByMpPaymentId(long mpPaymentId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, sale_id, mp_payment_id, idempotency_key, amount, status,
                   created_at, approved_at, cancelled_at, refunded_at, last_error
            FROM pix_intents WHERE mp_payment_id = $mp LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$mp", mpPaymentId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return ReadIntent(reader);
    }

    private static PixIntent ReadIntent(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        SaleId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
        MpPaymentId = reader.GetInt64(2),
        IdempotencyKey = reader.IsDBNull(3) ? null : reader.GetString(3),
        Amount = reader.GetDouble(4),
        Status = reader.GetString(5),
        CreatedAt = reader.IsDBNull(6) ? "" : reader.GetString(6),
        ApprovedAt = reader.IsDBNull(7) ? null : reader.GetString(7),
        CancelledAt = reader.IsDBNull(8) ? null : reader.GetString(8),
        RefundedAt = reader.IsDBNull(9) ? null : reader.GetString(9),
        LastError = reader.IsDBNull(10) ? null : reader.GetString(10),
    };

    public static string Sanitize(string? error)
    {
        var s = (error ?? "").Trim();
        if (s.Length == 0)
            return "";
        if (s.Contains("APP_USR", StringComparison.OrdinalIgnoreCase)
            || s.Contains("TEST-", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Bearer", StringComparison.OrdinalIgnoreCase)
            || s.Contains("access_token", StringComparison.OrdinalIgnoreCase))
            return "Falha no Mercado Pago.";
        return s.Length > 240 ? s[..240] : s;
    }

    private static void Stamp(long mpPaymentId, string status, bool cancelled = false, bool refunded = false, string? error = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE pix_intents SET
                status = $status,
                cancelled_at = CASE WHEN $cancel = 1 THEN datetime('now','localtime') ELSE cancelled_at END,
                refunded_at = CASE WHEN $refund = 1 THEN datetime('now','localtime') ELSE refunded_at END,
                last_error = $err
            WHERE mp_payment_id = $mp;
            """;
        cmd.Parameters.AddWithValue("$status", status.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$cancel", cancelled ? 1 : 0);
        cmd.Parameters.AddWithValue("$refund", refunded ? 1 : 0);
        cmd.Parameters.AddWithValue("$err", string.IsNullOrWhiteSpace(error) ? DBNull.Value : error.Trim());
        cmd.Parameters.AddWithValue("$mp", mpPaymentId);
        cmd.ExecuteNonQuery();
    }
}
