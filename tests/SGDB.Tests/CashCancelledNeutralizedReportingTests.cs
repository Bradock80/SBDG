using SGDB.Domain.Finance;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69H — venda cancelada + troca integral não polui KPI/fechamento;
/// saldo e histórico permanecem. Não altera o banco da loja.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CashCancelledNeutralizedReportingTests
{
    public const double IncidentAmount = 59_224_415_254_560d;
    private const double OpeningLikeStore = 893.69;

    [Fact]
    public void Caso25338_KpisSemTrilhoes_SaldoEHistoricoPreservados()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(OpeningLikeStore, "loja");

        InsertCancelledSaleWithIntegralExchange(IncidentAmount);

        var view = CashService.GetOperacaoView();
        Assert.True(view.VendasDiaPdv < 1_000, $"Vendas PDV poluída: {view.VendasDiaPdv}");
        Assert.True(view.EntradasCaixa < 1_000, $"Entradas poluída: {view.EntradasCaixa}");
        Assert.True(view.SaidasCaixa < 1_000, $"Saídas poluída: {view.SaidasCaixa}");
        Assert.True(view.EntradasPorForma.Values.All(v => v < 1_000));
        Assert.Equal(OpeningLikeStore, view.SaldoFinalGaveta, 2);
        Assert.Equal(OpeningLikeStore, view.SaldoFinal, 2);

        Assert.Equal(2, CountHugeCashMovements());
        Assert.Contains(view.Rows, r => r.Historico.Contains(CashMovementReportingRules.BadgeCancelledSale, StringComparison.Ordinal));
        Assert.Contains(view.Rows, r => r.Historico.Contains(CashMovementReportingRules.BadgeLinkedExchange, StringComparison.Ordinal));

        var aberturaId = view.Rows.First(r => r.Kind == "abertura").Id;
        CashService.CloseSession(view.SaldoFinalGaveta, "fecha 69H");
        var detail = CashService.GetCaixaHistoricoDetail(aberturaId);
        Assert.NotNull(detail);
        Assert.True(detail!.EntradasCaixa < 1_000);
        Assert.True(detail.SaidasCaixa < 1_000);
        Assert.Equal(OpeningLikeStore, detail.SaldoFinalGaveta, 2);
        Assert.Equal(2, CountHugeCashMovements());
        Assert.True(GetFechamentoVendasDia() < 1_000);
    }

    [Fact]
    public void VendaNormal_EntraEmKpiESaldo()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "t");

        var pid = TestDataHelper.SeedSimpleProduct(20, 100, 40, "N100", "Normal 100");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 100, 100);

        var view = CashService.GetOperacaoView();
        Assert.Equal(100, view.VendasDiaPdv, 2);
        Assert.Equal(100, view.EntradasCaixa, 2);
        Assert.Equal(0, view.SaidasCaixa, 2);
        Assert.Equal(150, view.SaldoFinalGaveta, 2);
    }

    [Fact]
    public void CancelamentoNormal_RemoveMovimentoDeVendaDoCaixa()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(50, "t");

        var pid = TestDataHelper.SeedSimpleProduct(20, 100, 40, "C100", "Cancel 100");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 100, 100);
        PdvService.CancelSale(sale.SaleId);

        var view = CashService.GetOperacaoView();
        Assert.Equal(0, view.VendasDiaPdv, 2);
        Assert.Equal(0, view.EntradasCaixa, 2);
        Assert.Equal(50, view.SaldoFinalGaveta, 2);
        Assert.Equal(0, CountCashMovementsForSale(sale.SaleId));
    }

    [Fact]
    public void TrocaParcialVendaAtiva_PermaneceNasSaidas()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "t");

        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 4, "TP", "Parcial");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);

        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems = [],
            PaymentType = "Dinheiro",
        });

        var view = CashService.GetOperacaoView();
        Assert.Equal(20, view.VendasDiaPdv, 2);
        Assert.Equal(20, view.EntradasCaixa, 2);
        Assert.Equal(10, view.SaidasCaixa, 2);
        Assert.Equal(60, view.SaldoFinalGaveta, 2);
        Assert.DoesNotContain(view.Rows, r =>
            r.Kind == "troca" && r.Historico.Contains(CashMovementReportingRules.BadgeLinkedExchange, StringComparison.Ordinal));
    }

    [Fact]
    public void VendaCanceladaComTrocaParcial_NaoEscondeTroca_FailSafe()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "t");

        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 4, "CP", "Canc Parcial");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems = [],
            PaymentType = "Dinheiro",
        });

        MarkSaleCancelledOnly(sale.SaleId);

        var view = CashService.GetOperacaoView();
        Assert.Equal(0, view.VendasDiaPdv, 2);
        Assert.Equal(20, view.EntradasCaixa, 2);
        Assert.Equal(10, view.SaidasCaixa, 2);
        Assert.Equal(60, view.SaldoFinalGaveta, 2);
        Assert.True(CountCashMovementsForSale(sale.SaleId) >= 1);
    }

    [Fact]
    public void SangriaReal_NaoEOmitidaJuntoDoPar25338()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(OpeningLikeStore, "loja");
        CashService.AddSangria(20, "sangria real");
        InsertCancelledSaleWithIntegralExchange(IncidentAmount);

        var view = CashService.GetOperacaoView();
        Assert.True(view.VendasDiaPdv < 1_000);
        Assert.True(view.EntradasCaixa < 1_000);
        Assert.Equal(20, view.SaidasCaixa, 2);
        Assert.Equal(OpeningLikeStore - 20, view.SaldoFinalGaveta, 2);
    }

    private static void EnsureStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    private static void InsertCancelledSaleWithIntegralExchange(double amount)
    {
        using var conn = DatabaseService.OpenConnection();
        var sessionId = Convert.ToInt32(Scalar(conn,
            "SELECT id FROM cash_sessions WHERE status = 'aberta' ORDER BY id DESC LIMIT 1;"));
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        var created = DateBrHelper.NowUtcIso();

        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, cash_received, change_amount, created_at)
            VALUES ($d, $t, 'Dinheiro', 1, $t, 0, $c);
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$d", day);
        ins.Parameters.AddWithValue("$t", amount);
        ins.Parameters.AddWithValue("$c", created);
        var saleId = Convert.ToInt32(ins.ExecuteScalar());

        using var ex = conn.CreateCommand();
        ex.CommandText = """
            INSERT INTO sale_exchanges (
              original_sale_id, created_at, return_total, new_total, difference, payment_type, cash_session_id)
            VALUES ($sale, $c, $t, 0, $diff, 'Dinheiro', $sid);
            SELECT last_insert_rowid();
            """;
        ex.Parameters.AddWithValue("$sale", saleId);
        ex.Parameters.AddWithValue("$c", created);
        ex.Parameters.AddWithValue("$t", amount);
        ex.Parameters.AddWithValue("$diff", -amount);
        ex.Parameters.AddWithValue("$sid", sessionId);
        var exchangeId = Convert.ToInt32(ex.ExecuteScalar());

        InsertMovement(conn, sessionId, day, created, "venda",
            $"VENDA #{saleId} (cancelada — fixture 69H)", amount, 0, "sale", saleId);
        InsertMovement(conn, sessionId, day, created, "troca",
            $"TROCA #{exchangeId} — devolução venda #{saleId}", 0, amount, "sale_exchange", exchangeId);
    }

    private static void InsertMovement(
        Microsoft.Data.Sqlite.SqliteConnection conn, int sessionId, string day, string created,
        string kind, string description, double amountIn, double amountOut, string refType, int refId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cash_movements (
              session_id, movement_date, kind, description, payment_type,
              amount_in, amount_out, affects_balance, ref_type, ref_id, created_at)
            VALUES ($sid, $date, $kind, $desc, 'Dinheiro', $in, $out, 1, $rt, $rid, $c);
            """;
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.Parameters.AddWithValue("$date", day);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$desc", description);
        cmd.Parameters.AddWithValue("$in", amountIn);
        cmd.Parameters.AddWithValue("$out", amountOut);
        cmd.Parameters.AddWithValue("$rt", refType);
        cmd.Parameters.AddWithValue("$rid", refId);
        cmd.Parameters.AddWithValue("$c", created);
        cmd.ExecuteNonQuery();
    }

    private static void MarkSaleCancelledOnly(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sales SET cancelled = 1 WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        cmd.ExecuteNonQuery();
    }

    private static int CountHugeCashMovements()
    {
        using var conn = DatabaseService.OpenConnection();
        return Convert.ToInt32(Scalar(conn, """
            SELECT COUNT(*) FROM cash_movements
            WHERE kind IN ('venda','troca')
              AND (amount_in > 1e12 OR amount_out > 1e12);
            """));
    }

    private static int CountCashMovementsForSale(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cash_movements WHERE ref_type = 'sale' AND ref_id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double GetFechamentoVendasDia()
    {
        using var conn = DatabaseService.OpenConnection();
        var v = Scalar(conn, "SELECT IFNULL(MAX(amount_in), 0) FROM cash_movements WHERE kind = 'fechamento';");
        return Convert.ToDouble(v);
    }

    private static int GetSaleItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static object Scalar(Microsoft.Data.Sqlite.SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()!;
    }
}
