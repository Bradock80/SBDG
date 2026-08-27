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

    public const string DatabasePathEnvVar = "SGDB_DATABASE_PATH";

    public static string DefaultStoreDatabasePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SGDB",
            "deposito.db");

    /// <summary>
    /// Caminho de abertura do app. <c>SGDB_DATABASE_PATH</c> (processo) tem prioridade
    /// e nunca é ignorado em favor do banco padrão da loja.
    /// EXE em pasta SGDB_TESTE* sem a variável recusa abrir (não cai no banco real).
    /// </summary>
    public static string ResolveStartupDatabasePath() =>
        ResolveStartupDatabasePath(
            Environment.GetEnvironmentVariable(DatabasePathEnvVar),
            AppContext.BaseDirectory);

    public static string ResolveStartupDatabasePath(string? envPath, string? baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(envPath))
            return Path.GetFullPath(envPath.Trim());

        if (!string.IsNullOrWhiteSpace(baseDirectory)
            && baseDirectory.Contains("SGDB_TESTE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Este executável de teste exige SGDB_DATABASE_PATH. Use iniciar_sgdb_teste.cmd.");
        }

        return DefaultStoreDatabasePath;
    }

    public static bool IsIsolatedDatabasePath(string? databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            return false;
        try
        {
            return !string.Equals(
                Path.GetFullPath(databasePath),
                Path.GetFullPath(DefaultStoreDatabasePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    public static void Initialize()
    {
        var path = ResolveStartupDatabasePath();
        var dataDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dataDir))
            Directory.CreateDirectory(dataDir);
        Initialize(path);
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
        // OpenConnection pode ter rodado migrações antes das CREATE TABLE
        // em banco novo. Reaplica para colunas como users.permissions_json.
        _migrationsDone = false;
        ApplyPendingMigrations(conn);
        _migrationsDone = true;
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
