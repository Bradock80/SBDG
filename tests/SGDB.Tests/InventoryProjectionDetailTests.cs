using System.Globalization;
using System.IO;
using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// 70D-B5B — contrato em memória do detalhe de projeção.
/// Sem WPF, sem Load/GetById/ListByProduct, sem recálculo FEFO.
/// </summary>
public class InventoryProjectionDetailTests
{
    private static ProductTurnoverRow Turnover(
        int id = 1,
        string name = "Leite",
        string code = "B4B01",
        double stock = 40,
        double fridge = 0,
        double total = 40,
        double vmv30 = 1) =>
        new()
        {
            ProductId = id,
            Name = name,
            Code = code,
            Stock = stock,
            StockFridge = fridge,
            TotalStock = total,
            Vmv30 = vmv30,
            CoverageDays = 40,
            CoverageBand = InventoryCoverageBand.Normal,
            HistoryDays = 45,
            HasPhysicalAvailabilityEvidence = true,
        };

    private static InventoryProjectionLotResult Lot(
        int id,
        InventoryProjectionLotKind kind,
        double qty,
        double? surplus = null,
        double? value = null,
        DateTime? expiry = null,
        int? days = null) =>
        new()
        {
            LotId = id,
            Kind = kind,
            Quantity = qty,
            ExpiryDate = expiry,
            DaysUntilExpiry = days,
            AlreadyExpired = kind == InventoryProjectionLotKind.AlreadyExpired,
            ProjectedSurplusAtExpiry = surplus,
            ProjectedSurplusValue = value,
        };

    private static InventoryProjectedLotCost Cost(
        int lotId,
        LotCostSource source,
        double? used = null) =>
        new()
        {
            LotId = lotId,
            CostSource = source,
            UsedCost = used,
        };

    private static InventoryProjectedLotIdentity Identity(int lotId, string? number) =>
        new() { LotId = lotId, LotNumber = number };

    private static InventoryProjectedProduct Product(
        int id = 1,
        InventorySkuProjectionBlockedReason sku = InventorySkuProjectionBlockedReason.None,
        InventoryExpiryProjectionBlockedReason expiry = InventoryExpiryProjectionBlockedReason.None,
        double? demand = 30,
        double? excess = 10,
        double tracked = 40,
        double untracked = 0,
        bool fridge = false,
        IReadOnlyList<InventoryProjectionLotResult>? lots = null,
        IReadOnlyList<InventoryProjectedLotCost>? costs = null,
        IReadOnlyList<InventoryProjectedLotIdentity>? identities = null) =>
        new()
        {
            ProductId = id,
            Projection = new InventoryProjectionResult
            {
                SkuBlockedReason = sku,
                ExpiryBlockedReason = expiry,
                HorizonDays = 30,
                ProjectedDemand = sku == InventorySkuProjectionBlockedReason.None ? demand : null,
                ProjectedExcessQuantity = sku == InventorySkuProjectionBlockedReason.None ? excess : null,
                TrackedLotQuantity = tracked,
                UntrackedWarehouseQuantity = untracked,
                HasLotLocationLimitation = fridge,
                Lots = lots ?? [],
            },
            LotCosts = costs ?? [],
            LotIdentities = identities ?? [],
        };

    private static InventoryProjectedProductPresentation Present(InventoryProjectedProduct product, ProductTurnoverRow? row = null) =>
        InventoryProjectionPresentation.FromProduct(product, row);

    private static InventoryProjectionSnapshot Snapshot(ProductTurnoverRow row, InventoryProjectedProduct product) =>
        new()
        {
            Today = new DateTime(2026, 9, 1),
            QueryCount = 7,
            Intelligence = new InventoryIntelligenceSnapshot
            {
                Today = new DateTime(2026, 9, 1),
                QueryCount = 6,
                Rows = [row],
            },
            ByProductId = new Dictionary<int, InventoryProjectedProduct> { [row.ProductId] = product },
        };

    private static InventoryProjectionDetail RequireDetail(
        InventoryProjectedProduct product,
        ProductTurnoverRow? row = null)
    {
        row ??= Turnover(product.ProductId);
        var snap = Snapshot(row, product);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var detail = InventoryProjectionDetail.TryCreate(snap, presented, row.ProductId);
        Assert.NotNull(detail);
        return detail!;
    }

    [Fact]
    public void Filled_lot_number_is_displayed_and_differs_from_lot_id()
    {
        var lot = Assert.Single(Present(Product(
            lots: [Lot(42, InventoryProjectionLotKind.Dated, 30, surplus: 10)],
            identities: [Identity(42, "ABC-99")])).Lots);
        Assert.Equal(42, lot.LotId);
        Assert.Equal("ABC-99", lot.LotNumber);
        Assert.Equal("ABC-99", lot.LotNumberDisplay);
        Assert.NotEqual(lot.LotId.ToString(CultureInfo.InvariantCulture), lot.LotNumberDisplay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_lot_number_displays_em_dash_not_lot_id(string? number)
    {
        var lot = Assert.Single(Present(Product(
            lots: [Lot(7, InventoryProjectionLotKind.Dated, 30, surplus: 10)],
            identities: number is null ? [] : [Identity(7, number)])).Lots);
        Assert.Equal(7, lot.LotId);
        Assert.Null(lot.LotNumber);
        Assert.Equal(InventoryProjectionPresentation.EmDash, lot.LotNumberDisplay);
        Assert.NotEqual("7", lot.LotNumberDisplay);
    }

    [Fact]
    public void Format_lot_number_trims_and_does_not_invent()
    {
        Assert.Equal("L-1", InventoryProjectionPresentation.FormatLotNumber(" L-1 "));
        Assert.Equal(InventoryProjectionPresentation.EmDash, InventoryProjectionPresentation.FormatLotNumber(null));
        Assert.Equal(InventoryProjectionPresentation.EmDash, InventoryProjectionPresentation.FormatLotNumber(""));
        Assert.Equal(InventoryProjectionPresentation.EmDash, InventoryProjectionPresentation.FormatLotNumber("   "));
    }

    [Fact]
    public void Consumption_30_minus_10_is_20()
    {
        var value = InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated, 30, 10, InventoryExpiryProjectionBlockedReason.None);
        Assert.Equal(20, value);
        var lot = Assert.Single(Present(Product(
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 30, surplus: 10)])).Lots);
        Assert.Equal(20, lot.ProjectedConsumptionUntilExpiry);
        Assert.Equal("20", lot.ProjectedConsumptionUntilExpiryDisplay);
    }

    [Fact]
    public void Consumption_30_minus_0_is_30()
    {
        var value = InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated, 30, 0, InventoryExpiryProjectionBlockedReason.None);
        Assert.Equal(30, value);
        var lot = Assert.Single(Present(Product(
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 30, surplus: 0)])).Lots);
        Assert.Equal(30, lot.ProjectedConsumptionUntilExpiry);
        Assert.Equal("30", lot.ProjectedConsumptionUntilExpiryDisplay);
    }

    [Fact]
    public void Missing_surplus_is_unavailable_not_zero()
    {
        var value = InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated, 30, null, InventoryExpiryProjectionBlockedReason.None);
        Assert.Null(value);
        var lot = Assert.Single(Present(Product(
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 30)])).Lots);
        Assert.Null(lot.ProjectedConsumptionUntilExpiry);
        Assert.Equal(InventoryProjectionPresentation.EmDash, lot.ProjectedConsumptionUntilExpiryDisplay);
    }

    [Fact]
    public void Expired_lot_consumption_is_unavailable()
    {
        var value = InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.AlreadyExpired, 12, 12, InventoryExpiryProjectionBlockedReason.None);
        Assert.Null(value);
        var lot = Assert.Single(Present(Product(
            lots: [Lot(1, InventoryProjectionLotKind.AlreadyExpired, 12)])).Lots);
        Assert.Null(lot.ProjectedConsumptionUntilExpiry);
        Assert.Equal(InventoryProjectionPresentation.EmDash, lot.ProjectedConsumptionUntilExpiryDisplay);
    }

    [Fact]
    public void Expires_today_consumption_is_unavailable()
    {
        Assert.Null(InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.ExpiresToday, 8, 0, InventoryExpiryProjectionBlockedReason.None));
        var lot = Assert.Single(Present(Product(
            lots: [Lot(1, InventoryProjectionLotKind.ExpiresToday, 8, days: 0)])).Lots);
        Assert.Null(lot.ProjectedConsumptionUntilExpiry);
        Assert.Equal(InventoryProjectionPresentation.EmDash, lot.ProjectedConsumptionUntilExpiryDisplay);
    }

    [Fact]
    public void Undated_consumption_is_unavailable()
    {
        Assert.Null(InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Undated, 15, 5, InventoryExpiryProjectionBlockedReason.None));
        var lot = Assert.Single(Present(Product(
            lots: [Lot(1, InventoryProjectionLotKind.Undated, 15)])).Lots);
        Assert.Null(lot.ProjectedConsumptionUntilExpiry);
        Assert.Equal(InventoryProjectionPresentation.EmDash, lot.ProjectedConsumptionUntilExpiryDisplay);
    }

    [Fact]
    public void Blocked_expiry_makes_consumption_unavailable()
    {
        Assert.Null(InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated,
            30,
            10,
            InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse));
        var lot = Assert.Single(Present(Product(
            expiry: InventoryExpiryProjectionBlockedReason.InsufficientHistory,
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 30, surplus: 10)])).Lots);
        Assert.Null(lot.ProjectedConsumptionUntilExpiry);
        Assert.Equal(InventoryProjectionPresentation.EmDash, lot.ProjectedConsumptionUntilExpiryDisplay);
    }

    [Fact]
    public void Consumption_is_never_negative_or_non_finite()
    {
        Assert.Equal(0, InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated, 10, 12, InventoryExpiryProjectionBlockedReason.None));
        Assert.Null(InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated, double.NaN, 1, InventoryExpiryProjectionBlockedReason.None));
        Assert.Null(InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated, 10, double.PositiveInfinity, InventoryExpiryProjectionBlockedReason.None));
        Assert.Null(InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated, 10, double.NegativeInfinity, InventoryExpiryProjectionBlockedReason.None));
        Assert.Null(InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated, -1, 0, InventoryExpiryProjectionBlockedReason.None));
        Assert.Null(InventoryProjectionPresentation.ProjectedConsumptionUntilExpiry(
            InventoryProjectionLotKind.Dated, 10, -0.5, InventoryExpiryProjectionBlockedReason.None));
    }

    [Fact]
    public void Detail_receives_70c_70d_lots_alerts_tracking_and_fridge_warning()
    {
        var row = Turnover(3, "Iogurte", "B4B11", stock: 20, fridge: 20, total: 40);
        var product = Product(
            id: 3,
            untracked: 5,
            fridge: true,
            lots:
            [
                Lot(8, InventoryProjectionLotKind.Undated, 2),
                Lot(9, InventoryProjectionLotKind.Dated, 10, surplus: 4, value: 8, expiry: new DateTime(2026, 10, 1), days: 30),
                Lot(1, InventoryProjectionLotKind.AlreadyExpired, 3),
            ],
            costs: [Cost(9, LotCostSource.LotRecorded, 2)],
            identities:
            [
                Identity(8, "U1"),
                Identity(9, "D1"),
                Identity(1, "E1"),
            ]);
        var detail = RequireDetail(product, row);

        Assert.Same(row, detail.Intelligence);
        Assert.Equal("Iogurte", detail.Intelligence.Name);
        Assert.Equal(20, detail.Intelligence.StockFridge);
        Assert.Equal(3, detail.Projection.ProductId);
        Assert.Equal("10", detail.Projection.Surplus30Display);
        Assert.Equal(3, detail.Lots.Count);
        Assert.Equal(8, detail.Lots[0].LotId);
        Assert.Equal("U1", detail.Lots[0].LotNumberDisplay);
        Assert.Equal(9, detail.Lots[1].LotId);
        Assert.Equal("D1", detail.Lots[1].LotNumberDisplay);
        Assert.Equal(1, detail.Lots[2].LotId);
        Assert.Equal(5, detail.UntrackedWarehouseQuantity);
        Assert.True(detail.HasUntrackedWarehouse);
        Assert.Contains("sem lote identificado", detail.UntrackedWarehouseAlert, StringComparison.Ordinal);
        Assert.True(detail.HasLotLocationLimitation);
        Assert.Equal(InventoryProjectionPresentation.FridgeLimitationText, detail.FridgeLimitationAlert);
        Assert.DoesNotContain("está na geladeira", detail.FridgeLimitationAlert, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lote no depósito", string.Join(' ', detail.Alerts), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(detail.Alerts, a => a == detail.FridgeLimitationAlert);
        Assert.Equal("Custo do lote", detail.Lots[1].CostSourceDisplay);
        Assert.Equal(8, detail.Lots[1].ProjectedSurplusValue);
    }

    [Fact]
    public void Missing_selection_returns_null()
    {
        var row = Turnover(1);
        var snap = Snapshot(row, Product());
        var presented = InventoryProjectionPresentation.Apply(snap);
        Assert.Null(InventoryProjectionDetail.TryCreate(snap, presented, 99));
        Assert.Null(InventoryProjectionDetail.TryCreate(snap, presented, 0));
        Assert.Null(InventoryProjectionDetail.TryCreate(null, presented, 1));
    }

    [Fact]
    public void Recorded_estimated_partial_and_unavailable_costs_are_preserved()
    {
        var recorded = Present(Product(
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 10, surplus: 4, value: 8)],
            costs: [Cost(1, LotCostSource.LotRecorded, 2)]));
        Assert.Equal(InventoryProjectionSurplusValueQuality.CompleteRecorded, recorded.SurplusValueQuality);
        Assert.Equal("Custo do lote", recorded.SurplusValueQualityDisplay);
        Assert.Equal("Custo do lote", Assert.Single(recorded.Lots).CostSourceDisplay);

        var estimated = Present(Product(
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 10, surplus: 4, value: 8)],
            costs: [Cost(1, LotCostSource.CurrentAverageEstimate, 2)]));
        Assert.Equal(InventoryProjectionSurplusValueQuality.CompleteWithEstimate, estimated.SurplusValueQuality);
        Assert.Equal("Estimado pelo custo médio atual", estimated.SurplusValueQualityDisplay);
        Assert.EndsWith("*", estimated.SurplusValueDisplay, StringComparison.Ordinal);

        var partial = Present(Product(
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Dated, 10, surplus: 4, value: 8),
                Lot(2, InventoryProjectionLotKind.Dated, 10, surplus: 3, value: null),
            ],
            costs:
            [
                Cost(1, LotCostSource.LotRecorded, 2),
                Cost(2, LotCostSource.Unavailable),
            ]));
        Assert.Equal(InventoryProjectionSurplusValueQuality.Partial, partial.SurplusValueQuality);
        Assert.Equal("Valor parcial", partial.SurplusValueQualityDisplay);
        Assert.Contains("parcial", partial.SurplusValueDisplay, StringComparison.OrdinalIgnoreCase);

        var unavailable = Present(Product(
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 10, surplus: 4)],
            costs: [Cost(1, LotCostSource.Unavailable)]));
        Assert.Equal(InventoryProjectionSurplusValueQuality.Unavailable, unavailable.SurplusValueQuality);
        Assert.Equal("Sem custo disponível", unavailable.SurplusValueQualityDisplay);
        Assert.Equal(InventoryProjectionPresentation.EmDash, unavailable.SurplusValueDisplay);
    }

    [Fact]
    public void Detail_has_no_thirty_day_money_and_no_invented_location()
    {
        var detail = RequireDetail(Product(
            fridge: true,
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 30, surplus: 10, value: 20)],
            costs: [Cost(1, LotCostSource.LotRecorded, 2)]));
        Assert.Null(typeof(InventoryProjectedProductPresentation).GetProperty("ProjectedExcessValue"));
        Assert.Null(typeof(InventoryProjectedLotPresentation).GetProperty("Location"));
        Assert.NotNull(detail.Projection.ProjectedExpirySurplusValue);
        Assert.Equal("Valor estimado da sobra", detail.Projection.SurplusValueCaption);
        Assert.DoesNotContain("prejuízo", detail.Projection.SurplusValueCaption, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("perda", detail.Projection.SurplusValueDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blocked_reasons_reuse_b3_texts()
    {
        var detail = RequireDetail(Product(
            sku: InventorySkuProjectionBlockedReason.NoObservableDemand,
            expiry: InventoryExpiryProjectionBlockedReason.InvalidExpiryDate));
        Assert.Equal(InventorySkuProjectionBlockedReason.NoObservableDemand, detail.Projection.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.InvalidExpiryDate, detail.Projection.ExpiryBlockedReason);
        Assert.Equal(
            InventoryProjectionPresentation.SkuBlockedText(InventorySkuProjectionBlockedReason.NoObservableDemand).Explanation,
            detail.Projection.SkuBlockedExplanation);
        Assert.Equal(
            InventoryProjectionPresentation.ExpiryBlockedText(InventoryExpiryProjectionBlockedReason.InvalidExpiryDate).Explanation,
            detail.Projection.ExpiryBlockedExplanation);
    }

    [Fact]
    public void Detail_and_presentation_sources_have_no_io_or_engine()
    {
        foreach (var file in new[] { "InventoryProjectionDetail.cs", "InventoryProjectionPresentation.cs" })
        {
            var source = ReadModel(file);
            Assert.DoesNotContain("InventoryProjectionService.Load", source, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryIntelligenceService.Load", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ListByProduct", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetByProductId", source, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryProjectionEngine.Project", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ConsumeDatedFefo", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ProjectedExcessValue", source, StringComparison.Ordinal);
            Assert.DoesNotContain("prejuízo certo", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("perda garantida", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lote no depósito", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lote na geladeira", source, StringComparison.OrdinalIgnoreCase);
        }

        var detail = ReadModel("InventoryProjectionDetail.cs");
        Assert.DoesNotContain("ConsumeDatedFefo", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", detail, StringComparison.Ordinal);

        var view = ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml");
        var viewCs = ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml.cs");
        Assert.DoesNotContain("Detalhar projeção", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InventoryProjectionDetail", viewCs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionWindow", viewCs, StringComparison.Ordinal);
    }

    private static string ReadModel(string fileName) =>
        ReadSource("src", "SGDB.App", "Models", fileName);

    private static string ReadSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        return "";
    }
}
