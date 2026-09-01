using System.Globalization;
using System.IO;
using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// 70D-B4A — composição 70C + 70D em memória. Sem WPF, Load, XAML ou motor.
/// </summary>
public class InventoryIntelligenceProjectionPresentationTests
{
    private static ProductTurnoverRow Turnover(
        int id,
        string name = "Produto",
        string code = "P",
        double stock = 10,
        double fridge = 0,
        double total = 10,
        double vmv30 = 1,
        double? coverageDays = 20,
        InventoryCoverageBand band = InventoryCoverageBand.Normal) =>
        new()
        {
            ProductId = id,
            Name = name,
            Code = code,
            Stock = stock,
            StockFridge = fridge,
            TotalStock = total,
            Vmv30 = vmv30,
            CoverageDays = coverageDays,
            CoverageBand = band,
            HistoryDays = 45,
            HasPhysicalAvailabilityEvidence = true,
        };

    private static InventoryIntelligenceGridRow Giro(ProductTurnoverRow row) =>
        InventoryIntelligencePresentation.ToGridRow(row);

    private static InventoryProjectedProductPresentation Proj(
        int id,
        InventorySkuProjectionBlockedReason sku = InventorySkuProjectionBlockedReason.None,
        InventoryExpiryProjectionBlockedReason expiry = InventoryExpiryProjectionBlockedReason.None,
        double? excess = 0,
        double? demand = 30,
        IReadOnlyList<InventoryProjectionLotResult>? lots = null,
        double tracked = 0,
        double untracked = 0) =>
        InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
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
                Lots = lots ?? [],
            },
        });

    private static InventoryProjectionLotResult Lot(
        int lotId,
        InventoryProjectionLotKind kind,
        double qty,
        double? surplus = null) =>
        new()
        {
            LotId = lotId,
            Kind = kind,
            Quantity = qty,
            AlreadyExpired = kind == InventoryProjectionLotKind.AlreadyExpired,
            ProjectedSurplusAtExpiry = surplus,
        };

    private static InventoryProjectionPresentationSnapshot Snap(
        params InventoryProjectedProductPresentation[] products)
    {
        var map = new Dictionary<int, InventoryProjectedProductPresentation>();
        foreach (var p in products)
        {
            if (!map.ContainsKey(p.ProductId))
                map[p.ProductId] = p;
        }

        return new InventoryProjectionPresentationSnapshot
        {
            Products = products,
            ByProductId = map,
        };
    }

    [Fact]
    public void SeventyC_list_is_authority_when_70d_order_and_ids_differ()
    {
        var giro = new[]
        {
            Giro(Turnover(10, "Dez")),
            Giro(Turnover(20, "Vinte")),
            Giro(Turnover(30, "Trinta")),
        };
        var rows = InventoryIntelligenceProjectionPresentation.Combine(
            giro,
            Snap(Proj(30, excess: 3), Proj(10, excess: 1), Proj(99, excess: 99)));

        Assert.Equal(new[] { 10, 20, 30 }, rows.Select(r => r.ProductId).ToArray());
        Assert.DoesNotContain(rows, r => r.ProductId == 99);
        Assert.Same(giro[0], rows[0].Intelligence);
        Assert.Same(giro[1], rows[1].Intelligence);
        Assert.Same(giro[2], rows[2].Intelligence);

        Assert.Equal("1", rows[0].Surplus30Display);
        Assert.Null(rows[1].Projection);
        Assert.Equal("—", rows[1].Surplus30Display);
        Assert.Equal("Projeção indisponível", rows[1].ValidityRiskDisplay);
        Assert.Equal("3", rows[2].Surplus30Display);
    }

    [Fact]
    public void Join_matches_by_product_id()
    {
        var t1 = Turnover(1, "Alfa", "A");
        var t2 = Turnover(2, "Beta", "B");
        var rows = InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(t1), Giro(t2)],
            Snap(Proj(2, excess: 9), Proj(1, excess: 0)));

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].ProductId);
        Assert.Equal("Alfa", rows[0].Name);
        Assert.Equal("0", rows[0].Surplus30Display);
        Assert.Equal(2, rows[1].ProductId);
        Assert.Equal("9", rows[1].Surplus30Display);
    }

    [Fact]
    public void SeventyD_order_does_not_change_association()
    {
        var giro = new[] { Giro(Turnover(10, "Dez")), Giro(Turnover(20, "Vinte")) };
        var forward = InventoryIntelligenceProjectionPresentation.Combine(
            giro, Snap(Proj(10, excess: 1), Proj(20, excess: 2)));
        var reverse = InventoryIntelligenceProjectionPresentation.Combine(
            giro, Snap(Proj(20, excess: 2), Proj(10, excess: 1)));

        Assert.Equal(forward[0].Surplus30Display, reverse[0].Surplus30Display);
        Assert.Equal(forward[1].Surplus30Display, reverse[1].Surplus30Display);
        Assert.Equal("1", reverse[0].Surplus30Display);
        Assert.Equal("2", reverse[1].Surplus30Display);
        Assert.Equal("Dez", reverse[0].Name);
    }

    [Fact]
    public void Missing_70d_is_unavailable_and_leaves_70c_intact()
    {
        var giro = Giro(Turnover(5, "Só 70C", "X", stock: 7, vmv30: 2, coverageDays: 3.5,
            band: InventoryCoverageBand.Low));
        var combined = Assert.Single(
            InventoryIntelligenceProjectionPresentation.Combine([giro], Snap()));

        Assert.Same(giro, combined.Intelligence);
        Assert.Null(combined.Projection);
        Assert.Equal("Só 70C", combined.Name);
        Assert.Equal(giro.StockDisplay, combined.StockDisplay);
        Assert.Equal(giro.Vmv30Display, combined.Vmv30Display);
        Assert.Equal(giro.CoverageDisplay, combined.CoverageDisplay);
        Assert.Equal(giro.SituationDisplay, combined.SituationDisplay);
        Assert.Equal("—", combined.Surplus30Display);
        Assert.Equal("Projeção indisponível", combined.ValidityRiskDisplay);
        Assert.Equal("Projeção indisponível", combined.ExcessStatusDisplay);
        Assert.Equal(InventoryProjectionExcessStatus.Unavailable, combined.ExcessStatus);
        Assert.Equal(InventoryProjectionValidityStatus.ProjectionUnavailable, combined.ValidityStatus);
        Assert.Null(combined.ProjectedExcessQuantity);
        Assert.NotEqual("0", combined.Surplus30Display);
        Assert.NotEqual("Sem lote identificado", combined.ValidityRiskDisplay);
        Assert.NotEqual("Sem sobra 30d", combined.ExcessStatusDisplay);
    }

    [Fact]
    public void Extra_70d_without_70c_does_not_create_row()
    {
        var rows = InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1, "Um"))],
            Snap(Proj(1, excess: 0), Proj(99, excess: 50)));
        Assert.Single(rows);
        Assert.Equal(1, rows[0].ProductId);
        Assert.DoesNotContain(rows, r => r.ProductId == 99);
    }

    [Fact]
    public void Surplus_zero_is_zero()
    {
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(Proj(1, excess: 0))));
        Assert.Equal("0", row.Surplus30Display);
        Assert.Equal(0, row.ProjectedExcessQuantity);
        Assert.Equal(InventoryProjectionExcessStatus.NoExcess, row.ExcessStatus);
    }

    [Fact]
    public void Surplus_positive_keeps_b3_display()
    {
        var proj = Proj(1, excess: 12);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(proj)));
        Assert.Equal(proj.Surplus30Display, row.Surplus30Display);
        Assert.Equal("12", row.Surplus30Display);
        Assert.Equal(12, row.ProjectedExcessQuantity);
        Assert.Equal(InventoryProjectionExcessStatus.ProjectedExcess, row.ExcessStatus);
    }

    [Fact]
    public void Surplus_unavailable_is_em_dash()
    {
        var proj = Proj(1, sku: InventorySkuProjectionBlockedReason.NoObservableDemand,
            expiry: InventoryExpiryProjectionBlockedReason.NoObservableDemand, excess: null);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(proj)));
        Assert.Equal("—", row.Surplus30Display);
        Assert.Null(row.ProjectedExcessQuantity);
        Assert.Equal(InventoryProjectionExcessStatus.Unavailable, row.ExcessStatus);
    }

    [Fact]
    public void Validity_expired_is_preserved()
    {
        var proj = Proj(1, lots: [Lot(1, InventoryProjectionLotKind.AlreadyExpired, 4)], tracked: 4);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(proj)));
        Assert.Equal(InventoryProjectionValidityStatus.Expired, row.ValidityStatus);
        Assert.Equal("Vencido", row.ValidityRiskDisplay);
        Assert.Same(proj, row.Projection);
    }

    [Fact]
    public void Validity_expires_today_is_preserved()
    {
        var proj = Proj(1, lots: [Lot(1, InventoryProjectionLotKind.ExpiresToday, 2)], tracked: 2);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(proj)));
        Assert.Equal(InventoryProjectionValidityStatus.ExpiresToday, row.ValidityStatus);
        Assert.Equal("Vence hoje", row.ValidityRiskDisplay);
    }

    [Fact]
    public void Validity_invalid_is_preserved()
    {
        var proj = Proj(1, expiry: InventoryExpiryProjectionBlockedReason.InvalidExpiryDate, tracked: 10);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(proj)));
        Assert.Equal(InventoryProjectionValidityStatus.InvalidExpiry, row.ValidityStatus);
        Assert.Equal("Validade cadastrada inválida", row.ValidityRiskDisplay);
    }

    [Fact]
    public void Validity_no_lot_is_preserved()
    {
        var proj = Proj(1, untracked: 40, tracked: 0);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(proj)));
        Assert.Equal(InventoryProjectionValidityStatus.NoLot, row.ValidityStatus);
        Assert.Equal("Sem lote identificado", row.ValidityRiskDisplay);
    }

    [Fact]
    public void Validity_projection_unavailable_from_duplicate_lot_id()
    {
        var proj = Proj(1, expiry: InventoryExpiryProjectionBlockedReason.DuplicateLotId, tracked: 40);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(proj)));
        Assert.Equal(InventoryProjectionValidityStatus.ProjectionUnavailable, row.ValidityStatus);
        Assert.Equal("Projeção indisponível", row.ValidityRiskDisplay);
    }

    [Fact]
    public void Sku_calculable_with_expiry_blocked_keeps_surplus()
    {
        var proj = Proj(
            1,
            expiry: InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse,
            excess: 20,
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 40)],
            tracked: 80);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(proj)));
        Assert.Equal("20", row.Surplus30Display);
        Assert.Equal(20, row.ProjectedExcessQuantity);
        Assert.Equal(InventoryProjectionExcessStatus.ProjectedExcess, row.ExcessStatus);
        Assert.Equal(InventoryProjectionValidityStatus.Dated, row.ValidityStatus);
        Assert.Equal("Com validade", row.ValidityRiskDisplay);
        Assert.Equal("Lotes excedem o depósito", row.Projection!.ExpiryBlockedShortText);
    }

    [Fact]
    public void SeventyC_fields_stay_identical()
    {
        var giro = Giro(Turnover(8, "Nome", "C8", stock: 3, fridge: 1, total: 4, vmv30: 0.5,
            coverageDays: 8, band: InventoryCoverageBand.Attention));
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [giro], Snap(Proj(8, excess: 1))));
        Assert.Equal(giro.ProductId, row.ProductId);
        Assert.Equal(giro.Name, row.Name);
        Assert.Equal(giro.Code, row.Code);
        Assert.Equal(giro.StockDisplay, row.StockDisplay);
        Assert.Equal(giro.StockFridgeDisplay, row.StockFridgeDisplay);
        Assert.Equal(giro.TotalStockDisplay, row.TotalStockDisplay);
        Assert.Equal(giro.Vmv30Display, row.Vmv30Display);
        Assert.Equal(giro.CoverageDisplay, row.CoverageDisplay);
        Assert.Equal(giro.LastSaleDisplay, row.LastSaleDisplay);
        Assert.Equal(giro.DaysWithoutSaleDisplay, row.DaysWithoutSaleDisplay);
        Assert.Equal(giro.SituationDisplay, row.SituationDisplay);
        Assert.Equal(giro.AlertDisplay, row.AlertDisplay);
        Assert.Equal(giro.HistoryDisplay, row.HistoryDisplay);
        Assert.Equal(giro.Tone, row.Tone);
        Assert.Same(giro, row.Intelligence);
    }

    [Fact]
    public void Product_id_is_preserved()
    {
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(42))], Snap(Proj(42, excess: 0))));
        Assert.Equal(42, row.ProductId);
        Assert.Equal(42, row.Intelligence.ProductId);
        Assert.Equal(42, row.Projection!.ProductId);
    }

    [Fact]
    public void Full_b3_reference_is_preserved()
    {
        var proj = Proj(3, excess: 4);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(3))], Snap(proj)));
        Assert.Same(proj, row.Projection);
        Assert.Equal(proj.DemandCaption, row.Projection!.DemandCaption);
        Assert.Equal(proj.Lots, row.Projection.Lots);
    }

    [Fact]
    public void Typed_sort_fields_are_exposed()
    {
        var available = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], Snap(Proj(1, excess: 7.5))));
        Assert.Equal(7.5, available.ProjectedExcessQuantity);
        Assert.IsType<InventoryProjectionValidityStatus>(available.ValidityStatus);

        var missing = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(2))], Snap()));
        Assert.Null(missing.ProjectedExcessQuantity);
        Assert.Equal(InventoryProjectionValidityStatus.ProjectionUnavailable, missing.ValidityStatus);
    }

    [Fact]
    public void Combiner_does_not_mutate_inputs()
    {
        var giro = Giro(Turnover(1, "Fixo"));
        var proj = Proj(1, excess: 3);
        var products = new List<InventoryProjectedProductPresentation> { proj };
        var snap = new InventoryProjectionPresentationSnapshot
        {
            Products = products,
            ByProductId = new Dictionary<int, InventoryProjectedProductPresentation> { [1] = proj },
        };

        _ = InventoryIntelligenceProjectionPresentation.Combine([giro], snap);

        Assert.Equal("Fixo", giro.Name);
        Assert.Single(products);
        Assert.Same(proj, products[0]);
        Assert.Equal(3, proj.ProjectedExcessQuantity);
        Assert.True(snap.ByProductId.ContainsKey(1));
    }

    [Fact]
    public void Empty_giro_list_is_empty()
    {
        var rows = InventoryIntelligenceProjectionPresentation.Combine([], Snap(Proj(1, excess: 9)));
        Assert.Empty(rows);
    }

    [Fact]
    public void Multiple_products_keep_70c_order()
    {
        var giro = new[] { Giro(Turnover(3, "C")), Giro(Turnover(1, "A")), Giro(Turnover(2, "B")) };
        var rows = InventoryIntelligenceProjectionPresentation.Combine(
            giro, Snap(Proj(1, excess: 1), Proj(2, excess: 2), Proj(3, excess: 3)));
        Assert.Equal(new[] { 3, 1, 2 }, rows.Select(r => r.ProductId).ToArray());
        Assert.Equal(new[] { "C", "A", "B" }, rows.Select(r => r.Name).ToArray());
        Assert.Equal(new[] { "3", "1", "2" }, rows.Select(r => r.Surplus30Display).ToArray());
    }

    [Fact]
    public void Apply_reuses_70c_filter_then_joins()
    {
        var rows = new[]
        {
            Turnover(1, "Critico", band: InventoryCoverageBand.Critical, coverageDays: 2, stock: 2, total: 2, vmv30: 1),
            Turnover(2, "Normal", band: InventoryCoverageBand.Normal, coverageDays: 20),
        };
        var snap = Snap(Proj(1, excess: 0), Proj(2, excess: 8));
        var filtered = InventoryIntelligenceProjectionPresentation.Apply(
            rows,
            new InventoryIntelligenceUiFilter { Card = InventoryIntelligenceCardKind.Critical },
            snap);

        var only = Assert.Single(filtered);
        Assert.Equal(1, only.ProductId);
        Assert.Equal("Critico", only.Name);
        Assert.Equal("0", only.Surplus30Display);
        Assert.Equal(InventoryIntelligencePresentation.ToGridRow(rows[0]).SituationDisplay, only.SituationDisplay);
    }

    [Fact]
    public void Duplicate_identical_70d_product_id_is_still_unavailable()
    {
        var first = Proj(1, excess: 10);
        var second = Proj(1, excess: 10);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))],
            [first, second]));

        Assert.Null(row.Projection);
        Assert.Equal("—", row.Surplus30Display);
        Assert.Null(row.ProjectedExcessQuantity);
        Assert.Equal("Projeção indisponível", row.ValidityRiskDisplay);
        Assert.NotEqual("10", row.Surplus30Display);
        Assert.NotSame(first, row.Projection);
        Assert.NotSame(second, row.Projection);
    }

    [Fact]
    public void Duplicate_70d_product_id_marks_projection_unavailable()
    {
        var first = Proj(1, excess: 10);
        var second = Proj(1, excess: 99);
        var rows = InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1, "Dup"))],
            [first, second]);

        var row = Assert.Single(rows);
        Assert.Equal("Dup", row.Name);
        Assert.Null(row.Projection);
        Assert.Equal("—", row.Surplus30Display);
        Assert.Null(row.ProjectedExcessQuantity);
        Assert.Equal("Projeção indisponível", row.ValidityRiskDisplay);
        Assert.NotEqual("10", row.Surplus30Display);
        Assert.NotEqual("99", row.Surplus30Display);
    }

    [Fact]
    public void Snapshot_by_product_id_is_used_when_present()
    {
        var chosen = Proj(1, excess: 4);
        var ignored = Proj(1, excess: 80);
        var snap = new InventoryProjectionPresentationSnapshot
        {
            Products = [ignored],
            ByProductId = new Dictionary<int, InventoryProjectedProductPresentation> { [1] = chosen },
        };
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))], snap));
        Assert.Same(chosen, row.Projection);
        Assert.Equal("4", row.Surplus30Display);
    }

    [Fact]
    public void Displays_stay_pt_br_under_en_us_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            var proj = Proj(1, excess: 1234);
            var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
                [Giro(Turnover(1))], Snap(proj)));
            Assert.Equal(proj.Surplus30Display, row.Surplus30Display);
            Assert.Equal("1.234", row.Surplus30Display);
            Assert.DoesNotContain(",", row.Surplus30Display, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void Source_has_no_io_clock_or_recalculation()
    {
        var path = FindSource("InventoryIntelligenceProjectionPresentation.cs");
        Assert.True(File.Exists(path), path);
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetByProductId", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Load(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClassifyCoverage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectedDemand(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_price", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindSource(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SGDB.App", "Models", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return "";
    }
}
