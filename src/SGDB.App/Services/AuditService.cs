using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

public sealed class AuditQuery
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public string? Search { get; init; }
    public string? UserLogin { get; init; }
    public string? ActionFilter { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 25;
}

public static class AuditService
{
    public static void Log(string action, string entity, string? entityId = null, string? details = null)
    {
        try
        {
            using var conn = DatabaseService.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO audit_log (user_login, user_name, action, entity, entity_id, details)
                VALUES ($login, $nome, $action, $entity, $eid, $details);
                """;
            cmd.Parameters.AddWithValue("$login", AppSession.UserLogin);
            cmd.Parameters.AddWithValue("$nome", AppSession.CurrentUser?.Nome ?? "Sistema");
            cmd.Parameters.AddWithValue("$action", (action ?? "").Trim());
            cmd.Parameters.AddWithValue("$entity", (entity ?? "").Trim());
            cmd.Parameters.AddWithValue("$eid", (object?)entityId?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$details", (object?)details?.Trim() ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // auditoria nunca derruba a operação principal
        }
    }

    public static void LogJson(string action, string entity, string? entityId, object payload, string summary) =>
        Log(action, entity, entityId, AuditPayloadBuilder.Serialize(summary, payload));

    /// <summary>
    /// Auditoria na mesma transação da operação. Se a inserção falhar, a mutação deve dar rollback.
    /// Não engole exceção — diferente de <see cref="Log"/>, que nunca derruba o fluxo principal.
    /// </summary>
    public static void LogJson(
        SqliteConnection conn,
        SqliteTransaction tx,
        string action,
        string entity,
        string? entityId,
        object payload,
        string summary)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO audit_log (user_login, user_name, action, entity, entity_id, details)
            VALUES ($login, $nome, $action, $entity, $eid, $details);
            """;
        cmd.Parameters.AddWithValue("$login", AppSession.UserLogin);
        cmd.Parameters.AddWithValue("$nome", AppSession.CurrentUser?.Nome ?? "Sistema");
        cmd.Parameters.AddWithValue("$action", (action ?? "").Trim());
        cmd.Parameters.AddWithValue("$entity", (entity ?? "").Trim());
        cmd.Parameters.AddWithValue("$eid", (object?)entityId?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$details", AuditPayloadBuilder.Serialize(summary, payload));
        cmd.ExecuteNonQuery();
    }

    public static int Count(AuditQuery query)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSql(query, countOnly: true);
        ApplyParameters(cmd, query, countOnly: true);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : Convert.ToInt32(result ?? 0);
    }

    public static IReadOnlyList<AuditLogRow> List(AuditQuery query)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSql(query, countOnly: false);
        ApplyParameters(cmd, query, countOnly: false);

        var list = new List<AuditLogRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadRow(reader));
        return list;
    }

    public static IReadOnlyList<AuditLogRow> List(
        DateTime? from = null,
        DateTime? to = null,
        string? search = null,
        int limit = 500) =>
        List(new AuditQuery { From = from, To = to, Search = search, Limit = limit });

    public static IReadOnlyList<AuditUserFilterOption> ListUserFilters()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT IFNULL(user_login,''), IFNULL(user_name,'')
            FROM audit_log
            WHERE user_login IS NOT NULL AND user_login != ''
            ORDER BY user_name COLLATE NOCASE;
            """;

        var list = new List<AuditUserFilterOption> { AuditUserFilterOption.All };
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var login = reader.GetString(0);
            var name = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(login))
                continue;
            list.Add(new AuditUserFilterOption(login, string.IsNullOrWhiteSpace(name) ? login : name));
        }
        return list;
    }

    public static IReadOnlyList<AuditLogRow> ListForExport(AuditQuery query, int maxRows = 10000)
    {
        var q = new AuditQuery
        {
            From = query.From,
            To = query.To,
            Search = query.Search,
            UserLogin = query.UserLogin,
            ActionFilter = query.ActionFilter,
            Offset = 0,
            Limit = Math.Clamp(maxRows, 1, 10000),
        };
        return List(q);
    }

    private static void ApplyParameters(SqliteCommand cmd, AuditQuery query, bool countOnly)
    {
        if (query.From is DateTime df)
            cmd.Parameters.AddWithValue("$from", df.ToString("yyyy-MM-dd"));
        if (query.To is DateTime dt)
            cmd.Parameters.AddWithValue("$to", dt.ToString("yyyy-MM-dd"));
        if (!string.IsNullOrWhiteSpace(query.Search))
            cmd.Parameters.AddWithValue("$q", "%" + query.Search.Trim() + "%");
        if (!string.IsNullOrWhiteSpace(query.UserLogin))
            cmd.Parameters.AddWithValue("$user", query.UserLogin.Trim());
        if (!countOnly)
        {
            cmd.Parameters.AddWithValue("$lim", Math.Clamp(query.Limit, 1, 500));
            cmd.Parameters.AddWithValue("$off", Math.Max(0, query.Offset));
        }
    }

    private static string BuildSql(AuditQuery query, bool countOnly)
    {
        var select = countOnly
            ? "SELECT COUNT(*)"
            : """
              SELECT id, created_at, IFNULL(user_login,''), IFNULL(user_name,''),
                     action, entity, entity_id, IFNULL(details,'')
              """;

        var sql = select + """
            FROM audit_log
            WHERE 1=1
            """;

        if (query.From is DateTime)
            sql += " AND date(created_at) >= $from";
        if (query.To is DateTime)
            sql += " AND date(created_at) <= $to";
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            sql += """
                 AND (
                    user_login LIKE $q OR user_name LIKE $q OR action LIKE $q
                    OR entity LIKE $q OR IFNULL(details,'') LIKE $q OR IFNULL(entity_id,'') LIKE $q
                 )
                """;
        }
        if (!string.IsNullOrWhiteSpace(query.UserLogin))
            sql += " AND user_login = $user";

        sql += BuildActionFilterSql(query.ActionFilter);

        if (!countOnly)
            sql += " ORDER BY id DESC LIMIT $lim OFFSET $off;";

        return sql;
    }

    private static string BuildActionFilterSql(string? filterKey)
    {
        return (filterKey ?? "").Trim().ToLowerInvariant() switch
        {
            "cancel_venda" => " AND ((action = 'cancelar' AND entity = 'venda') OR (action = 'remover' AND entity = 'item'))",
            "desconto" => """
                 AND (
                    action LIKE '%desconto%'
                    OR IFNULL(details,'') LIKE '%desconto%'
                    OR IFNULL(details,'') LIKE '%Desconto%'
                 )
                """,
            "caixa_mov" => " AND action IN ('sangria','suprimento')",
            "caixa_sessao" => " AND action IN ('abrir','fechar','abertura','fechamento')",
            "estoque_cadastro" => """
                 AND (
                    (action = 'alterar' AND entity = 'produto')
                    OR (action = 'entrada' AND entity = 'compra')
                    OR (action IN ('criar','alterar') AND entity IN ('cliente','fornecedor','pessoa'))
                 )
                """,
            "login_logout" => " AND action IN ('login','logout')",
            _ => "",
        };
    }

    private static AuditLogRow ReadRow(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetInt64(0),
            CreatedAt = reader.GetString(1),
            UserLogin = reader.GetString(2),
            UserName = reader.GetString(3),
            Action = reader.GetString(4),
            Entity = reader.GetString(5),
            EntityId = reader.IsDBNull(6) ? null : reader.GetString(6),
            Details = reader.GetString(7),
        };
}
