using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Fechamento atômico de deck: venda + estoque + caixa + settled numa única transação.
/// </summary>
public static class OpenTabSettlementService
{
    /// <summary>
    /// Finaliza a venda do deck e marca o open_tab como settled no mesmo COMMIT.
    /// Se MarkSettled falhar após gravar a venda na TX, o rollback desfaz tudo.
    /// </summary>
    public static PdvFinalizeResult SettleOpenTab(
        int tabId,
        PdvFinalizeRequest request,
        DateTime? sessionDate = null)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("fechar deck");
        var d = (sessionDate ?? DateTime.Today).Date;

        using var conn = DatabaseService.OpenConnection();
        CashService.RequireOperational(conn, d);

        using var tx = conn.BeginTransaction();

        // Proteção de duplo fechamento antes de gravar venda (mensagem clara, sem trabalho parcial).
        EnsureTabNotAlreadySettled(conn, tx, tabId);

        var result = PdvService.FinalizeSaleCore(conn, tx, request, d);
        OpenTabService.MarkSettledCore(conn, tx, tabId, result.SaleId);

        tx.Commit();
        OpenTabService.RaiseOpenTabsChanged();
        return result;
    }

    private static void EnsureTabNotAlreadySettled(
        SqliteConnection conn, SqliteTransaction tx, int tabId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT status FROM open_tabs WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", tabId);
        var status = cmd.ExecuteScalar() as string;
        if (status is null)
            throw new OpenTabException("Deck não encontrado.");
        if (string.Equals(status, "settled", StringComparison.OrdinalIgnoreCase))
            throw new OpenTabException("Este deck já foi fechado.");
    }
}
