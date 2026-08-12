using System.IO;
using Microsoft.Data.Sqlite;

namespace SGDB.Services;

public static partial class DatabaseService
{
    private static string? _connectionString;
    private static readonly object MigrationLock = new();
    private static bool _migrationsDone;

    public static string DatabasePath { get; private set; } = "";

    public static string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("Banco não inicializado.");

    public static void Initialize()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SGDB");

        Directory.CreateDirectory(dataDir);
        Initialize(Path.Combine(dataDir, "deposito.db"));
    }

    /// <summary>Inicializa o banco em um caminho explícito (ex.: cópia para testes).</summary>
    public static void Initialize(string databasePath)
    {
        DatabasePath = databasePath;
        var dataDir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(dataDir))
            Directory.CreateDirectory(dataDir);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
        };
        _connectionString = builder.ConnectionString;
        _migrationsDone = false;

        using var conn = OpenConnection();
        EnsureSchema(conn);
        SeedDefaultAdmin(conn);
    }

    public static SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        try
        {
            using var wal = conn.CreateCommand();
            wal.CommandText = "PRAGMA journal_mode=WAL;";
            wal.ExecuteNonQuery();
        }
        catch { /* WAL opcional */ }

        // Migrações só na 1ª abertura — rodá-las em toda conexão travava o UI
        // (UPDATE/PRAGMA sob lock enquanto outro reader ainda estava aberto).
        if (!_migrationsDone)
        {
            lock (MigrationLock)
            {
                if (!_migrationsDone)
                {
                    ApplyPendingMigrations(conn);
                    _migrationsDone = true;
                }
            }
        }

        return conn;
    }
}
