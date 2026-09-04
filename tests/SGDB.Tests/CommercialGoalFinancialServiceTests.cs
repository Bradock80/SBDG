using System.Globalization;
using System.IO;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71B-B2 — snapshot financeiro mensal da Meta. Banco TEMP; nunca deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CommercialGoalFinancialServiceTests
{
    static TempDatabase Begin()
    {
        PdvService.TestBeforeInsertSaleItems = null;
        PdvService.TestAfterInsertSaleItems = null;
        PdvService.TestAfterSwapItemUpdate = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(100, "71b-b2");
        return db;
    }

    static CommercialCompetence Comp(DateTime day) =>
        CommercialCompetence.Create(day.Year, day.Month);

    static CommercialGoalFinancialSnapshot LoadFor(DateTime day) =>
        CommercialGoalFinancialService.Load(Comp(day));

    [Fact]
    public void QueryCount_e_dois_e_limitacao_troca()
    {
        Assert.Equal(2, CommercialGoalFinancialService.ExpectedQueryCount);
        Assert.Equal(2, CommercialGoalFinancialSnapshot.ExpectedQueryCount);
        Assert.Contains("sale_exchanges", CommercialGoalFinancialService.ExchangeDoesNotAdjustPnlLimitation, StringComparison.Ordinal);
        Assert.Contains("não reduzem", CommercialGoalFinancialService.ExchangeDoesNotAdjustPnlLimitation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mes_sem_vendas()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 9, 15);
        var snap = LoadFor(day);
        Assert.Equal(0, snap.SaleCount);
        Assert.Equal(0, snap.SaleItemCount);
        Assert.Equal(0m, snap.NetCommercialRevenue);
        Assert.Equal(0m, snap.Cogs);
        Assert.Equal(0m, snap.GrossProfit);
        Assert.True(snap.GrossProfitAvailable);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.CostQuality);
        Assert.False(snap.ProfitIsEstimated);
        Assert.Null(snap.CostReliabilityNote);
        Assert.Equal(0, snap.UnavailableCostItemCount);
    }

    [Fact]
    public void Uma_venda_simples_snapshot()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 4, 10);
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "S1", "Simples");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Equal(1, snap.SaleCount);
        Assert.Equal(1, snap.SaleItemCount);
        Assert.Equal(10m, snap.NetCommercialRevenue);
        Assert.Equal(6m, snap.Cogs);
        Assert.Equal(4m, snap.GrossProfit);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.CostQuality);
        Assert.Equal(1, snap.HistoricalCostItemCount);
        Assert.Equal(0, snap.EstimatedLegacyCostItemCount);
        Assert.False(snap.ProfitIsEstimated);
    }

    [Fact]
    public void Varias_vendas()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 5, 5);
        var pid = TestDataHelper.SeedSimpleProduct(50, 10, 4, "V1", "Varias");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        TestDataHelper.FinalizeSimpleCashSale(pid, 2, 10, 20);
        SetSessionDateAll(day);

        var snap = LoadFor(day);
        Assert.Equal(2, snap.SaleCount);
        Assert.Equal(2, snap.SaleItemCount);
        Assert.Equal(30m, snap.NetCommercialRevenue);
        Assert.Equal(12m, snap.Cogs);
        Assert.Equal(18m, snap.GrossProfit);
    }

    [Fact]
    public void Venda_cancelada_nao_entra()
    {
        using var _ = Begin();
        var day = DateTime.Today;
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 5, "C1", "Cancel");
        var keep = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        var cancel = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        PdvService.CancelSale(cancel.SaleId);
        SetSessionDate(keep.SaleId, day);
        SetSessionDate(cancel.SaleId, day);

        var snap = LoadFor(day);
        Assert.Equal(1, snap.SaleCount);
        Assert.Equal(1, snap.SaleItemCount);
        Assert.Equal(10m, snap.NetCommercialRevenue);
        Assert.Equal(5m, snap.Cogs);
        Assert.Equal(5m, snap.GrossProfit);

        var dre = DreService.GetDre(Comp(day).StartDate.ToDateTime(TimeOnly.MinValue), Comp(day).EndDate.ToDateTime(TimeOnly.MinValue));
        Assert.Equal((double)snap.NetCommercialRevenue, dre.ReceitaLiquida);
        Assert.Equal((double)snap.Cogs, dre.Cmv);
        Assert.Equal((double)snap.GrossProfit!, dre.LucroBruto);
    }

    [Fact]
    public void Fiado_conta_como_faturamento_na_venda()
    {
        using var _ = Begin();
        var day = DateTime.Today;
        var customer = SeedCustomer();
        var pid = TestDataHelper.SeedSimpleProduct(20, 15, 7, "F1", "Fiado");
        var result = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = pid,
                    Code = "F1",
                    Name = "Fiado",
                    Unit = "UN",
                    Quantity = 1,
                    UnitPrice = 15,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Fiado",
            CustomerPersonId = customer,
            CashReceived = 0,
        });
        Assert.Equal(15, result.Total);
        SetSessionDate(result.SaleId, day);

        var snap = LoadFor(day);
        Assert.Equal(15m, snap.NetCommercialRevenue);
        Assert.Equal(7m, snap.Cogs);
        Assert.Equal(8m, snap.GrossProfit);
    }

    [Fact]
    public void Alteracao_pagamento_nao_muda_receita()
    {
        using var _ = Begin();
        var day = DateTime.Today;
        var pid = TestDataHelper.SeedSimpleProduct(20, 20, 8, "P1", "Pag");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 20, 20);
        SetSessionDate(sale.SaleId, day);
        var before = LoadFor(day);
        PdvService.ChangeSalePayment(
            sale.SaleId,
            [new PdvPaymentPart { PaymentType = "PIX", Amount = 20 }],
            cashReceived: 0);
        var after = LoadFor(day);
        Assert.Equal(before.NetCommercialRevenue, after.NetCommercialRevenue);
        Assert.Equal(before.Cogs, after.Cogs);
        Assert.Equal(before.GrossProfit, after.GrossProfit);
    }

    [Fact]
    public void Desconto_reflete_em_sales_total()
    {
        using var _ = Begin();
        var day = DateTime.Today;
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 4, "D1", "Desc");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = pid, Code = "D1", Name = "Desc", Unit = "UN",
                    Quantity = 1, UnitPrice = 10, StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            Discount = 2,
            CashReceived = 8,
        });
        SetSessionDate(sale.SaleId, day);

        var snap = LoadFor(day);
        Assert.Equal(8m, snap.NetCommercialRevenue);
        Assert.Equal(4m, snap.Cogs);
        Assert.Equal(4m, snap.GrossProfit);
    }

    [Fact]
    public void Acrescimo_reflete_em_sales_total()
    {
        using var _ = Begin();
        var day = DateTime.Today;
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A1", "Acr");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = pid, Code = "A1", Name = "Acr", Unit = "UN",
                    Quantity = 1, UnitPrice = 10, StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            Surcharge = 3,
            CashReceived = 13,
        });
        SetSessionDate(sale.SaleId, day);

        var snap = LoadFor(day);
        Assert.Equal(13m, snap.NetCommercialRevenue);
        Assert.Equal(4m, snap.Cogs);
        Assert.Equal(9m, snap.GrossProfit);
    }

    [Fact]
    public void Quantity_maior_que_1()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 3);
        var pid = TestDataHelper.SeedSimpleProduct(50, 10, 5, "Q1", "Qtd");
        TestDataHelper.FinalizeSimpleCashSale(pid, 3, 10, 30);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Equal(30m, snap.NetCommercialRevenue);
        Assert.Equal(15m, snap.Cogs);
        Assert.Equal(15m, snap.GrossProfit);
        Assert.Equal(1, snap.HistoricalCostItemCount);
    }

    [Fact]
    public void Custo_snapshot_zero_e_historico()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 4);
        var pid = TestDataHelper.SeedSimpleProduct(10, 8, 0, "Z1", "Brinde");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        SetSessionDate(LastSaleId(), day);
        SetCost(pid, 9);

        var snap = LoadFor(day);
        Assert.Equal(0m, snap.Cogs);
        Assert.Equal(8m, snap.GrossProfit);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.CostQuality);
        Assert.Equal(1, snap.HistoricalCostItemCount);
        Assert.False(snap.ProfitIsEstimated);
    }

    [Fact]
    public void Fallback_legado()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 5);
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "L1", "Legado");
        InsertLegacySale(pid, 2, 8, day);
        SetCost(pid, 9);

        var snap = LoadFor(day);
        Assert.Equal(16m, snap.NetCommercialRevenue);
        Assert.Equal(18m, snap.Cogs);
        Assert.Equal(-2m, snap.GrossProfit);
        Assert.Equal(CommercialGoalCostQuality.EstimatedLegacy, snap.CostQuality);
        Assert.True(snap.ProfitIsEstimated);
        Assert.Equal(0, snap.HistoricalCostItemCount);
        Assert.Equal(1, snap.EstimatedLegacyCostItemCount);
        Assert.Equal(HistoricalSaleCostRules.EstimatedLegacyPeriodNote, snap.CostReliabilityNote);
    }

    [Fact]
    public void Mix_snapshot_e_legado()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 6);
        var pid = TestDataHelper.SeedSimpleProduct(30, 8, 5, "M1", "Mix");
        InsertLegacySale(pid, 1, 8, day);
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        SetSessionDate(LastSaleId(), day);
        SetCost(pid, 9);

        var snap = LoadFor(day);
        Assert.Equal(16m, snap.NetCommercialRevenue);
        Assert.Equal(14m, snap.Cogs);
        Assert.Equal(5m, snap.HistoricalCogs);
        Assert.Equal(9m, snap.EstimatedLegacyCogs);
        Assert.Equal(2m, snap.GrossProfit);
        Assert.Equal(CommercialGoalCostQuality.EstimatedLegacy, snap.CostQuality);
        Assert.Equal(1, snap.HistoricalCostItemCount);
        Assert.Equal(1, snap.EstimatedLegacyCostItemCount);
    }

    [Fact]
    public void Custo_indisponivel_lucro_NA()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 7);
        var pid = TestDataHelper.SeedSimpleProduct(10, 10, 4, "U1", "Unavail");
        var saleId = InsertLegacySale(pid, 1, 10, day);
        SetItemQuantityUnavailable(saleId);

        var snap = LoadFor(day);
        Assert.Equal(10m, snap.NetCommercialRevenue);
        Assert.Equal(1, snap.UnavailableCostItemCount);
        Assert.Equal(CommercialGoalCostQuality.Unavailable, snap.CostQuality);
        Assert.Null(snap.GrossProfit);
        Assert.False(snap.GrossProfitAvailable);
        Assert.Equal(CommercialGoalFinancialService.UnavailableGrossProfitNote, snap.CostReliabilityNote);
        Assert.Equal(0m, snap.Cogs);
    }

    [Fact]
    public void Janeiro_31_dias()
    {
        using var _ = Begin();
        var competence = CommercialCompetence.Create(2026, 1);
        Assert.Equal(31, competence.DaysInMonth);
        var pid = TestDataHelper.SeedSimpleProduct(10, 10, 3, "J1", "Jan");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2026, 1, 31));
        var snap = CommercialGoalFinancialService.Load(competence);
        Assert.Equal(10m, snap.NetCommercialRevenue);
        Assert.Equal(competence.ToString(), snap.Competence.ToString());
    }

    [Fact]
    public void Fevereiro_28()
    {
        using var _ = Begin();
        var competence = CommercialCompetence.Create(2026, 2);
        Assert.Equal(28, competence.DaysInMonth);
        var pid = TestDataHelper.SeedSimpleProduct(10, 10, 3, "F28", "Fev28");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2026, 2, 28));
        var snap = CommercialGoalFinancialService.Load(competence);
        Assert.Equal(1, snap.SaleCount);
        Assert.Equal(0, CommercialGoalFinancialService.Load(CommercialCompetence.Create(2026, 3)).SaleCount);
    }

    [Fact]
    public void Fevereiro_bissexto_29()
    {
        using var _ = Begin();
        var competence = CommercialCompetence.Create(2024, 2);
        Assert.Equal(29, competence.DaysInMonth);
        var pid = TestDataHelper.SeedSimpleProduct(10, 10, 3, "F29", "Fev29");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2024, 2, 29));
        var snap = CommercialGoalFinancialService.Load(competence);
        Assert.Equal(1, snap.SaleCount);
    }

    [Fact]
    public void Dezembro_janeiro_nao_misturam()
    {
        using var _ = Begin();
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 4, "DJ", "Ano");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2025, 12, 31));
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2026, 1, 1));

        var dec = CommercialGoalFinancialService.Load(CommercialCompetence.Create(2025, 12));
        var jan = CommercialGoalFinancialService.Load(CommercialCompetence.Create(2026, 1));
        Assert.Equal(1, dec.SaleCount);
        Assert.Equal(1, jan.SaleCount);
        Assert.Equal(10m, dec.NetCommercialRevenue);
        Assert.Equal(10m, jan.NetCommercialRevenue);
    }

    [Fact]
    public void Venda_fora_da_competencia_ignorada()
    {
        using var _ = Begin();
        var pid = TestDataHelper.SeedSimpleProduct(10, 10, 4, "OUT", "Fora");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2026, 3, 1));
        var snap = CommercialGoalFinancialService.Load(CommercialCompetence.Create(2026, 4));
        Assert.Equal(0, snap.SaleCount);
        Assert.Equal(0m, snap.NetCommercialRevenue);
    }

    [Fact]
    public void Lucro_zero_e_negativo()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 9, 2);
        var zero = TestDataHelper.SeedSimpleProduct(10, 5, 5, "LZ", "Zero");
        TestDataHelper.FinalizeSimpleCashSale(zero, 1, 5, 5);
        SetSessionDate(LastSaleId(), day);
        var snapZero = LoadFor(day);
        Assert.Equal(0m, snapZero.GrossProfit);

        var neg = TestDataHelper.SeedSimpleProduct(10, 5, 8, "LN", "Neg");
        TestDataHelper.FinalizeSimpleCashSale(neg, 1, 5, 5);
        SetSessionDate(LastSaleId(), day);
        var snapNeg = LoadFor(day);
        Assert.Equal(10m, snapNeg.NetCommercialRevenue);
        Assert.Equal(13m, snapNeg.Cogs);
        Assert.Equal(-3m, snapNeg.GrossProfit);
    }

    [Fact]
    public void Equivalencia_DRE_mes_completo()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 11, 15);
        var competence = Comp(day);
        var pid = TestDataHelper.SeedSimpleProduct(40, 12, 5, "EQ", "Equiv");
        TestDataHelper.FinalizeSimpleCashSale(pid, 2, 12, 24);
        SetSessionDate(LastSaleId(), day);
        InsertLegacySale(pid, 1, 12, day);
        SetCost(pid, 7);

        var snap = CommercialGoalFinancialService.Load(competence);
        var dre = DreService.GetDre(
            competence.StartDate.ToDateTime(TimeOnly.MinValue),
            competence.EndDate.ToDateTime(TimeOnly.MinValue));

        Assert.Equal(dre.ReceitaLiquida, (double)snap.NetCommercialRevenue);
        Assert.Equal(dre.Cmv, (double)snap.Cogs);
        Assert.Equal(dre.LucroBruto, (double)snap.GrossProfit!);
        Assert.Equal(dre.HasEstimatedLegacyCost, snap.ProfitIsEstimated);
        Assert.Equal(dre.CmvHistorico, (double)snap.HistoricalCogs);
        Assert.Equal(dre.CmvEstimado, (double)snap.EstimatedLegacyCogs);
        Assert.Equal(dre.QtdVendas, snap.SaleCount);
    }

    [Fact]
    public void Troca_nao_reduz_pnl_contrato_V1()
    {
        using var _ = Begin();
        var day = DateTime.Today;
        var pid = TestDataHelper.SeedSimpleProduct(20, 100, 60, "TR", "Troca");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 100, 100);
        SetSessionDate(sale.SaleId, day);
        var before = LoadFor(day);

        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns =
            [
                new SaleExchangeReturnLine
                {
                    SaleItemId = FirstSaleItemId(sale.SaleId),
                    Qty = 1,
                },
            ],
        });

        var after = LoadFor(day);
        Assert.Equal(before.NetCommercialRevenue, after.NetCommercialRevenue);
        Assert.Equal(before.Cogs, after.Cogs);
        Assert.Equal(before.GrossProfit, after.GrossProfit);
        Assert.Contains("sale_exchanges", after.ExchangePnlLimitation, StringComparison.Ordinal);
    }

    [Fact]
    public void Breakdown_preserva_Soma_Period_dos_consumidores()
    {
        using var _ = Begin();
        var day = DateTime.Today;
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "BR", "Break");
        InsertLegacySale(pid, 1, 8, day);
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        SetSessionDate(LastSaleId(), day);

        var from = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var conn = DatabaseService.OpenConnection();
        var classic = HistoricalSaleCostRules.SumNonCancelledBySession(conn, from, from);
        var detail = HistoricalSaleCostRules.SumNonCancelledBySessionWithBreakdown(conn, from, from);
        Assert.Equal(classic.Total, detail.Period.Total);
        Assert.Equal(classic.Historical, detail.Period.Historical);
        Assert.Equal(classic.EstimatedLegacy, detail.Period.EstimatedLegacy);
        Assert.Equal(classic.HasEstimatedLegacyCost, detail.Period.HasEstimatedLegacyCost);
        Assert.Equal(2, detail.SaleItemCount);
    }

    [Fact]
    public void Servico_nao_consulta_sale_exchanges()
    {
        var path = FindSource("src", "SGDB.App", "Services", "CommercialGoalFinancialService.cs");
        var src = File.ReadAllText(path);
        Assert.DoesNotContain("FROM sale_exchange", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN sale_exchange", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppSettings", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Forecast", src, StringComparison.Ordinal);
    }

    static int SeedCustomer()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('cliente', 'fisica', 'Cliente 71B', 1, '[]');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static void SetCost(int productId, double cost)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET cost_price = $c WHERE id = $id;";
        cmd.Parameters.AddWithValue("$c", cost);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    static int InsertLegacySale(int productId, double qty, double unitPrice, DateTime sessionDate)
    {
        var total = ProductPriceHelper.RoundPrice(qty * unitPrice);
        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
            VALUES ($d, $t, 'Dinheiro', 0, datetime('now','localtime'));
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$d", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        ins.Parameters.AddWithValue("$t", total);
        var saleId = Convert.ToInt32(ins.ExecuteScalar());
        using var item = conn.CreateCommand();
        item.CommandText = """
            INSERT INTO sale_items (sale_id, product_id, product_name, quantity, unit_price, subtotal)
            VALUES ($s, $p, 'LEGADO 71B', $q, $u, $t);
            """;
        item.Parameters.AddWithValue("$s", saleId);
        item.Parameters.AddWithValue("$p", productId);
        item.Parameters.AddWithValue("$q", qty);
        item.Parameters.AddWithValue("$u", unitPrice);
        item.Parameters.AddWithValue("$t", total);
        item.ExecuteNonQuery();
        return saleId;
    }

    static void SetSessionDate(int saleId, DateTime day)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sales SET session_date = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", saleId);
        cmd.ExecuteNonQuery();
    }

    static void SetSessionDateAll(DateTime day)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sales SET session_date = $d;";
        cmd.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    static int LastSaleId()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(id) FROM sales;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static int FirstSaleItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $s LIMIT 1;";
        cmd.Parameters.AddWithValue("$s", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static void SetItemQuantityUnavailable(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        // SQLite REAL Infinity — Microsoft.Data.Sqlite rejeita double.NaN em parâmetros.
        cmd.CommandText = "UPDATE sale_items SET quantity = 1e999 WHERE sale_id = $s;";
        cmd.Parameters.AddWithValue("$s", saleId);
        cmd.ExecuteNonQuery();
    }

    static string FindSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}
