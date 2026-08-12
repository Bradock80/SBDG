using System.IO;
using SGDB.Services;

namespace SGDB.Tests.Infrastructure;

/// <summary>
/// Banco SQLite temporário exclusivo por teste.
/// Usa DatabaseService.Initialize(path) — nunca o deposito.db da loja.
/// Parte de arquivo totalmente vazio (sem pré-criar tabelas).
/// </summary>
public sealed class TempDatabase : IDisposable
{
    public string DatabasePath { get; }
    private bool _disposed;

    private TempDatabase(string databasePath)
    {
        DatabasePath = databasePath;
    }

    public static TempDatabase Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SGDB.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.db");
        DatabaseService.Initialize(path);
        return new TempDatabase(path);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Fecha qualquer handle residual antes de apagar WAL/SHM.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        TryDelete(DatabasePath);
        TryDelete(DatabasePath + "-wal");
        TryDelete(DatabasePath + "-shm");
        TryDelete(DatabasePath + "-journal");

        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            try
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                    Directory.Delete(dir);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static void TryDelete(string path)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
    }
}
