using System.Globalization;
using System.IO;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71B-B8B — contribuição por produto. Banco TEMP; nunca deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CommercialGoalProductContributionServiceTests
{
    static TempDatabase Begin()
    {
        PdvService.TestBeforeInsertSaleItems = null;
        PdvService.TestAfterInsertSaleItems = null;
        PdvService.TestAfterSwapItemUpdate = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(100, "71b-b8b");
        return db;
    }

    static CommercialCompetence Comp(DateTime day) =>
        CommercialCompetence.Create(day.Year, day.Month);

    static CommercialGoalProductContributionSnapshot LoadFor(DateTime day) =>
        CommercialGoalProductContributionService.Load(Comp(day));

    static CommercialGoalFinancialSnapshot LoadB2(DateTime day) =>
        CommercialGoalFinancialService.Load(Comp(day));

    [Fact]
    public void QueryCount_e_um_e_nao_le_trocas()
    {
        Assert.Equal(1, CommercialGoalProductContributionService.ExpectedQueryCount);
        Assert.Equal(1, CommercialGoalProductContributionSnapshot.ExpectedQueryCount);
        var src = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "CommercialGoalProductContributionService.cs"));
        Assert.DoesNotContain("FROM sale_exchange", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN sale_exchange", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProductService.GetById", src, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_method_fees", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fiado_payments", src, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountToken(src, "cmd.CommandText"));
        Assert.Contains("LEFT JOIN sale_items", src, StringComparison.Ordinal);
        Assert.Contains("session_date", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Uma_venda_um_item_sem_ajuste()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 4, 10);
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "S1", "Simples");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Single(snap.Rows);
        Assert.Equal(pid, snap.Rows[0].ProductId);
        Assert.Equal(10m, snap.Rows[0].Revenue);
        Assert.Equal(6m, snap.Rows[0].Cogs);
        Assert.Equal(4m, snap.Rows[0].GrossProfit);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.Rows[0].CostQuality);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Dois_itens_sem_ajuste()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 5, 5);
        var a = TestDataHelper.SeedSimpleProduct(20, 60, 10, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 40, 8, "B", "B");
        FinalizeTwo(a, 60, b, 40, discount: 0, surcharge: 0, cash: 100);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Equal(2, snap.Rows.Count);
        Assert.Equal(60m, Row(snap, a).Revenue);
        Assert.Equal(40m, Row(snap, b).Revenue);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Desconto_global_60_40()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 6, 1);
        var a = TestDataHelper.SeedSimpleProduct(20, 60, 10, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 40, 10, "B", "B");
        FinalizeTwo(a, 60, b, 40, discount: 10, surcharge: 0, cash: 90);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Equal(54m, Row(snap, a).Revenue);
        Assert.Equal(36m, Row(snap, b).Revenue);
        Assert.Equal(10m, Row(snap, a).Cogs);
        Assert.Equal(10m, Row(snap, b).Cogs);
        Assert.Equal(44m, Row(snap, a).GrossProfit);
        Assert.Equal(26m, Row(snap, b).GrossProfit);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Acrescimo_global()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 6, 2);
        var a = TestDataHelper.SeedSimpleProduct(20, 60, 10, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 40, 10, "B", "B");
        FinalizeTwo(a, 60, b, 40, discount: 0, surcharge: 10, cash: 110);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Equal(66m, Row(snap, a).Revenue);
        Assert.Equal(44m, Row(snap, b).Revenue);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Residuo_de_um_centavo_desconto()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 6, 3);
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 1, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 20, 1, "B", "B");
        FinalizeTwo(a, 10, b, 20, discount: 0.01, surcharge: 0, cash: 29.99);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Equal(10.00m, Row(snap, a).Revenue);
        Assert.Equal(19.99m, Row(snap, b).Revenue);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Residuo_de_um_centavo_acrescimo()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 6, 4);
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 1, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 20, 1, "B", "B");
        FinalizeTwo(a, 10, b, 20, discount: 0, surcharge: 0.01, cash: 30.01);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Equal(10.00m, Row(snap, a).Revenue);
        Assert.Equal(20.01m, Row(snap, b).Revenue);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Subtotal_zero_nao_absorve_desconto()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 7, 1);
        var paid = TestDataHelper.SeedSimpleProduct(20, 10, 2, "P", "Pago");
        var free = TestDataHelper.SeedSimpleProduct(20, 0, 1, "F", "Gratis");
        InsertSaleWithItems(day, 9,
            (paid, 1, 10, 10,  2),
            (free, 1, 0, 0,  1));

        var snap = LoadFor(day);
        Assert.Equal(9m, Row(snap, paid).Revenue);
        Assert.Equal(0m, Row(snap, free).Revenue);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Todas_bases_zero_total_zero()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 7, 2);
        var a = TestDataHelper.SeedSimpleProduct(20, 0, 1, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 0, 1, "B", "B");
        InsertSaleWithItems(day, 0,
            (a, 1, 0, 0,  1),
            (b, 1, 0, 0,  1));

        var snap = LoadFor(day);
        Assert.Equal(0m, Row(snap, a).Revenue);
        Assert.Equal(0m, Row(snap, b).Revenue);
        Assert.Equal(0m, snap.Revenue);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Todas_bases_zero_total_nao_zero()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 7, 3);
        var a = TestDataHelper.SeedSimpleProduct(20, 0, 1, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 0, 1, "B", "B");
        var saleId = InsertSaleWithItems(day, 5,
            (a, 1, 0, 0,  1),
            (b, 1, 0, 0,  1));
        var firstItem = FirstSaleItemId(saleId);
        var firstPid = ProductIdOfItem(firstItem);

        var snap = LoadFor(day);
        Assert.Equal(5m, Row(snap, firstPid).Revenue);
        var other = firstPid == a ? b : a;
        Assert.Equal(0m, Row(snap, other).Revenue);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Desconto_100_e_total_zero()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 7, 4);
        var a = TestDataHelper.SeedSimpleProduct(20, 60, 10, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 40, 8, "B", "B");
        InsertSaleWithItems(day, 0,
            (a, 1, 60, 60,  10),
            (b, 1, 40, 40,  8));

        var snap = LoadFor(day);
        Assert.Equal(0m, Row(snap, a).Revenue);
        Assert.Equal(0m, Row(snap, b).Revenue);
        Assert.True(Row(snap, a).GrossProfit < 0);
        Assert.Null(Row(snap, a).GrossMarginPercent);
        AssertCloses(snap, LoadB2(day));
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
        Assert.Equal(10m, snap.Revenue);
        Assert.DoesNotContain(snap.Rows, r => r.Revenue == 20m);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Venda_sem_itens_nao_atribuida()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 1);
        InsertItemlessSale(day, 12.50m);

        var snap = LoadFor(day);
        Assert.Equal(12.50m, snap.UnattributedRevenue);
        Assert.Equal(0m, snap.UnattributedCogs);
        Assert.Equal(12.50m, snap.UnattributedGrossProfit);
        Assert.Empty(snap.Rows);
        Assert.True(snap.HasLimitation(CommercialGoalProductContributionLimitation.HasUnattributedRevenue));
        Assert.True(snap.HasLimitation(CommercialGoalProductContributionLimitation.ExchangesNotAdjusted));
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Mesmo_sku_em_varias_vendas()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 2);
        var pid = TestDataHelper.SeedSimpleProduct(50, 10, 4, "V1", "Varias");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        TestDataHelper.FinalizeSimpleCashSale(pid, 2, 10, 20);
        SetSessionDateAll(day);

        var snap = LoadFor(day);
        Assert.Single(snap.Rows);
        Assert.Equal(30m, snap.Rows[0].Revenue);
        Assert.Equal(12m, snap.Rows[0].Cogs);
        Assert.Equal(2, snap.Rows[0].SaleCount);
        Assert.Equal(3, snap.Rows[0].UnitsSold);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Sku_repetido_na_mesma_venda()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 3);
        var pid = TestDataHelper.SeedSimpleProduct(50, 10, 4, "R1", "Rep");
        FinalizeTwo(pid, 10, pid, 10, discount: 1, surcharge: 0, cash: 19);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Single(snap.Rows);
        Assert.Equal(19m, snap.Rows[0].Revenue);
        Assert.Equal(1, snap.Rows[0].SaleCount);
        Assert.Equal(2, snap.Rows[0].UnitsSold);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Cost_at_sale_exato_e_zero_exato()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 4);
        var paid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "E1", "Exato");
        var gift = TestDataHelper.SeedSimpleProduct(20, 10, 0, "E0", "Brinde");
        TestDataHelper.FinalizeSimpleCashSale(paid, 1, 10, 10);
        TestDataHelper.FinalizeSimpleCashSale(gift, 1, 10, 10);
        SetSessionDateAll(day);

        var snap = LoadFor(day);
        Assert.Equal(CommercialGoalCostQuality.Exact, Row(snap, paid).CostQuality);
        Assert.Equal(CommercialGoalCostQuality.Exact, Row(snap, gift).CostQuality);
        Assert.Equal(0m, Row(snap, gift).Cogs);
        Assert.Equal(10m, Row(snap, gift).GrossProfit);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Fallback_legado_e_mistura_vira_estimated()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 5);
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "L1", "Legado");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        InsertLegacySale(pid, 1, 10, day);
        SetSessionDateAll(day);

        var snap = LoadFor(day);
        Assert.Equal(CommercialGoalCostQuality.EstimatedLegacy, snap.CostQuality);
        Assert.Equal(CommercialGoalCostQuality.EstimatedLegacy, Row(snap, pid).CostQuality);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Custo_indisponivel_nao_publica_gp()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 6);
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "U1", "Indisp");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(sale.SaleId, day);
        SetItemQuantityUnavailable(sale.SaleId);

        var snap = LoadFor(day);
        Assert.Equal(CommercialGoalCostQuality.Unavailable, snap.CostQuality);
        Assert.False(snap.GrossProfitAvailable);
        Assert.Null(snap.GrossProfit);
        Assert.Null(snap.UnattributedGrossProfit);
        Assert.Null(Row(snap, pid).GrossProfit);
        Assert.Null(Row(snap, pid).GrossMarginPercent);
        Assert.Null(Row(snap, pid).GrossProfitShare);
        Assert.Equal(10m, snap.Revenue);
        var b2 = LoadB2(day);
        Assert.Equal(b2.NetCommercialRevenue, snap.Revenue);
        Assert.Equal(b2.Cogs, snap.Cogs);
        Assert.Null(b2.GrossProfit);
    }

    [Fact]
    public void Gp_negativo_margem_negativa()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 7);
        var pid = TestDataHelper.SeedSimpleProduct(20, 5, 10, "N1", "Neg");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 5, 5);
        SetSessionDate(LastSaleId(), day);

        var snap = LoadFor(day);
        Assert.Equal(-5m, snap.Rows[0].GrossProfit);
        Assert.True(snap.Rows[0].GrossMarginPercent < 0);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Receita_zero_margem_null_gp_total_zero_share_null()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 8);
        var zero = TestDataHelper.SeedSimpleProduct(20, 10, 10, "Z", "Zero");
        InsertSaleWithItems(day, 0, (zero, 1, 10, 10,  0));
        var even = TestDataHelper.SeedSimpleProduct(20, 10, 10, "E", "Even");
        TestDataHelper.FinalizeSimpleCashSale(even, 1, 10, 10);
        SetSessionDateAll(day);

        var snap = LoadFor(day);
        Assert.Null(Row(snap, zero).GrossMarginPercent);
        Assert.Equal(0m, Row(snap, even).GrossProfit);
        Assert.Equal(0m, snap.GrossProfit);
        Assert.Null(Row(snap, even).GrossProfitShare);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Gp_total_negativo_share_preserva_sinal()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 9);
        var a = TestDataHelper.SeedSimpleProduct(20, 5, 10, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 4, 10, "B", "B");
        TestDataHelper.FinalizeSimpleCashSale(a, 1, 5, 5);
        TestDataHelper.FinalizeSimpleCashSale(b, 1, 4, 4);
        SetSessionDateAll(day);

        var snap = LoadFor(day);
        Assert.True(snap.GrossProfit < 0);
        Assert.NotNull(Row(snap, a).GrossProfitShare);
        Assert.True(Row(snap, a).GrossProfitShare > 0);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Ranking_gp_desc_depois_product_id()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 8, 10);
        var low = TestDataHelper.SeedSimpleProduct(20, 10, 8, "L", "Low");
        var high = TestDataHelper.SeedSimpleProduct(20, 10, 1, "H", "High");
        TestDataHelper.FinalizeSimpleCashSale(low, 1, 10, 10);
        TestDataHelper.FinalizeSimpleCashSale(high, 1, 10, 10);
        SetSessionDateAll(day);

        var snap = LoadFor(day);
        Assert.Equal(high, snap.Rows[0].ProductId);
        Assert.Equal(low, snap.Rows[1].ProductId);
        Assert.True(snap.Rows[0].GrossProfit > snap.Rows[1].GrossProfit);
    }

    [Fact]
    public void Fiado_cartao_dinheiro_mesmo_gp()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 9, 1);
        var customer = SeedCustomer();
        var cashP = TestDataHelper.SeedSimpleProduct(20, 15, 7, "CASH", "Cash");
        var cardP = TestDataHelper.SeedSimpleProduct(20, 15, 7, "CARD", "Card");
        var fiadoP = TestDataHelper.SeedSimpleProduct(20, 15, 7, "FIA", "Fiado");

        TestDataHelper.FinalizeSimpleCashSale(cashP, 1, 15, 15);
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [Line(cardP, "CARD", 15)],
            PaymentType = "Cartão Crédito",
            CashReceived = 0,
        });
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [Line(fiadoP, "FIA", 15)],
            PaymentType = "Fiado",
            CustomerPersonId = customer,
            CashReceived = 0,
        });
        SetSessionDateAll(day);

        var snap = LoadFor(day);
        Assert.Equal(Row(snap, cashP).Revenue, Row(snap, cardP).Revenue);
        Assert.Equal(Row(snap, cashP).GrossProfit, Row(snap, fiadoP).GrossProfit);
        Assert.Equal(Row(snap, cashP).Cogs, Row(snap, cardP).Cogs);
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Alteracao_pagamento_nao_muda_contribuicao()
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
        Assert.Equal(before.Rows[0].Revenue, after.Rows[0].Revenue);
        Assert.Equal(before.Rows[0].GrossProfit, after.Rows[0].GrossProfit);
        AssertCloses(after, LoadB2(day));
    }

    [Fact]
    public void Kit_permanece_no_sku_vendido()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 9, 2);
        var comp = TestDataHelper.SeedSimpleProduct(50, 5, 2, "CMP", "Comp");
        var kit = TestDataHelper.SeedSimpleProduct(10, 20, 6, "KIT", "Kit");
        SetKit(kit, comp, qty: 2);
        InsertSaleWithItems(day, 20, (kit, 1, 20, 20,  6));

        var snap = LoadFor(day);
        Assert.Single(snap.Rows);
        Assert.Equal(kit, snap.Rows[0].ProductId);
        Assert.DoesNotContain(snap.Rows, r => r.ProductId == comp);
        Assert.True(snap.Rows[0].HasLimitation(CommercialGoalProductContributionLimitation.HistoricalBomUnavailable));
        Assert.True(snap.HasLimitation(CommercialGoalProductContributionLimitation.HistoricalBomUnavailable));
        AssertCloses(snap, LoadB2(day));
    }

    [Fact]
    public void Session_date_exclui_outra_competencia()
    {
        using var _ = Begin();
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 4, "D", "Data");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2026, 1, 15));
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), new DateTime(2026, 2, 15));

        var jan = LoadFor(new DateTime(2026, 1, 15));
        var feb = LoadFor(new DateTime(2026, 2, 15));
        Assert.Equal(10m, jan.Revenue);
        Assert.Equal(10m, feb.Revenue);
        AssertCloses(jan, LoadB2(new DateTime(2026, 1, 15)));
        AssertCloses(feb, LoadB2(new DateTime(2026, 2, 15)));
    }

    [Fact]
    public void Mes_sem_vendas()
    {
        using var _ = Begin();
        var snap = LoadFor(new DateTime(2026, 3, 1));
        Assert.Equal(0, snap.SaleCount);
        Assert.Empty(snap.Rows);
        Assert.Equal(0m, snap.UnattributedRevenue);
        Assert.True(snap.GrossProfitAvailable);
        Assert.Equal(0m, snap.GrossProfit);
        Assert.True(snap.HasLimitation(CommercialGoalProductContributionLimitation.ExchangesNotAdjusted));
        AssertCloses(snap, LoadB2(new DateTime(2026, 3, 1)));
    }

    static void AssertCloses(
        CommercialGoalProductContributionSnapshot snap,
        CommercialGoalFinancialSnapshot b2)
    {
        decimal skuRev = 0, skuCogs = 0, skuGp = 0;
        foreach (var row in snap.Rows)
        {
            skuRev += row.Revenue;
            skuCogs += row.Cogs;
            if (row.GrossProfit is { } gp)
                skuGp += gp;
        }

        Assert.Equal(b2.NetCommercialRevenue, snap.Revenue);
        Assert.Equal(b2.NetCommercialRevenue, skuRev + snap.UnattributedRevenue);
        Assert.Equal(b2.Cogs, snap.Cogs);
        Assert.Equal(b2.Cogs, skuCogs + snap.UnattributedCogs);
        Assert.Equal(b2.GrossProfitAvailable, snap.GrossProfitAvailable);
        Assert.Equal(b2.CostQuality, snap.CostQuality);
        if (b2.GrossProfitAvailable)
        {
            Assert.Equal(b2.GrossProfit, snap.GrossProfit);
            Assert.Equal(b2.GrossProfit, skuGp + snap.UnattributedGrossProfit);
        }
        else
        {
            Assert.Null(snap.GrossProfit);
            Assert.Null(snap.UnattributedGrossProfit);
        }
    }

    static CommercialGoalProductContributionRow Row(
        CommercialGoalProductContributionSnapshot snap, int productId)
    {
        foreach (var row in snap.Rows)
        {
            if (row.ProductId == productId)
                return row;
        }

        throw new InvalidOperationException($"SKU {productId} ausente.");
    }

    static PdvCartLine Line(int productId, string code, double price) =>
        new()
        {
            ProductId = productId,
            Code = code,
            Name = code,
            Unit = "UN",
            Quantity = 1,
            UnitPrice = price,
            StockUnitsPerSale = 1,
        };

    static void FinalizeTwo(
        int a, double priceA, int b, double priceB,
        double discount, double surcharge, double cash)
    {
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                Line(a, "A", priceA),
                Line(b, "B", priceB),
            ],
            PaymentType = "Dinheiro",
            Discount = discount,
            Surcharge = surcharge,
            CashReceived = cash,
        });
    }

    static int InsertSaleWithItems(
        DateTime day,
        decimal total,
        params (int ProductId, double Qty, double UnitPrice, double Subtotal, double Cost)[] items)
    {
        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
            VALUES ($d, $t, 'Dinheiro', 0, datetime('now','localtime'));
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        ins.Parameters.AddWithValue("$t", (double)total);
        var saleId = Convert.ToInt32(ins.ExecuteScalar());
        foreach (var item in items)
        {
            using var row = conn.CreateCommand();
            row.CommandText = """
                INSERT INTO sale_items (
                  sale_id, product_id, product_code, product_name, unit,
                  quantity, unit_price, subtotal, cost_at_sale)
                VALUES ($s, $p, $c, $n, 'UN', $q, $u, $sub, $cost);
                """;
            row.Parameters.AddWithValue("$s", saleId);
            row.Parameters.AddWithValue("$p", item.ProductId);
            row.Parameters.AddWithValue("$c", item.ProductId.ToString(CultureInfo.InvariantCulture));
            row.Parameters.AddWithValue("$n", "ITEM");
            row.Parameters.AddWithValue("$q", item.Qty);
            row.Parameters.AddWithValue("$u", item.UnitPrice);
            row.Parameters.AddWithValue("$sub", item.Subtotal);
            row.Parameters.AddWithValue("$cost", item.Cost);
            row.ExecuteNonQuery();
        }

        return saleId;
    }

    static void InsertItemlessSale(DateTime day, decimal total)
    {
        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
            VALUES ($d, $t, 'Fiado', 0, datetime('now','localtime'));
            """;
        ins.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        ins.Parameters.AddWithValue("$t", (double)total);
        ins.ExecuteNonQuery();
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
            VALUES ($s, $p, 'LEGADO B8B', $q, $u, $t);
            """;
        item.Parameters.AddWithValue("$s", saleId);
        item.Parameters.AddWithValue("$p", productId);
        item.Parameters.AddWithValue("$q", qty);
        item.Parameters.AddWithValue("$u", unitPrice);
        item.Parameters.AddWithValue("$t", total);
        item.ExecuteNonQuery();
        return saleId;
    }

    static void SetKit(int kitId, int componentId, double qty)
    {
        var extra = new ProductExtra
        {
            Composicao = true,
            ComposicaoItens =
            [
                new ProductCompositionItem { ProductId = componentId, Quantity = qty, Name = "Comp" },
            ],
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET extra_json = $j WHERE id = $id;";
        cmd.Parameters.AddWithValue("$j", extra.ToJson());
        cmd.Parameters.AddWithValue("$id", kitId);
        cmd.ExecuteNonQuery();
    }

    static int SeedCustomer()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('cliente', 'fisica', 'Cliente B8B', 1, '[]');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
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
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $s ORDER BY id LIMIT 1;";
        cmd.Parameters.AddWithValue("$s", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static int ProductIdOfItem(int saleItemId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM sale_items WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleItemId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static void SetItemQuantityUnavailable(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sale_items SET quantity = 1e999 WHERE sale_id = $s;";
        cmd.Parameters.AddWithValue("$s", saleId);
        cmd.ExecuteNonQuery();
    }

    static int CountToken(string src, string token)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var i = src.IndexOf(token, start, StringComparison.Ordinal);
            if (i < 0)
                return count;
            count++;
            start = i + token.Length;
        }
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
