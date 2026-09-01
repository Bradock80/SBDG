using System.IO;
using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// 70D-B4B — integração da projeção na tela Estoque Inteligente.
/// Sem instanciar UserControl WPF, sem Load de banco, sem EXE.
/// </summary>
public class InventoryIntelligenceModuleViewB4BTests
{
    [Fact]
    public void View_loads_composed_projection_once_and_never_reloads_70c()
    {
        var cs = ReadViewCs();
        Assert.Contains("InventoryProjectionService.Load()", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryIntelligenceService.Load", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("GetByProductId", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("FromProduct", cs, StringComparison.Ordinal);

        var loadIdx = cs.IndexOf("InventoryProjectionService.Load()", StringComparison.Ordinal);
        var applyIdx = cs.IndexOf("InventoryProjectionPresentation.Apply(snapshot)", StringComparison.Ordinal);
        var assignSnap = cs.IndexOf("_snapshot = snapshot;", StringComparison.Ordinal);
        var assignPresented = cs.IndexOf("_presented = presented;", StringComparison.Ordinal);
        Assert.True(loadIdx >= 0 && applyIdx > loadIdx);
        Assert.True(assignSnap > applyIdx && assignPresented > applyIdx);
        Assert.Equal(1, CountOccurrences(cs, "InventoryProjectionService.Load("));
    }

    [Fact]
    public void View_blocks_store_network_client_before_any_load()
    {
        var cs = ReadViewCs();
        var clientIdx = cs.IndexOf("StoreNetworkMode.IsClient", StringComparison.Ordinal);
        var loadIdx = cs.IndexOf("InventoryProjectionService.Load()", StringComparison.Ordinal);
        Assert.InRange(clientIdx, 0, loadIdx - 1);
        Assert.Contains("ShowClientBlocked();", cs, StringComparison.Ordinal);

        var mode = ReadSource("src", "SGDB.App", "Services", "StoreNetworkMode.cs");
        Assert.Contains("estoque_inteligente", mode, StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_adds_surplus_and_validity_columns_with_typed_sort()
    {
        var xaml = ReadViewXaml();
        Assert.Contains("Header=\"Produto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Histórico\"", xaml, StringComparison.Ordinal);

        var surplus = ColumnBlock(xaml, "Sobra 30d");
        Assert.Contains("Binding=\"{Binding Surplus30Display}\"", surplus, StringComparison.Ordinal);
        Assert.Contains("SortMemberPath=\"ProjectedExcessQuantity\"", surplus, StringComparison.Ordinal);
        Assert.Contains("Width=\"90\"", surplus, StringComparison.Ordinal);
        Assert.Contains("Value=\"Right\"", surplus, StringComparison.Ordinal);
        Assert.Contains("Value=\"Center\"", surplus, StringComparison.Ordinal);
        Assert.Contains("ToolTip\" Value=\"{Binding ExcessStatusDisplay}\"", surplus, StringComparison.Ordinal);

        var validity = ColumnBlock(xaml, "Validade / risco");
        Assert.Contains("Binding=\"{Binding ValidityRiskDisplay}\"", validity, StringComparison.Ordinal);
        Assert.Contains("SortMemberPath=\"ValidityStatus\"", validity, StringComparison.Ordinal);
        Assert.Contains("Width=\"180\"", validity, StringComparison.Ordinal);
        Assert.Contains("Value=\"Left\"", validity, StringComparison.Ordinal);
        Assert.Contains("ToolTip\" Value=\"{Binding ValidityRiskDisplay}\"", validity, StringComparison.Ordinal);

        Assert.DoesNotContain("prejuízo certo", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("perda certa", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vai vencer", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vai perder", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_keeps_70c_filter_and_order()
    {
        var rows = new[]
        {
            Turnover(2, "Beta", InventoryCoverageBand.Normal),
            Turnover(1, "Alfa", InventoryCoverageBand.Critical, coverageDays: 2, stock: 2, total: 2, vmv30: 1),
            Turnover(3, "Gama", InventoryCoverageBand.Critical, coverageDays: 1, stock: 1, total: 1, vmv30: 1),
        };
        var presented = Snap(Proj(1, excess: 4), Proj(2, excess: 9), Proj(3, excess: 1));
        var filtered = InventoryIntelligenceProjectionPresentation.Apply(
            rows,
            new InventoryIntelligenceUiFilter { Card = InventoryIntelligenceCardKind.Critical },
            presented);

        Assert.Equal(new[] { 1, 3 }, filtered.Select(r => r.ProductId).ToArray());
        Assert.Equal(new[] { "Alfa", "Gama" }, filtered.Select(r => r.Name).ToArray());
        Assert.DoesNotContain(filtered, r => r.ProductId == 2);
    }

    [Fact]
    public void Missing_70d_is_em_dash_and_zero_surplus_is_not_unavailable()
    {
        var missing = Assert.Single(InventoryIntelligenceProjectionPresentation.Apply(
            [Turnover(1, "Sem 70D")],
            new InventoryIntelligenceUiFilter(),
            Snap()));
        Assert.Null(missing.Projection);
        Assert.Equal("—", missing.Surplus30Display);
        Assert.Null(missing.ProjectedExcessQuantity);
        Assert.Equal(InventoryProjectionExcessStatus.Unavailable, missing.ExcessStatus);
        Assert.Equal("Projeção indisponível", missing.ValidityRiskDisplay);

        var zero = Assert.Single(InventoryIntelligenceProjectionPresentation.Apply(
            [Turnover(2, "Zero")],
            new InventoryIntelligenceUiFilter(),
            Snap(Proj(2, excess: 0))));
        Assert.Equal("0", zero.Surplus30Display);
        Assert.Equal(0, zero.ProjectedExcessQuantity);
        Assert.Equal(InventoryProjectionExcessStatus.NoExcess, zero.ExcessStatus);
        Assert.NotEqual(missing.Surplus30Display, zero.Surplus30Display);
    }

    [Fact]
    public void Typed_sort_uses_quantity_and_validity_status_not_display_text()
    {
        var rows = InventoryIntelligenceProjectionPresentation.Apply(
            [Turnover(1, "Dez"), Turnover(2, "Dois"), Turnover(3, "Indisp")],
            new InventoryIntelligenceUiFilter(),
            Snap(
                Proj(1, excess: 10),
                Proj(2, excess: 2),
                Proj(3, sku: InventorySkuProjectionBlockedReason.NoObservableDemand,
                    expiry: InventoryExpiryProjectionBlockedReason.NoObservableDemand, excess: null)));

        var byText = rows.OrderBy(r => r.Surplus30Display, StringComparer.Ordinal).Select(r => r.ProductId).ToArray();
        var byQty = rows.OrderBy(r => r.ProjectedExcessQuantity).Select(r => r.ProductId).ToArray();
        Assert.Equal(new[] { 3, 2, 1 }, byQty);
        Assert.NotEqual(byText, byQty);

        var mixed = InventoryIntelligenceProjectionPresentation.Apply(
            [Turnover(10, "Vencido"), Turnover(20, "Com validade"), Turnover(30, "Indisp")],
            new InventoryIntelligenceUiFilter(),
            Snap(
                Proj(10, lots: [Lot(1, InventoryProjectionLotKind.AlreadyExpired, 4)], tracked: 4),
                Proj(20, lots: [Lot(2, InventoryProjectionLotKind.Dated, 4)], tracked: 4),
                Proj(30, sku: InventorySkuProjectionBlockedReason.NoObservableDemand,
                    expiry: InventoryExpiryProjectionBlockedReason.NoObservableDemand)));

        var validityText = mixed.OrderBy(r => r.ValidityRiskDisplay, StringComparer.Ordinal)
            .Select(r => r.ProductId).ToArray();
        var validityEnum = mixed.OrderBy(r => r.ValidityStatus).Select(r => r.ProductId).ToArray();
        Assert.Equal(new[] { 10, 20, 30 }, validityEnum);
        Assert.NotEqual(validityText, validityEnum);
    }

    [Fact]
    public void Cards_use_full_intelligence_snapshot_not_filtered_rows()
    {
        var cs = ReadViewCs();
        Assert.Contains(
            "InventoryIntelligencePresentation.CountCards(_snapshot.Intelligence.Rows)",
            cs,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CountCards(rows)", cs, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(cs, "CountCards(_snapshot.Intelligence.Rows)"));

        var rows = new[]
        {
            Turnover(1, "Critico", InventoryCoverageBand.Critical, coverageDays: 2, stock: 2, total: 2, vmv30: 1),
            Turnover(2, "Normal", InventoryCoverageBand.Normal),
        };
        var cards = InventoryIntelligencePresentation.CountCards(rows);
        var filtered = InventoryIntelligenceProjectionPresentation.Apply(
            rows,
            new InventoryIntelligenceUiFilter { Card = InventoryIntelligenceCardKind.Critical },
            Snap(Proj(1, excess: 0), Proj(2, excess: 8)));

        Assert.Equal(2, cards.All);
        Assert.Equal(1, cards.Critical);
        Assert.Single(filtered);
        Assert.Equal(1, filtered[0].ProductId);
    }

    [Fact]
    public void Selection_and_detail_use_wrapper_and_keep_70c_text()
    {
        var cs = ReadViewCs();
        Assert.Contains("InventoryIntelligenceProjectionGridRow", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("is not InventoryIntelligenceGridRow", cs, StringComparison.Ordinal);
        Assert.Contains("var giro = row.Intelligence;", cs, StringComparison.Ordinal);
        Assert.Contains("giro.SituationDisplay", cs, StringComparison.Ordinal);
        Assert.Contains("giro.AlertDisplay", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectedDemandDisplay", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("SurplusValueDisplay", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpiryBlockedExplanation", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("UntrackedWarehouseAlert", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_failure_keeps_previous_snapshots_on_valid_load()
    {
        var cs = ReadViewCs();
        var keep = cs.IndexOf("failure.Value.KeepPreviousSnapshot", StringComparison.Ordinal);
        var meta = cs.IndexOf("MetaText.Text = failure.Value.OperatorMessage;", keep, StringComparison.Ordinal);
        var nextElse = cs.IndexOf("else", keep, StringComparison.Ordinal);
        Assert.True(keep >= 0 && meta > keep && meta < nextElse);

        var keepBlock = cs[keep..nextElse];
        Assert.DoesNotContain("_snapshot =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_presented =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", cs, StringComparison.Ordinal);

        var firstFail = InventoryIntelligencePresentation.ResolveLoadFailure(false);
        var refreshFail = InventoryIntelligencePresentation.ResolveLoadFailure(true);
        Assert.False(firstFail.KeepPreviousSnapshot);
        Assert.True(refreshFail.KeepPreviousSnapshot);
        Assert.Equal(InventoryIntelligencePresentation.RefreshKeepDataMessage, refreshFail.OperatorMessage);
    }

    [Fact]
    public void ApplyView_uses_b4a_api_on_intelligence_rows()
    {
        var cs = ReadViewCs();
        Assert.Contains("InventoryIntelligenceProjectionPresentation.Apply(", cs, StringComparison.Ordinal);
        Assert.Contains(
            "_snapshot.Intelligence.Rows, _filter, _presented, _attentionPresented)",
            cs,
            StringComparison.Ordinal);
        Assert.Contains("Grid.Items.SortDescriptions.Clear();", cs, StringComparison.Ordinal);
        Assert.Contains("InventoryProjectionSnapshot _snapshot", cs, StringComparison.Ordinal);
        Assert.Contains("InventoryProjectionPresentationSnapshot _presented", cs, StringComparison.Ordinal);
        Assert.Contains("InventoryAttentionSnapshot _attention", cs, StringComparison.Ordinal);
        Assert.Contains("InventoryAttentionPresentationSnapshot _attentionPresented", cs, StringComparison.Ordinal);
    }

    private static ProductTurnoverRow Turnover(
        int id,
        string name,
        InventoryCoverageBand band = InventoryCoverageBand.Normal,
        double? coverageDays = 20,
        double stock = 10,
        double total = 10,
        double vmv30 = 1) =>
        new()
        {
            ProductId = id,
            Name = name,
            Code = "P" + id,
            Stock = stock,
            StockFridge = 0,
            TotalStock = total,
            Vmv30 = vmv30,
            CoverageDays = coverageDays,
            CoverageBand = band,
            HistoryDays = 45,
            HasPhysicalAvailabilityEvidence = true,
        };

    private static InventoryProjectedProductPresentation Proj(
        int id,
        InventorySkuProjectionBlockedReason sku = InventorySkuProjectionBlockedReason.None,
        InventoryExpiryProjectionBlockedReason expiry = InventoryExpiryProjectionBlockedReason.None,
        double? excess = 0,
        IReadOnlyList<InventoryProjectionLotResult>? lots = null,
        double tracked = 0) =>
        InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = id,
            Projection = new InventoryProjectionResult
            {
                SkuBlockedReason = sku,
                ExpiryBlockedReason = expiry,
                HorizonDays = 30,
                ProjectedDemand = sku == InventorySkuProjectionBlockedReason.None ? 30 : null,
                ProjectedExcessQuantity = sku == InventorySkuProjectionBlockedReason.None ? excess : null,
                TrackedLotQuantity = tracked,
                Lots = lots ?? [],
            },
        });

    private static InventoryProjectionLotResult Lot(
        int lotId,
        InventoryProjectionLotKind kind,
        double qty) =>
        new()
        {
            LotId = lotId,
            Kind = kind,
            Quantity = qty,
            AlreadyExpired = kind == InventoryProjectionLotKind.AlreadyExpired,
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

    private static string ColumnBlock(string xaml, string header)
    {
        var marker = $"Header=\"{header}\"";
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, marker);
        var open = xaml.LastIndexOf("<DataGridTextColumn", start, StringComparison.Ordinal);
        var close = xaml.IndexOf("</DataGridTextColumn>", start, StringComparison.Ordinal);
        Assert.True(open >= 0 && close > open, header);
        return xaml[open..(close + "</DataGridTextColumn>".Length)];
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(value, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += value.Length;
        }

        return count;
    }

    private static string ReadViewCs() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml.cs");

    private static string ReadViewXaml() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml");

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
