using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class ValidityControlTests
{
    static readonly DateTime Today = new(2026, 8, 24);

    [Theory]
    [InlineData(-2, ProductExpiryStatusKind.Expired)]
    [InlineData(0, ProductExpiryStatusKind.Today)]
    [InlineData(1, ProductExpiryStatusKind.Within7)]
    [InlineData(7, ProductExpiryStatusKind.Within7)]
    [InlineData(8, ProductExpiryStatusKind.Within15)]
    [InlineData(15, ProductExpiryStatusKind.Within15)]
    [InlineData(16, ProductExpiryStatusKind.Within30)]
    [InlineData(30, ProductExpiryStatusKind.Within30)]
    [InlineData(31, ProductExpiryStatusKind.Within60)]
    [InlineData(60, ProductExpiryStatusKind.Within60)]
    [InlineData(61, ProductExpiryStatusKind.Within90)]
    [InlineData(90, ProductExpiryStatusKind.Within90)]
    [InlineData(91, ProductExpiryStatusKind.Ok)]
    public void FaixasExclusivas(int offset, ProductExpiryStatusKind kind)
    {
        var status = ProductExpiryService.Classify(Today.AddDays(offset), Today);
        Assert.Equal(kind, status.Kind);
    }

    [Fact]
    public void CardsSemDuplaContagem()
    {
        var product = Product("P", lots:
        [
            Lot("A", -1),
            Lot("B", 0),
            Lot("C", 3),
            Lot("D", 10),
            Lot("E", 20),
            Lot("F", 40),
            Lot("G", 70),
            Lot("H", 120),
            Lot("I", null),
        ]);
        var snap = ValidityControlEngine.Snapshot([product], Today);
        Assert.Equal(1, snap.Cards.Expired);
        Assert.Equal(1, snap.Cards.Today);
        Assert.Equal(1, snap.Cards.Days7);
        Assert.Equal(1, snap.Cards.Days15);
        Assert.Equal(1, snap.Cards.Days30);
        Assert.Equal(1, snap.Cards.Days60);
        Assert.Equal(1, snap.Cards.Days90);
        Assert.Equal(1, snap.Cards.Ok);
        Assert.Equal(1, snap.Cards.Uninformed);
        Assert.Equal(9, snap.Cards.Total);
        Assert.Equal(9, snap.Rows.Count);
    }

    [Fact]
    public void FiltroPorFaixa()
    {
        var snap = ValidityControlEngine.Snapshot([Product("P", lots: [Lot("A", -1), Lot("B", 3), Lot("C", 40)])], Today);
        var seven = ValidityControlEngine.Apply(snap.Rows, ValidityControlFilterKind.Days7);
        Assert.Equal("B", Assert.Single(seven).LotDisplay);
        var expired = ValidityControlEngine.Apply(snap.Rows, ValidityControlFilterKind.Expired);
        Assert.Equal("A", Assert.Single(expired).LotDisplay);
    }

    [Fact]
    public void Ordenacao_VencidoHojeMenorValidadeSemValidadeProduto()
    {
        var rows = ValidityControlEngine.BuildRows(
        [
            Product("Zebra", lots: [Lot("Z", 40)]),
            Product("Antarctica", lots: [Lot("S", null), Lot("V", -2), Lot("H", 0), Lot("N", 5)]),
        ], Today);
        Assert.Equal(["V", "H", "N", "S", "Z"], rows.Select(r => r.LotDisplay).ToArray());
    }

    [Fact]
    public void QuantityZeroFora()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "Q0", "ZERO");
        Receive(pid, 4, "ATIVO", Today.AddDays(3));
        InsertLot(pid, "MORTO", Today.AddDays(3), 0);
        var snap = ValidityControlService.GetSnapshotLocal(Today);
        Assert.Equal("ATIVO", Assert.Single(snap.Rows).LotDisplay);
    }

    [Fact]
    public void LegacyExtraJsonIgnorado()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(8, 5, 2, "LEG", "LEGADO");
        SetExtra(pid, """{"data_validade":"2026-08-25","controle_validade":false}""");
        var snap = ValidityControlService.GetSnapshotLocal(Today);
        Assert.DoesNotContain(snap.Rows, r => r.ProductId == pid);
        Assert.Null(ProductExpiryService.GetNextExpiry(pid));
    }

    [Fact]
    public void SemValidade_LoteAtivoSemExpiry()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "NV", "SEM DATA");
        Receive(pid, 6, "L1", expiry: null);
        var row = Assert.Single(ValidityControlService.GetSnapshotLocal(Today).Rows);
        Assert.Equal(ProductExpiryStatusKind.Uninformed, row.Status.Kind);
        Assert.Equal("SEM VALIDADE", row.StatusDisplay);
        Assert.Equal(ValidityControlRowKind.UninformedLot, row.RowKind);
    }

    [Fact]
    public void ProdutoComControleSemLote_AlertaSemInventarLote()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(12, 5, 2, "NL", "SEM LOTE");
        SetExtra(pid, """{"controle_validade":true}""");
        var row = Assert.Single(ValidityControlService.GetSnapshotLocal(Today).Rows);
        Assert.Equal(pid, row.ProductId);
        Assert.Null(row.LotId);
        Assert.Equal(ValidityControlEngine.MissingExpiryLabel, row.StatusDisplay);
        Assert.Equal(12, row.Quantity);
        Assert.Null(row.ExpiryDate);
    }

    [Fact]
    public void EstoqueSemLoteIdentificado_NaoInventaValidade()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(10, 5, 2, "DIV", "DIVERGENTE");
        SetExtra(pid, """{"controle_validade":true}""");
        Receive(pid, 4, "COM DATA", Today.AddDays(20));
        var snap = ValidityControlService.GetSnapshotLocal(Today);
        Assert.Equal(2, snap.Rows.Count);
        var alert = Assert.Single(snap.Rows, r => r.RowKind == ValidityControlRowKind.UntrackedStock);
        Assert.Equal(ValidityControlEngine.UntrackedStockLabel, alert.StatusDisplay);
        Assert.Equal(6, alert.Quantity);
        Assert.Null(alert.ExpiryDate);
        Assert.Equal(Today.AddDays(20).Date, ProductExpiryService.GetNextExpiry(pid));
    }

    [Fact]
    public void ProximaValidadeCoerenteComLotes()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "NX", "NEXT");
        Receive(pid, 2, "FAR", Today.AddDays(40));
        Receive(pid, 3, "NEAR", Today.AddDays(5));
        Assert.Equal(Today.AddDays(5).Date, ProductExpiryService.GetNextExpiry(pid));
        var near = ValidityControlService.GetSnapshotLocal(Today).Rows
            .Where(r => r.ProductId == pid)
            .OrderBy(r => r.DaysRemaining)
            .First();
        Assert.Equal("NEAR", near.LotDisplay);
        Assert.Equal(5, near.DaysRemaining);
    }

    [Fact]
    public void HomeContador()
    {
        var cards = new ValidityControlCards { Expired = 1, Days7 = 3, Days15 = 2, Days30 = 5 };
        Assert.True(ValidityControlEngine.ShouldShowHomeAlert(cards));
        Assert.Equal("Validades: 1 vencido • 3 até 7 dias • 7 até 30 dias",
            ValidityControlEngine.FormatHomeSummary(cards));
        Assert.False(ValidityControlEngine.ShouldShowHomeAlert(new ValidityControlCards { Ok = 9 }));
    }

    [Fact]
    public void RefreshRecalcula()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "RF", "REFRESH");
        Receive(pid, 1, "A", Today.AddDays(2));
        Assert.Equal(1, ValidityControlService.GetSnapshotLocal(Today).Cards.Days7);
        Receive(pid, 1, "B", Today.AddDays(-1));
        var snap = ValidityControlService.GetSnapshotLocal(Today);
        Assert.Equal(1, snap.Cards.Expired);
        Assert.Equal(1, snap.Cards.Days7);
        Assert.Equal(2, snap.Rows.Count);
    }

    [Fact]
    public void Filtro7DiasNaoUsaRelatorioLegado()
    {
        Assert.Equal(ValidityControlFilterKind.Days7, ValidityControlService.FilterFromLegacyDays(7));
        Assert.Equal(ValidityControlFilterKind.All, ValidityControlService.FilterFromLegacyDays(null));
    }

    [Fact]
    public void RedeLoja_ClientUsaHost()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(0, 5, 2, "CL", "CLI");
        Receive(pid, 8, "LOCAL", Today.AddDays(2));

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures = ["session", ValidityControlService.Feature];
        StoreNetworkClient.TestGetValidityControl = () => new ValidityControlSnapshot
        {
            Rows =
            [
                new ValidityControlRow
                {
                    ProductId = pid,
                    ProductName = "HOST",
                    LotDisplay = "HOST",
                    Status = ProductExpiryService.Classify(Today.AddDays(2), Today),
                    StatusDisplay = "ATÉ 7 DIAS",
                },
            ],
            Cards = new ValidityControlCards { Days7 = 1 },
        };
        try
        {
            var snap = ValidityControlService.GetSnapshot(Today);
            Assert.Equal("HOST", Assert.Single(snap.Rows).LotDisplay);
            Assert.Equal(1, StoreNetworkClient.TestGetValidityControlSendCount);
            Assert.Equal("LOCAL", Assert.Single(ValidityControlService.GetSnapshotLocal(Today).Rows).LotDisplay);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void HostAntigo_FalhaClaro()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures = ["session", ProductExpiryService.LotsReadFeature];
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ValidityControlService.GetSnapshot());
            Assert.Equal(ValidityControlService.HostNeedsUpgradeMessage, ex.Message);
            Assert.Equal(0, StoreNetworkClient.TestGetValidityControlSendCount);
            Assert.Equal(1, StoreNetworkClient.TestStatusFetchCount);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void FeatureAnunciadaSemMudarApiVersion()
    {
        Assert.Contains(ValidityControlService.Feature, StoreNetworkHost.AdvertisedFeatures);
        Assert.Contains(ProductExpiryService.LotsReadFeature, StoreNetworkHost.AdvertisedFeatures);
    }

    [Fact]
    public void ProductLotsWindowContinuaFormatando()
    {
        var lot = new ProductLot
        {
            LotNumber = "X",
            Quantity = 2,
            ExpiryDateIso = Today.AddDays(7).ToString("yyyy-MM-dd"),
            PurchaseId = 3,
        };
        var row = Assert.Single(ProductLotListRow.FromLots([lot], Today));
        Assert.Equal("ATÉ 7 DIAS", row.StatusDisplay);
        Assert.True(ProductExpiryService.CanOpenLotsWindow(9));
    }

    [Fact]
    public void LoteComUnitCost_UsaLancadoNoLote()
    {
        var product = Product("P", [Lot("A", 5, qty: 10, unitCost: 2)], costPrice: 3);
        var row = Assert.Single(ValidityControlEngine.BuildRows([product], Today));
        Assert.Equal(2, row.UsedCost);
        Assert.Equal(LotCostSource.LotRecorded, row.CostSource);
        Assert.Equal(20, row.LotValue);
        Assert.Equal(2, row.UnitCost);
    }

    [Fact]
    public void LoteSemUnitCost_UsaMedioAtual()
    {
        var product = Product("P", [Lot("A", 5, qty: 10, unitCost: 0)], costPrice: 3);
        var row = Assert.Single(ValidityControlEngine.BuildRows([product], Today));
        Assert.Equal(3, row.UsedCost);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, row.CostSource);
        Assert.Equal(30, row.LotValue);
        Assert.Equal(0, row.UnitCost);
    }

    [Fact]
    public void LoteSemNenhumCusto_ValorNull()
    {
        var product = Product("P", [Lot("A", 5, qty: 10, unitCost: 0)], costPrice: 0);
        var row = Assert.Single(ValidityControlEngine.BuildRows([product], Today));
        Assert.Null(row.UsedCost);
        Assert.Equal(LotCostSource.Unavailable, row.CostSource);
        Assert.Null(row.LotValue);
        Assert.Equal("—", row.LotValueDisplay);
        Assert.NotEqual("—", row.CostDisplay);
    }

    [Fact]
    public void LeftoverComCostPrice_Estimado()
    {
        var product = Product("IOG", [], stock: 12, costPrice: 2.50, explicitExpiry: true);
        var row = Assert.Single(ValidityControlEngine.BuildRows([product], Today));
        Assert.Equal(ValidityControlRowKind.MissingExpiry, row.RowKind);
        Assert.Equal(12, row.Quantity);
        Assert.Equal(2.50, row.UsedCost);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, row.CostSource);
        Assert.Equal(30, row.LotValue);
        Assert.Equal(0, row.UnitCost);
    }

    [Fact]
    public void LeftoverSemCostPrice_SemCusto()
    {
        var product = Product("IOG", [], stock: 12, costPrice: 0, explicitExpiry: true);
        var row = Assert.Single(ValidityControlEngine.BuildRows([product], Today));
        Assert.Equal(ValidityControlRowKind.MissingExpiry, row.RowKind);
        Assert.Null(row.UsedCost);
        Assert.Equal(LotCostSource.Unavailable, row.CostSource);
        Assert.Null(row.LotValue);
    }

    [Fact]
    public void QuantidadeNegativa_NaoGeraValorNegativo()
    {
        var comCusto = ValidityControlEngine.ComputeLotValue(-8, 2.50);
        Assert.Equal(0, comCusto);
        Assert.True(comCusto >= 0);
        Assert.Null(ValidityControlEngine.ComputeLotValue(-8, null));
        var row = ValidityControlEngine.FromLot(
            Lot("NEG", 3, qty: -5, unitCost: 4),
            Product("P", [], costPrice: 9),
            Today);
        Assert.Equal(0, row.LotValue);
        Assert.Equal(4, row.UsedCost);
        Assert.Equal(LotCostSource.LotRecorded, row.CostSource);
    }

    [Fact]
    public void ValorDoLote_UsaRoundPrice()
    {
        const double qty = 3;
        const double unit = 1.115;
        var expected = ProductPriceHelper.RoundPrice(qty * unit);
        var product = Product("P", [Lot("A", 4, qty: qty, unitCost: unit)]);
        var row = Assert.Single(ValidityControlEngine.BuildRows([product], Today));
        Assert.Equal(expected, row.LotValue);
        Assert.Equal(expected, ValidityControlEngine.ComputeLotValue(qty, unit));
    }

    [Fact]
    public void MultiplosLotes_ValoresSeparados()
    {
        var product = Product("P",
        [
            Lot("L1", 5, qty: 10, unitCost: 2),
            Lot("L2", 20, qty: 30, unitCost: 4),
        ], costPrice: 9);
        var rows = ValidityControlEngine.BuildRows([product], Today);
        Assert.Equal(2, rows.Count);
        var l1 = Assert.Single(rows, r => r.LotDisplay == "L1");
        var l2 = Assert.Single(rows, r => r.LotDisplay == "L2");
        Assert.Equal(20, l1.LotValue);
        Assert.Equal(120, l2.LotValue);
        Assert.Equal(LotCostSource.LotRecorded, l1.CostSource);
        Assert.Equal(LotCostSource.LotRecorded, l2.CostSource);
    }

    [Fact]
    public void GeladeiraNaoEntraEmLeftoverNemValorDoLote()
    {
        var product = Product("P",
            [Lot("DEP", 10, qty: 70, unitCost: 2)],
            stock: 70,
            stockFridge: 30,
            costPrice: 2,
            explicitExpiry: true);
        var rows = ValidityControlEngine.BuildRows([product], Today);
        var lot = Assert.Single(rows);
        Assert.Equal(ValidityControlRowKind.Lot, lot.RowKind);
        Assert.Equal(70, lot.Quantity);
        Assert.Equal(140, lot.LotValue);
        Assert.Equal(30, lot.StockFridge);
        Assert.DoesNotContain(rows, r => r.RowKind == ValidityControlRowKind.UntrackedStock);
        Assert.DoesNotContain(rows, r => r.RowKind == ValidityControlRowKind.MissingExpiry);
    }

    [Fact]
    public void CigarroSemControle_NaoCriaAlertaPorAusenciaDeValidade()
    {
        var product = Product("MARLBORO BOX", [], stock: 80, costPrice: 10, explicitExpiry: false);
        Assert.Empty(ValidityControlEngine.BuildRows([product], Today));
    }

    [Fact]
    public void UntrackedStock_UsaMedioNaoCustoDeOutroLote()
    {
        var product = Product("P",
            [Lot("COM", 20, qty: 4, unitCost: 9)],
            stock: 10,
            costPrice: 2.50,
            explicitExpiry: true);
        var rows = ValidityControlEngine.BuildRows([product], Today);
        Assert.Equal(2, rows.Count);
        var lot = Assert.Single(rows, r => r.RowKind == ValidityControlRowKind.Lot);
        Assert.Equal(36, lot.LotValue);
        Assert.Equal(LotCostSource.LotRecorded, lot.CostSource);
        var leftover = Assert.Single(rows, r => r.RowKind == ValidityControlRowKind.UntrackedStock);
        Assert.Equal(6, leftover.Quantity);
        Assert.Equal(2.50, leftover.UsedCost);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, leftover.CostSource);
        Assert.Equal(15, leftover.LotValue);
        Assert.Equal(0, leftover.UnitCost);
    }

    [Fact]
    public void GeladeiraNoBanco_NaoGeraLeftover()
    {
        using var db = TempDatabase.Create();
        var pid = TestDataHelper.SeedSimpleProduct(70, 5, 2, "GF", "GELADEIRA");
        SetExtra(pid, """{"controle_validade":true}""");
        SetFridge(pid, 30);
        Receive(pid, 70, "DEP", Today.AddDays(10));
        var snap = ValidityControlService.GetSnapshotLocal(Today);
        var row = Assert.Single(snap.Rows, r => r.ProductId == pid);
        Assert.Equal("DEP", row.LotDisplay);
        Assert.Equal(30, row.StockFridge);
        Assert.Equal(1.5, row.UsedCost);
        Assert.Equal(LotCostSource.LotRecorded, row.CostSource);
        Assert.Equal(105, row.LotValue);
        Assert.DoesNotContain(snap.Rows, r => r.RowKind is ValidityControlRowKind.UntrackedStock
            or ValidityControlRowKind.MissingExpiry);
        Assert.Equal(ValiditySuggestedAction.Monitor, row.SuggestedAction);
        Assert.Equal(105, row.LotValue);
    }

    [Fact]
    public void VencidoComSaldo_RemoveExpired()
    {
        var row = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("V", -2, qty: 12, unitCost: 2)])], Today));
        Assert.Equal(ValiditySuggestedAction.RemoveExpired, row.SuggestedAction);
        Assert.Equal(0, row.AttentionRank);
        Assert.Equal(24, row.LotValue);
        Assert.Contains("vencido", row.SuggestedActionReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promoção", row.SuggestedActionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VencidoComAltoValor_ContinuaRemoveExpired()
    {
        var row = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("V", -1, qty: 100, unitCost: 50)])], Today));
        Assert.Equal(5000, row.LotValue);
        Assert.Equal(ValiditySuggestedAction.RemoveExpired, row.SuggestedAction);
        Assert.NotEqual(ValiditySuggestedAction.ConsiderPromotion, row.SuggestedAction);
        Assert.NotEqual(ValiditySuggestedAction.PrioritizeSale, row.SuggestedAction);
    }

    [Fact]
    public void VencidoNuncaSugerePromocao()
    {
        var action = ValidityControlEngine.ResolveSuggestedAction(
            ValidityControlRowKind.Lot, ProductExpiryStatusKind.Expired, 10);
        Assert.Equal(ValiditySuggestedAction.RemoveExpired, action);
        Assert.NotEqual(ValiditySuggestedAction.ConsiderPromotion, action);
        Assert.NotEqual(ValiditySuggestedAction.PrioritizeSale, action);
    }

    [Fact]
    public void FaixaCriticaValida_PriorizarSaida()
    {
        var today = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("H", 0, qty: 4, unitCost: 2)])], Today));
        Assert.Equal(ProductExpiryStatusKind.Today, today.Status.Kind);
        Assert.Equal(ValiditySuggestedAction.PrioritizeSale, today.SuggestedAction);
        Assert.Equal("Vence hoje. Priorizar saída.", today.SuggestedActionReason);
        Assert.DoesNotContain("promoção", today.SuggestedActionReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desconto", today.SuggestedActionReason, StringComparison.OrdinalIgnoreCase);

        var week = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("N", 6, qty: 4, unitCost: 2)])], Today));
        Assert.Equal(ProductExpiryStatusKind.Within7, week.Status.Kind);
        Assert.Equal(ValiditySuggestedAction.PrioritizeSale, week.SuggestedAction);
        Assert.Equal("Validade em 6 dias. Priorizar saída.", week.SuggestedActionReason);
    }

    [Fact]
    public void FaixaIntermediaria_Monitorar()
    {
        var d15 = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("Q", 10, qty: 10, unitCost: 3)])], Today));
        Assert.Equal(ProductExpiryStatusKind.Within15, d15.Status.Kind);
        Assert.Equal(ValiditySuggestedAction.Monitor, d15.SuggestedAction);

        var d30 = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("M", 20, qty: 10, unitCost: 3)])], Today));
        Assert.Equal(ProductExpiryStatusKind.Within30, d30.Status.Kind);
        Assert.Equal(ValiditySuggestedAction.Monitor, d30.SuggestedAction);
        Assert.Equal(30, d30.LotValue);
        Assert.Equal("Validade em 20 dias. Acompanhar saída.", d30.SuggestedActionReason);
        Assert.DoesNotContain("promoção", d30.SuggestedActionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForaDeAtencao_NenhumaAcao()
    {
        var row = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("OK", 120)])], Today));
        Assert.Equal(ProductExpiryStatusKind.Ok, row.Status.Kind);
        Assert.Equal(ValiditySuggestedAction.None, row.SuggestedAction);
        Assert.Equal(5, row.AttentionRank);
        Assert.Equal("", row.SuggestedActionReason);
        Assert.Equal("—", row.SuggestedActionDisplay);
    }

    [Fact]
    public void MissingExpiry_ReviewData()
    {
        var row = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("IOG", [], stock: 12, costPrice: 2.50, explicitExpiry: true)], Today));
        Assert.Equal(ValidityControlRowKind.MissingExpiry, row.RowKind);
        Assert.Equal(ValiditySuggestedAction.ReviewData, row.SuggestedAction);
        Assert.Equal("Estoque sem validade identificada.", row.SuggestedActionReason);
        Assert.Equal(30, row.LotValue);
    }

    [Fact]
    public void UntrackedStock_ReviewData()
    {
        var product = Product("P",
            [Lot("COM", 20, qty: 4, unitCost: 9)],
            stock: 10,
            costPrice: 2.50,
            explicitExpiry: true);
        var leftover = Assert.Single(
            ValidityControlEngine.BuildRows([product], Today),
            r => r.RowKind == ValidityControlRowKind.UntrackedStock);
        Assert.Equal(ValiditySuggestedAction.ReviewData, leftover.SuggestedAction);
        Assert.Equal("Estoque sem lote identificado.", leftover.SuggestedActionReason);
        Assert.Null(leftover.ExpiryDate);
    }

    [Fact]
    public void MesmaFaixa_MaiorLotValuePrimeiro()
    {
        var rows = ValidityControlEngine.BuildRows(
        [
            Product("A", [Lot("BAIXO", 5, qty: 10, unitCost: 5)], productId: 1),
            Product("B", [Lot("ALTO", 6, qty: 10, unitCost: 50)], productId: 2),
        ], Today);
        Assert.Equal(["ALTO", "BAIXO"], rows.Select(r => r.LotDisplay).ToArray());
        Assert.All(rows, r => Assert.Equal(ValiditySuggestedAction.PrioritizeSale, r.SuggestedAction));
    }

    [Fact]
    public void MesmaFaixa_LotValueNullNaoQuebraOrdenacao()
    {
        var rows = ValidityControlEngine.BuildRows(
        [
            Product("A", [Lot("SEM", 5, qty: 10, unitCost: 0)], costPrice: 0, productId: 1),
            Product("B", [Lot("COM", 6, qty: 10, unitCost: 4)], productId: 2),
        ], Today);
        Assert.Equal("COM", rows[0].LotDisplay);
        Assert.Equal(40, rows[0].LotValue);
        Assert.Equal("SEM", rows[1].LotDisplay);
        Assert.Null(rows[1].LotValue);
        Assert.Equal(LotCostSource.Unavailable, rows[1].CostSource);
    }

    [Fact]
    public void MesmaFaixaMesmoValor_QuantidadeDesempata()
    {
        var rows = ValidityControlEngine.BuildRows(
        [
            Product("A", [Lot("POUCO", 5, qty: 2, unitCost: 10)], productId: 1),
            Product("B", [Lot("MUITO", 6, qty: 20, unitCost: 1)], productId: 2),
        ], Today);
        Assert.Equal(20, rows[0].LotValue);
        Assert.Equal(20, rows[1].LotValue);
        Assert.Equal("MUITO", rows[0].LotDisplay);
        Assert.Equal("POUCO", rows[1].LotDisplay);
    }

    [Fact]
    public void DesempateFinalDeterministico()
    {
        var rows = ValidityControlEngine.BuildRows(
        [
            Product("Zeta", [Lot("L2", 5, qty: 10, unitCost: 2)], productId: 20),
            Product("Alfa", [Lot("L1", 5, qty: 10, unitCost: 2)], productId: 10),
        ], Today);
        Assert.Equal(2, rows.Count);
        Assert.Equal(10, rows[0].ProductId);
        Assert.Equal(20, rows[1].ProductId);
        var again = ValidityControlEngine.BuildRows(
        [
            Product("Alfa", [Lot("L1", 5, qty: 10, unitCost: 2)], productId: 10),
            Product("Zeta", [Lot("L2", 5, qty: 10, unitCost: 2)], productId: 20),
        ], Today);
        Assert.Equal(rows.Select(r => r.ProductId), again.Select(r => r.ProductId));
    }

    [Fact]
    public void VencidoBarato_AcimaDeValidoCaro()
    {
        var rows = ValidityControlEngine.BuildRows(
        [
            Product("Caro", [Lot("VALIDO", 3, qty: 10, unitCost: 100)], productId: 1),
            Product("Barato", [Lot("VENCIDO", -1, qty: 10, unitCost: 1)], productId: 2),
        ], Today);
        Assert.Equal("VENCIDO", rows[0].LotDisplay);
        Assert.Equal(10, rows[0].LotValue);
        Assert.Equal(ValiditySuggestedAction.RemoveExpired, rows[0].SuggestedAction);
        Assert.Equal("VALIDO", rows[1].LotDisplay);
        Assert.Equal(1000, rows[1].LotValue);
        Assert.Equal(ValiditySuggestedAction.PrioritizeSale, rows[1].SuggestedAction);
    }

    [Fact]
    public void AcaoSugeridaNaoAlteraLotValue()
    {
        var product = Product("P", [Lot("V", -1, qty: 10, unitCost: 2)], costPrice: 9);
        var row = Assert.Single(ValidityControlEngine.BuildRows([product], Today));
        Assert.Equal(20, row.LotValue);
        Assert.Equal(ValiditySuggestedAction.RemoveExpired, row.SuggestedAction);
        Assert.Equal(LotCostSource.LotRecorded, row.CostSource);
        Assert.Equal(2, row.UsedCost);
    }

    [Fact]
    public void CustoEstimadoContinuaEstimativa_ComAcao()
    {
        var row = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("A", 4, qty: 10, unitCost: 0)], costPrice: 3)], Today));
        Assert.Equal(LotCostSource.CurrentAverageEstimate, row.CostSource);
        Assert.Equal(30, row.LotValue);
        Assert.Equal(ValiditySuggestedAction.PrioritizeSale, row.SuggestedAction);
    }

    [Fact]
    public void MonitorNaFaixa60()
    {
        var row = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("L", 40)])], Today));
        Assert.Equal(ProductExpiryStatusKind.Within60, row.Status.Kind);
        Assert.Equal(ValiditySuggestedAction.Monitor, row.SuggestedAction);
        Assert.Equal(4, row.AttentionRank);
    }

    [Fact]
    public void MonitorNaFaixa90()
    {
        var row = Assert.Single(ValidityControlEngine.BuildRows(
            [Product("P", [Lot("L", 70)])], Today));
        Assert.Equal(ProductExpiryStatusKind.Within90, row.Status.Kind);
        Assert.Equal(ValiditySuggestedAction.Monitor, row.SuggestedAction);
    }

    [Fact]
    public void NenhumaLinhaDo70B2_EmiteConsiderPromotion()
    {
        var products = new[]
        {
            Product("E", [Lot("E", -1, qty: 2, unitCost: 1)], productId: 1),
            Product("H", [Lot("H", 0, qty: 2, unitCost: 1)], productId: 2),
            Product("S7", [Lot("S7", 5, qty: 2, unitCost: 1)], productId: 3),
            Product("S15", [Lot("S15", 10, qty: 2, unitCost: 1)], productId: 4),
            Product("S30", [Lot("S30", 20, qty: 2, unitCost: 1)], productId: 5),
            Product("S60", [Lot("S60", 40, qty: 2, unitCost: 1)], productId: 6),
            Product("S90", [Lot("S90", 70, qty: 2, unitCost: 1)], productId: 7),
            Product("OK", [Lot("OK", 120, qty: 2, unitCost: 1)], productId: 8),
            Product("MISS", [], stock: 5, costPrice: 2, explicitExpiry: true, productId: 9),
            Product("UNT", [Lot("L", 20, qty: 1, unitCost: 1)], stock: 5, costPrice: 2,
                explicitExpiry: true, productId: 10),
            Product("NODATE", [Lot("X", null, qty: 3)], productId: 11),
        };
        var rows = ValidityControlEngine.BuildRows(products, Today);
        Assert.NotEmpty(rows);
        Assert.DoesNotContain(rows, r => r.SuggestedAction == ValiditySuggestedAction.ConsiderPromotion);

        Assert.Equal(ValiditySuggestedAction.RemoveExpired,
            Assert.Single(rows, r => r.LotDisplay == "E").SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.PrioritizeSale,
            Assert.Single(rows, r => r.LotDisplay == "H").SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.PrioritizeSale,
            Assert.Single(rows, r => r.LotDisplay == "S7").SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.Monitor,
            Assert.Single(rows, r => r.LotDisplay == "S15").SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.Monitor,
            Assert.Single(rows, r => r.LotDisplay == "S30").SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.Monitor,
            Assert.Single(rows, r => r.LotDisplay == "S60").SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.Monitor,
            Assert.Single(rows, r => r.LotDisplay == "S90").SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.None,
            Assert.Single(rows, r => r.LotDisplay == "OK").SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.ReviewData,
            Assert.Single(rows, r => r.RowKind == ValidityControlRowKind.MissingExpiry).SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.ReviewData,
            Assert.Single(rows, r => r.RowKind == ValidityControlRowKind.UntrackedStock).SuggestedAction);
        Assert.Equal(ValiditySuggestedAction.ReviewData,
            Assert.Single(rows, r => r.RowKind == ValidityControlRowKind.UninformedLot).SuggestedAction);
    }

    [Fact]
    public void MultiplosLotes_AcoesIndependentes()
    {
        var rows = ValidityControlEngine.BuildRows(
        [
            Product("P",
            [
                Lot("VENC", -2, qty: 5, unitCost: 2),
                Lot("SAIDA", 4, qty: 5, unitCost: 2),
            ], costPrice: 9),
        ], Today);
        Assert.Equal(2, rows.Count);
        var venc = Assert.Single(rows, r => r.LotDisplay == "VENC");
        var saida = Assert.Single(rows, r => r.LotDisplay == "SAIDA");
        Assert.Equal(ValiditySuggestedAction.RemoveExpired, venc.SuggestedAction);
        Assert.Equal(10, venc.LotValue);
        Assert.Equal(ValiditySuggestedAction.PrioritizeSale, saida.SuggestedAction);
        Assert.Equal(10, saida.LotValue);
        Assert.Equal(LotCostSource.LotRecorded, venc.CostSource);
        Assert.Equal(LotCostSource.LotRecorded, saida.CostSource);
    }

    static ValidityControlProductInput Product(
        string name,
        ProductLot[] lots,
        double stock = 0,
        double stockFridge = 0,
        double costPrice = 0,
        bool explicitExpiry = false,
        int productId = 1) =>
        new()
        {
            ProductId = productId,
            Code = "C",
            Name = name,
            Stock = stock,
            StockFridge = stockFridge,
            CostPrice = costPrice,
            ExplicitExpiryControl = explicitExpiry,
            Lots = lots,
        };

    static ProductLot Lot(string number, int? days, double qty = 1, double unitCost = 0) =>
        new()
        {
            Id = number.GetHashCode(StringComparison.Ordinal),
            LotNumber = number,
            Quantity = qty,
            UnitCost = unitCost,
            ExpiryDateIso = days is int d ? Today.AddDays(d).ToString("yyyy-MM-dd") : null,
        };

    static void Receive(int productId, double qty, string lot, DateTime? expiry) =>
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = qty,
            LotNumber = lot,
            ExpiryDate = expiry,
            UnitCost = 1.5,
            PurchaseId = 11,
        });

    static void SetFridge(int productId, double fridge)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock_fridge = $f WHERE id = $id;";
        cmd.Parameters.AddWithValue("$f", fridge);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    static void SetExtra(int productId, string json)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET extra_json = $j WHERE id = $id;";
        cmd.Parameters.AddWithValue("$j", json);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    static void InsertLot(int productId, string lot, DateTime expiry, double qty)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO product_lots (product_id, lot_number, expiry_date, quantity, unit_cost)
            VALUES ($p, $l, $e, $q, 1);
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        cmd.Parameters.AddWithValue("$l", lot);
        cmd.Parameters.AddWithValue("$e", expiry.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$q", qty);
        cmd.ExecuteNonQuery();
    }
}
