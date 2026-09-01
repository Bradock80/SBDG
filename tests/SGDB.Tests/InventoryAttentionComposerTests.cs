using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70E-B2 — composição do motor B1 sobre o snapshot 70D. Sem I/O, UI ou recálculo.
/// </summary>
public class InventoryAttentionComposerTests
{
    private static readonly DateTime Today = new(2026, 9, 1);

    static ProductTurnoverRow Turnover(
        int id = 1,
        double stock = 30,
        double fridge = 0,
        double vmv30 = 1,
        int history = 45,
        bool evidence = true,
        bool composition = false,
        bool idle = false,
        bool insufficient30 = false) =>
        new()
        {
            ProductId = id,
            Name = "P" + id,
            Code = "C" + id,
            Stock = stock,
            StockFridge = fridge,
            TotalStock = stock + fridge,
            Vmv7 = vmv30,
            Vmv30 = vmv30,
            Vmv90 = vmv30,
            HistoryDays = history,
            HasPhysicalAvailabilityEvidence = evidence,
            IsCompositionProduct = composition,
            IsIdle = idle,
            IsHistoryInsufficient30 = insufficient30 || history < 30,
            CoverageBand = InventoryCoverageBand.Normal,
        };

    static InventoryProjectionLotInput Lot(int id, double qty, int? days, double? cost = 2) =>
        new()
        {
            LotId = id,
            Quantity = qty,
            ExpiryDate = days is int d ? Today.AddDays(d) : null,
            UnitCost = cost,
        };

    static InventoryProjectedProduct Project(ProductTurnoverRow row, params InventoryProjectionLotInput[] lots)
    {
        var projection = InventoryProjectionEngine.Project(new InventoryProjectionRequest
        {
            Today = Today,
            Vmv30 = row.Vmv30,
            HistoryDays = row.HistoryDays,
            IsHistoryInsufficient30 = row.IsHistoryInsufficient30,
            HasPhysicalAvailabilityEvidence = row.HasPhysicalAvailabilityEvidence,
            IsCompositionProduct = row.IsCompositionProduct,
            TotalStock = row.TotalStock,
            WarehouseStock = row.Stock,
            FridgeStock = row.StockFridge,
            HorizonDays = 30,
            Lots = lots,
        });

        var costs = lots.Select(l => new InventoryProjectedLotCost
        {
            LotId = l.LotId,
            UsedCost = l.UnitCost,
            CostSource = l.UnitCost is double c && c > 0.009
                ? LotCostSource.LotRecorded
                : LotCostSource.Unavailable,
        }).ToList();

        return new InventoryProjectedProduct
        {
            ProductId = row.ProductId,
            Projection = projection,
            LotCosts = costs,
        };
    }

    static InventoryProjectionSnapshot Snap(
        IReadOnlyList<ProductTurnoverRow> rows,
        IReadOnlyDictionary<int, InventoryProjectedProduct> map,
        int queryCount = InventoryProjectionService.ExpectedQueryCount) =>
        new()
        {
            Today = Today,
            QueryCount = queryCount,
            Intelligence = new InventoryIntelligenceSnapshot { Today = Today, QueryCount = 6, Rows = rows },
            ByProductId = map,
        };

    static InventoryAttentionSnapshot BuildComplete(params ProductTurnoverRow[] rows)
    {
        var map = new Dictionary<int, InventoryProjectedProduct>();
        foreach (var row in rows)
            map[row.ProductId] = Project(row, Lot(row.ProductId, row.Stock, 90));
        return InventoryAttentionComposer.Build(Snap(rows, map));
    }

    [Fact]
    public void Snapshot_vazio()
    {
        var built = InventoryAttentionComposer.Build(new InventoryProjectionSnapshot
        {
            QueryCount = InventoryProjectionService.ExpectedQueryCount,
        });
        Assert.Empty(built.Results);
        Assert.Empty(built.ByProductId);
        Assert.Equal(InventoryProjectionService.ExpectedQueryCount, built.QueryCount);
    }

    [Fact]
    public void Null_snapshot_nao_lanca()
    {
        var built = InventoryAttentionComposer.Build(null);
        Assert.Empty(built.Results);
        Assert.Equal(0, built.QueryCount);
    }

    [Fact]
    public void Um_produto_normal()
    {
        var row = Turnover(1, stock: 30, vmv30: 1);
        var product = Project(row, Lot(1, 30, 90));
        var built = InventoryAttentionComposer.Build(Snap([row], new Dictionary<int, InventoryProjectedProduct>
        {
            [1] = product,
        }));
        var result = Assert.Single(built.Results);
        Assert.Equal(1, result.ProductId);
        Assert.Equal(InventoryAttentionReason.None, result.PrimaryReason);
        Assert.Equal(InventoryAttentionPriority.Normal, result.Priority);
        Assert.Equal(InventoryAttentionConfidence.Reliable, result.Confidence);
        Assert.Equal(InventoryAttentionEngine.Evaluate(row, product).PrimaryReason, result.PrimaryReason);
    }

    [Fact]
    public void Um_produto_vencido()
    {
        var row = Turnover(2, stock: 10, vmv30: 1);
        var built = InventoryAttentionComposer.Build(Snap(
            [row],
            new Dictionary<int, InventoryProjectedProduct> { [2] = Project(row, Lot(1, 10, -1)) }));
        var result = Assert.Single(built.Results);
        Assert.Equal(2, result.ProductId);
        Assert.Equal(InventoryAttentionReason.Expired, result.PrimaryReason);
        Assert.Equal(InventoryOperatorAction.RemoveExpired, result.Action);
    }

    [Fact]
    public void Excesso_30d()
    {
        var row = Turnover(3, stock: 100, vmv30: 1);
        var result = Assert.Single(InventoryAttentionComposer.Build(Snap(
            [row],
            new Dictionary<int, InventoryProjectedProduct> { [3] = Project(row, Lot(1, 100, 120)) })).Results);
        Assert.Equal(InventoryAttentionReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Equal(InventoryOperatorAction.EvaluateExcess, result.Action);
    }

    [Fact]
    public void Idle()
    {
        var row = Turnover(4, stock: 30, vmv30: 1, idle: true, history: 90);
        var result = Assert.Single(InventoryAttentionComposer.Build(Snap(
            [row],
            new Dictionary<int, InventoryProjectedProduct> { [4] = Project(row, Lot(1, 30, 90)) })).Results);
        Assert.Equal(InventoryAttentionReason.Idle, result.PrimaryReason);
        Assert.Equal(InventoryAttentionFamily.Turnover, result.Family);
    }

    [Fact]
    public void Historico_insuficiente()
    {
        var row = Turnover(5, stock: 40, vmv30: 1, history: 10, insufficient30: true);
        var result = Assert.Single(InventoryAttentionComposer.Build(Snap(
            [row],
            new Dictionary<int, InventoryProjectedProduct> { [5] = Project(row, Lot(1, 40, 90)) })).Results);
        Assert.Equal(InventoryAttentionReason.InsufficientHistory, result.PrimaryReason);
        Assert.NotEqual(InventoryAttentionReason.ProjectionMissing, result.PrimaryReason);
    }

    [Fact]
    public void Invalid_expiry()
    {
        var row = Turnover(6, stock: 40, vmv30: 1);
        var lot = new InventoryProjectionLotInput
        {
            LotId = 1,
            Quantity = 40,
            HasInvalidExpiryText = true,
        };
        var result = Assert.Single(InventoryAttentionComposer.Build(Snap(
            [row],
            new Dictionary<int, InventoryProjectedProduct> { [6] = Project(row, lot) })).Results);
        Assert.Equal(InventoryAttentionReason.InvalidExpiryDate, result.PrimaryReason);
        Assert.NotEqual(InventoryAttentionReason.Undated, result.PrimaryReason);
    }

    [Fact]
    public void Multiplos_produtos_ProductId_correto()
    {
        var normal = Turnover(10, stock: 30, vmv30: 1);
        var expired = Turnover(20, stock: 8, vmv30: 1);
        var excess = Turnover(30, stock: 100, vmv30: 1);
        var idle = Turnover(40, stock: 30, vmv30: 1, idle: true, history: 90);
        var young = Turnover(50, stock: 12, vmv30: 1, history: 8, insufficient30: true);
        var invalid = Turnover(60, stock: 40, vmv30: 1);
        var invalidLot = new InventoryProjectionLotInput
        {
            LotId = 1,
            Quantity = 40,
            HasInvalidExpiryText = true,
        };

        var built = InventoryAttentionComposer.Build(Snap(
            [normal, expired, excess, idle, young, invalid],
            new Dictionary<int, InventoryProjectedProduct>
            {
                [10] = Project(normal, Lot(1, 30, 90)),
                [20] = Project(expired, Lot(1, 8, -2)),
                [30] = Project(excess, Lot(1, 100, 120)),
                [40] = Project(idle, Lot(1, 30, 90)),
                [50] = Project(young, Lot(1, 12, 90)),
                [60] = Project(invalid, invalidLot),
            }));

        Assert.Equal(new[] { 10, 20, 30, 40, 50, 60 }, built.Results.Select(r => r.ProductId));
        Assert.Equal(InventoryAttentionReason.None, built.ByProductId[10].PrimaryReason);
        Assert.Equal(InventoryAttentionReason.Expired, built.ByProductId[20].PrimaryReason);
        Assert.Equal(InventoryAttentionReason.ProjectedExcess30, built.ByProductId[30].PrimaryReason);
        Assert.Equal(InventoryAttentionReason.Idle, built.ByProductId[40].PrimaryReason);
        Assert.Equal(InventoryAttentionReason.InsufficientHistory, built.ByProductId[50].PrimaryReason);
        Assert.Equal(InventoryAttentionReason.InvalidExpiryDate, built.ByProductId[60].PrimaryReason);
    }

    [Fact]
    public void Preserva_ordem_70C()
    {
        var rows = new[] { Turnover(30), Turnover(10), Turnover(20) };
        var built = BuildComplete(rows);
        Assert.Equal(new[] { 30, 10, 20 }, built.Results.Select(r => r.ProductId));
    }

    [Fact]
    public void Projection_fora_de_ordem_nao_muda_join()
    {
        var a = Turnover(1, stock: 30);
        var b = Turnover(2, stock: 100, vmv30: 1);
        var p1 = Project(a, Lot(1, 30, 90));
        var p2 = Project(b, Lot(2, 100, 120));
        var forward = InventoryAttentionComposer.Build(Snap([a, b], new Dictionary<int, InventoryProjectedProduct>
        {
            [1] = p1,
            [2] = p2,
        }));
        var reverse = InventoryAttentionComposer.Build(Snap([a, b], new Dictionary<int, InventoryProjectedProduct>
        {
            [2] = p2,
            [1] = p1,
        }));
        Assert.Equal(forward.Results[0].PrimaryReason, reverse.Results[0].PrimaryReason);
        Assert.Equal(forward.Results[1].PrimaryReason, reverse.Results[1].PrimaryReason);
        Assert.Equal(1, reverse.Results[0].ProductId);
        Assert.Equal(2, reverse.Results[1].ProductId);
        Assert.Equal(InventoryAttentionReason.ProjectedExcess30, reverse.Results[1].PrimaryReason);
    }

    [Fact]
    public void Projection_ausente_nao_inventa_70D()
    {
        var row = Turnover(7, stock: 25, vmv30: 10);
        var built = InventoryAttentionComposer.Build(Snap([row], new Dictionary<int, InventoryProjectedProduct>()));
        var result = Assert.Single(built.Results);
        Assert.Equal(7, result.ProductId);
        Assert.Equal(InventoryAttentionReason.ProjectionMissing, result.PrimaryReason);
        Assert.NotEqual(InventoryAttentionReason.InsufficientHistory, result.PrimaryReason);
        Assert.NotEqual(InventoryAttentionReason.NoLot, result.PrimaryReason);
        Assert.NotEqual(InventoryAttentionReason.NoPhysicalEvidence, result.PrimaryReason);
        Assert.Equal(InventoryAttentionFamily.DataQuality, result.Family);
        Assert.Equal(InventoryAttentionPriority.Low, result.Priority);
        Assert.Equal(InventoryOperatorAction.ReviewData, result.Action);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, result.Confidence);
        Assert.Null(result.ProjectedExcessQuantity);
        Assert.Null(result.ProjectedExpirySurplusQuantity);
        Assert.Empty(result.SecondaryReasons);
    }

    [Fact]
    public void Projection_duplicada_nao_escolhe_last_wins()
    {
        var row = Turnover(8, stock: 100, vmv30: 1);
        var first = Project(row, Lot(1, 100, 120));
        var second = Project(Turnover(8, stock: 40, vmv30: 1), Lot(1, 40, 90));
        var built = InventoryAttentionComposer.Build(
            Today,
            InventoryProjectionService.ExpectedQueryCount,
            [row],
            [first, second]);
        var result = Assert.Single(built.Results);
        Assert.Equal(InventoryAttentionReason.DuplicateProjection, result.PrimaryReason);
        Assert.Equal(InventoryAttentionPriority.Critical, result.Priority);
        Assert.Equal(InventoryAttentionFamily.DataQuality, result.Family);
        Assert.Equal(InventoryOperatorAction.ReviewData, result.Action);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, result.Confidence);
        Assert.Null(result.ProjectedExcessQuantity);
        Assert.NotEqual(first.Projection.ProjectedExcessQuantity, result.ProjectedExcessQuantity);
        Assert.NotEqual(InventoryAttentionReason.ProjectedExcess30, result.PrimaryReason);
    }

    [Fact]
    public void Duplicate_identica_ainda_e_conflito()
    {
        var row = Turnover(9, stock: 100, vmv30: 1);
        var product = Project(row, Lot(1, 100, 120));
        var result = Assert.Single(InventoryAttentionComposer.Build(
            Today, 7, [row], [product, product]).Results);
        Assert.Equal(InventoryAttentionReason.DuplicateProjection, result.PrimaryReason);
        Assert.Null(result.ProjectedExcessQuantity);
    }

    [Fact]
    public void Duplicate_ordem_invertida_mesmo_resultado()
    {
        var row = Turnover(8, stock: 100, vmv30: 1);
        var first = Project(row, Lot(1, 100, 120));
        var second = Project(Turnover(8, stock: 40, vmv30: 1), Lot(1, 40, 90));
        var a = InventoryAttentionComposer.Build(Today, 7, [row], [first, second]);
        var b = InventoryAttentionComposer.Build(Today, 7, [row], [second, first]);
        Assert.Equal(a.Results[0].PrimaryReason, b.Results[0].PrimaryReason);
        Assert.Equal(a.Results[0].Priority, b.Results[0].Priority);
        Assert.Equal(a.Results[0].Action, b.Results[0].Action);
        Assert.Equal(a.Results[0].Confidence, b.Results[0].Confidence);
        Assert.Equal(a.Results[0].SecondaryReasons, b.Results[0].SecondaryReasons);
    }

    [Fact]
    public void Projection_extra_nao_cria_fantasma()
    {
        var row = Turnover(1, stock: 30);
        var extra = Project(Turnover(99, stock: 200, vmv30: 1), Lot(1, 200, 120));
        var built = InventoryAttentionComposer.Build(Snap(
            [row],
            new Dictionary<int, InventoryProjectedProduct>
            {
                [1] = Project(row, Lot(1, 30, 90)),
                [99] = extra,
            }));
        Assert.Single(built.Results);
        Assert.Equal(1, built.Results[0].ProductId);
        Assert.DoesNotContain(built.Results, r => r.ProductId == 99);
        Assert.False(built.ByProductId.ContainsKey(99));
    }

    [Fact]
    public void Um_erro_nao_bloqueia_outros()
    {
        var okA = Turnover(1, stock: 30, vmv30: 1);
        var dup = Turnover(2, stock: 100, vmv30: 1);
        var okC = Turnover(3, stock: 10, vmv30: 1);
        var built = InventoryAttentionComposer.Build(
            Today,
            InventoryProjectionService.ExpectedQueryCount,
            [okA, dup, okC],
            [
                Project(okA, Lot(1, 30, 90)),
                Project(dup, Lot(2, 100, 120)),
                Project(Turnover(2, stock: 40, vmv30: 1), Lot(9, 40, 90)),
                Project(okC, Lot(3, 10, -1)),
            ]);

        Assert.Equal(3, built.Results.Count);
        Assert.Equal(InventoryAttentionReason.None, built.Results[0].PrimaryReason);
        Assert.Equal(InventoryAttentionConfidence.Reliable, built.Results[0].Confidence);
        Assert.Equal(InventoryAttentionReason.DuplicateProjection, built.Results[1].PrimaryReason);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, built.Results[1].Confidence);
        Assert.Equal(InventoryAttentionReason.Expired, built.Results[2].PrimaryReason);
        Assert.Equal(InventoryOperatorAction.RemoveExpired, built.Results[2].Action);
    }

    [Fact]
    public void Confidence_e_SecondaryReasons_preservados_da_B1()
    {
        var row = Turnover(11, stock: 100, vmv30: 1, idle: true, history: 90);
        var product = Project(row, Lot(1, 40, 10, 2), Lot(2, 60, 90, 2));
        var expected = InventoryAttentionEngine.Evaluate(row, product);
        var actual = Assert.Single(InventoryAttentionComposer.Build(Snap(
            [row],
            new Dictionary<int, InventoryProjectedProduct> { [11] = product })).Results);

        Assert.Equal(expected.Priority, actual.Priority);
        Assert.Equal(expected.Family, actual.Family);
        Assert.Equal(expected.PrimaryReason, actual.PrimaryReason);
        Assert.Equal(expected.SecondaryReasons, actual.SecondaryReasons);
        Assert.Equal(expected.Action, actual.Action);
        Assert.Equal(expected.Confidence, actual.Confidence);
        Assert.Equal(expected.ProjectedExcessQuantity, actual.ProjectedExcessQuantity);
        Assert.Equal(expected.ProjectedExpirySurplusQuantity, actual.ProjectedExpirySurplusQuantity);
        Assert.Equal(expected.SurplusValueQuality, actual.SurplusValueQuality);
        Assert.NotEmpty(actual.SecondaryReasons);
    }

    [Fact]
    public void QueryCount_herdado_e_igual_a_7()
    {
        var row = Turnover(1);
        var built = InventoryAttentionComposer.Build(Snap(
            [row],
            new Dictionary<int, InventoryProjectedProduct> { [1] = Project(row, Lot(1, 30, 90)) },
            InventoryProjectionService.ExpectedQueryCount));
        Assert.Equal(7, InventoryProjectionService.ExpectedQueryCount);
        Assert.Equal(7, built.QueryCount);
    }

    [Fact]
    public void Determinismo_mesma_entrada()
    {
        var rows = new[] { Turnover(1), Turnover(2, stock: 10) };
        var map = new Dictionary<int, InventoryProjectedProduct>
        {
            [1] = Project(rows[0], Lot(1, 30, 90)),
            [2] = Project(rows[1], Lot(2, 10, -1)),
        };
        var snap = Snap(rows, map);
        var a = InventoryAttentionComposer.Build(snap);
        var b = InventoryAttentionComposer.Build(snap);
        Assert.Equal(a.Results.Select(r => r.PrimaryReason), b.Results.Select(r => r.PrimaryReason));
        Assert.Equal(a.Results.Select(r => r.SecondaryReasons), b.Results.Select(r => r.SecondaryReasons));
        Assert.Equal(a.QueryCount, b.QueryCount);
    }

    [Fact]
    public void Lista_grande_um_lookup_por_produto()
    {
        var rows = Enumerable.Range(1, 200).Select(i => Turnover(i, stock: 30)).ToArray();
        var map = rows.ToDictionary(r => r.ProductId, r => Project(r, Lot(r.ProductId, 30, 90)));
        var built = InventoryAttentionComposer.Build(Snap(rows, map));
        Assert.Equal(200, built.Results.Count);
        Assert.Equal(rows.Select(r => r.ProductId), built.Results.Select(r => r.ProductId));
        Assert.All(built.Results, r => Assert.Equal(r.ProductId, built.ByProductId[r.ProductId].ProductId));
        Assert.Equal(7, built.QueryCount);
    }

    [Fact]
    public void Nao_altera_snapshot_de_origem()
    {
        var row = Turnover(1);
        var map = new Dictionary<int, InventoryProjectedProduct>
        {
            [1] = Project(row, Lot(1, 30, 90)),
        };
        var snap = Snap([row], map);
        var intelligence = snap.Intelligence;
        _ = InventoryAttentionComposer.Build(snap);
        Assert.Equal(7, snap.QueryCount);
        Assert.Same(intelligence, snap.Intelligence);
        Assert.Same(intelligence.Rows, snap.Intelligence.Rows);
        Assert.Same(map, snap.ByProductId);
        Assert.Single(snap.Intelligence.Rows);
        Assert.Single(snap.ByProductId);
    }

    [Fact]
    public void Engine_Apply_delega_para_a_composicao()
    {
        var row = Turnover(1, stock: 25, vmv30: 10);
        var snap = Snap([row], new Dictionary<int, InventoryProjectedProduct>());
        var composed = InventoryAttentionComposer.Build(snap);
        var applied = InventoryAttentionEngine.Apply(snap);
        Assert.Equal(composed.Results[0].PrimaryReason, applied[0].PrimaryReason);
        Assert.Equal(InventoryAttentionReason.ProjectionMissing, applied[0].PrimaryReason);
    }

    [Fact]
    public void Composer_source_nao_tem_io_nem_recalculo()
    {
        var path = FindSource("src", "SGDB.App", "Services", "InventoryAttentionComposer.cs");
        Assert.True(File.Exists(path), path);
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.Data", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryIntelligenceService.Load", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidityControlService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionEngine.Project", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_price", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreNetwork", source, StringComparison.Ordinal);
        Assert.Contains("TryGetValue", source, StringComparison.Ordinal);
        Assert.Contains("InventoryAttentionEngine.Evaluate", source, StringComparison.Ordinal);
    }

    static string FindSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return "";
    }
}
