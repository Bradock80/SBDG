using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class DatabaseServiceTests
{
    [Fact]
    public void Initialize_BancoTotalmenteVazio_CriaSchemaCompleto()
    {
        using var db = TempDatabase.Create();

        Assert.True(File.Exists(db.DatabasePath));
        Assert.Equal(db.DatabasePath, DatabaseService.DatabasePath);

        using var conn = DatabaseService.OpenConnection();
        Assert.Equal(ConnectionState.Open, conn.State);
        Assert.True(TableExists(conn, "users"));
        Assert.True(TableExists(conn, "products"));
        Assert.True(ForeignKeysEnabled(conn));
    }

    [Fact]
    public void Initialize_CriaSchemaEPermiteAbrirConexao()
    {
        using var db = TempDatabase.Create();

        Assert.True(File.Exists(db.DatabasePath));
        Assert.Equal(db.DatabasePath, DatabaseService.DatabasePath);

        using var conn = DatabaseService.OpenConnection();
        Assert.Equal(ConnectionState.Open, conn.State);

        Assert.True(TableExists(conn, "products"));
        Assert.True(ForeignKeysEnabled(conn));
    }

    [Fact]
    public void Initialize_CriaTabelasPrincipaisDoSchema()
    {
        using var db = TempDatabase.Create();
        using var conn = DatabaseService.OpenConnection();

        // Subconjunto estável do schema atual (caracterização, não contrato fechado).
        string[] expected =
        [
            "products",
            "users",
            "people",
            "sales",
            "sale_items",
            "cash_sessions",
            "cash_movements",
            "purchases",
            "purchase_items",
            "movements",
            "open_tabs",
            "open_tab_items",
            "fiado_payments",
            "audit_log",
            "app_settings",
        ];

        foreach (var table in expected)
            Assert.True(TableExists(conn, table), $"Tabela esperada ausente: {table}");
    }

    [Fact]
    public void OpenConnection_CadaChamadaRetornaConexaoUsavel()
    {
        using var db = TempDatabase.Create();

        using (var c1 = DatabaseService.OpenConnection())
        using (var cmd = c1.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM products;";
            Assert.Equal(0L, (long)(cmd.ExecuteScalar() ?? -1));
        }

        using (var c2 = DatabaseService.OpenConnection())
        using (var cmd = c2.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys;";
            Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0));
        }
    }

    [Fact]
    public void Initialize_BancoJaInicializado_PodeExecutarNovamente()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SGDB.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.db");

        try
        {
            DatabaseService.Initialize(path);
            Assert.True(File.Exists(path));

            string[] tables;
            using (var conn = DatabaseService.OpenConnection())
            {
                Assert.True(TableExists(conn, "users"));
                Assert.True(TableExists(conn, "products"));
                Assert.True(ForeignKeysEnabled(conn));
                tables = ListUserTables(conn);
            }

            // Segunda inicialização no mesmo arquivo (simula reopen / reentrada).
            DatabaseService.Initialize(path);

            using (var conn = DatabaseService.OpenConnection())
            {
                Assert.Equal(ConnectionState.Open, conn.State);
                Assert.True(TableExists(conn, "users"));
                Assert.True(TableExists(conn, "products"));
                Assert.True(ForeignKeysEnabled(conn));
                Assert.Equal(tables, ListUserTables(conn));
            }
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                /* ignore cleanup races on Windows */
            }
        }
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM sqlite_master
            WHERE type = 'table' AND name = $name
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$name", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static bool ForeignKeysEnabled(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys;";
        var value = cmd.ExecuteScalar();
        return value is long l && l == 1
               || value is int i && i == 1
               || Convert.ToInt32(value ?? 0) == 1;
    }

    private static string[] ListUserTables(SqliteConnection conn)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list.ToArray();
    }
}
