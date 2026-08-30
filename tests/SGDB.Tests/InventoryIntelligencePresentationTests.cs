using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 70C-B3A — apresentação/filtro em memória. Não toca o motor, depósito.db nem o EXE.
/// </summary>
public class InventoryIntelligencePresentationTests
{
    private static ProductTurnoverRow Row(
        int id = 1,
        string name = "Produto",
        string code = "P1",
        InventoryCoverageBand band = InventoryCoverageBand.Normal,
        bool zeroTurnover = false,
        bool idle = false,
        bool anomaly = false,
        bool composition = false,
        bool evidence = true,
        int historyDays = 45,
        bool insufficient30 = false,
        DateTime? lastSale = null,
        int? daysWithout = null,
        double? coverageDays = 20,
        double stock = 10,
        double fridge = 0,
        double total = 10,
        double vmv30 = 1)
    {
        return new ProductTurnoverRow
        {
            ProductId = id,
            Name = name,
            Code = code,
            CoverageBand = band,
            IsZeroStockWithTurnover = zeroTurnover,
            IsIdle = idle,
            HasLocationStockAnomaly = anomaly,
            IsCompositionProduct = composition,
            HasPhysicalAvailabilityEvidence = evidence,
            HistoryDays = historyDays,
            IsHistoryInsufficient30 = insufficient30,
            LastValidSaleDate = lastSale,
            DaysWithoutSale = daysWithout,
            CoverageDays = coverageDays,
            Stock = stock,
            StockFridge = fridge,
            TotalStock = total,
            Vmv30 = vmv30,
        };
    }

    private static IReadOnlyList<InventoryIntelligenceGridRow> Apply(
        IReadOnlyList<ProductTurnoverRow> rows,
        InventoryIntelligenceUiFilter? filter = null) =>
        InventoryIntelligencePresentation.Apply(rows, filter ?? new InventoryIntelligenceUiFilter());

    private static InventoryIntelligenceUiFilter Card(InventoryIntelligenceCardKind kind) =>
        new() { Card = kind };

    [Fact]
    public void Card_All_returns_every_row()
    {
        var rows = new[]
        {
            Row(1, band: InventoryCoverageBand.Zero),
            Row(2, band: InventoryCoverageBand.Critical),
            Row(3, band: InventoryCoverageBand.Low),
        };
        var result = Apply(rows, Card(InventoryIntelligenceCardKind.All));
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Card_ZeroStock_is_CoverageBand_Zero_only()
    {
        var rows = new[]
        {
            Row(1, name: "Zero", band: InventoryCoverageBand.Zero),
            Row(2, name: "Zero giro", band: InventoryCoverageBand.Zero, zeroTurnover: true),
            Row(3, name: "Critical", band: InventoryCoverageBand.Critical),
            Row(4, name: "Low", band: InventoryCoverageBand.Low),
        };
        var result = Apply(rows, Card(InventoryIntelligenceCardKind.ZeroStock));
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Contains(r.Name, new[] { "Zero", "Zero giro" }));
    }

    [Fact]
    public void Card_ZeroStockWithTurnover_does_not_include_plain_zero()
    {
        var rows = new[]
        {
            Row(1, name: "Zero", band: InventoryCoverageBand.Zero),
            Row(2, name: "Zero giro", band: InventoryCoverageBand.Zero, zeroTurnover: true),
        };
        var result = Apply(rows, Card(InventoryIntelligenceCardKind.ZeroStockWithTurnover));
        Assert.Single(result);
        Assert.Equal("Zero giro", result[0].Name);
    }

    [Fact]
    public void Card_Critical_does_not_include_Low()
    {
        var rows = new[]
        {
            Row(1, name: "Crit", band: InventoryCoverageBand.Critical),
            Row(2, name: "Low", band: InventoryCoverageBand.Low),
            Row(3, name: "Attention", band: InventoryCoverageBand.Attention),
        };
        var result = Apply(rows, Card(InventoryIntelligenceCardKind.Critical));
        Assert.Single(result);
        Assert.Equal("Crit", result[0].Name);
    }

    [Fact]
    public void Card_Low_does_not_include_Critical()
    {
        var rows = new[]
        {
            Row(1, name: "Crit", band: InventoryCoverageBand.Critical),
            Row(2, name: "Low", band: InventoryCoverageBand.Low),
        };
        var result = Apply(rows, Card(InventoryIntelligenceCardKind.Low));
        Assert.Single(result);
        Assert.Equal("Low", result[0].Name);
    }

    [Fact]
    public void Card_Idle_uses_IsIdle_flag()
    {
        var rows = new[]
        {
            Row(1, name: "Parado", idle: true, band: InventoryCoverageBand.Normal),
            Row(2, name: "Ativo", idle: false),
        };
        var result = Apply(rows, Card(InventoryIntelligenceCardKind.Idle));
        Assert.Single(result);
        Assert.Equal("Parado", result[0].Name);
    }

    [Fact]
    public void Card_LocationAnomaly_uses_HasLocationStockAnomaly()
    {
        var rows = new[]
        {
            Row(1, name: "Anomalia", anomaly: true, band: InventoryCoverageBand.Zero),
            Row(2, name: "Ok"),
        };
        var result = Apply(rows, Card(InventoryIntelligenceCardKind.LocationAnomaly));
        Assert.Single(result);
        Assert.Equal("Anomalia", result[0].Name);
    }

    [Fact]
    public void CountCards_Low_excludes_Critical()
    {
        var rows = new[]
        {
            Row(1, band: InventoryCoverageBand.Critical),
            Row(2, band: InventoryCoverageBand.Critical),
            Row(3, band: InventoryCoverageBand.Low),
        };
        var counts = InventoryIntelligencePresentation.CountCards(rows);
        Assert.Equal(3, counts.All);
        Assert.Equal(2, counts.Critical);
        Assert.Equal(1, counts.Low);
    }

    [Fact]
    public void CountCards_is_not_affected_by_applying_a_filter()
    {
        var rows = new[]
        {
            Row(1, name: "Coca", band: InventoryCoverageBand.Critical),
            Row(2, name: "Pepsi", band: InventoryCoverageBand.Low),
            Row(3, name: "Água", band: InventoryCoverageBand.Zero, zeroTurnover: true),
            Row(4, name: "Parado", idle: true),
        };
        var before = InventoryIntelligencePresentation.CountCards(rows);
        _ = Apply(rows, new InventoryIntelligenceUiFilter
        {
            Card = InventoryIntelligenceCardKind.Critical,
            Search = "Coca",
        });
        var after = InventoryIntelligencePresentation.CountCards(rows);
        Assert.Equal(before.All, after.All);
        Assert.Equal(4, after.All);
        Assert.Equal(1, after.Critical);
        Assert.Equal(1, after.Low);
        Assert.Equal(1, after.ZeroStock);
        Assert.Equal(1, after.ZeroStockWithTurnover);
        Assert.Equal(1, after.Idle);
    }

    [Fact]
    public void Search_matches_name_case_insensitive()
    {
        var rows = new[]
        {
            Row(1, name: "Coca-Cola", code: "001"),
            Row(2, name: "Pepsi", code: "002"),
        };
        var result = Apply(rows, new InventoryIntelligenceUiFilter { Search = "coca" });
        Assert.Single(result);
        Assert.Equal("Coca-Cola", result[0].Name);
    }

    [Fact]
    public void Search_matches_code_case_insensitive()
    {
        var rows = new[]
        {
            Row(1, name: "Água", code: "ABC-9"),
            Row(2, name: "Suco", code: "XYZ"),
        };
        var result = Apply(rows, new InventoryIntelligenceUiFilter { Search = "abc" });
        Assert.Single(result);
        Assert.Equal("Água", result[0].Name);
    }

    [Theory]
    [InlineData(InventoryCoverageBand.Negative)]
    [InlineData(InventoryCoverageBand.Zero)]
    [InlineData(InventoryCoverageBand.Critical)]
    [InlineData(InventoryCoverageBand.Low)]
    [InlineData(InventoryCoverageBand.Attention)]
    [InlineData(InventoryCoverageBand.Normal)]
    [InlineData(InventoryCoverageBand.NotCalculable)]
    public void CoverageBand_filter_is_exclusive(InventoryCoverageBand band)
    {
        var rows = Enum.GetValues<InventoryCoverageBand>()
            .Select((b, i) => Row(i + 1, name: b.ToString(), band: b))
            .ToArray();
        var result = Apply(rows, new InventoryIntelligenceUiFilter { CoverageBand = band });
        Assert.Single(result);
        Assert.Equal(band.ToString(), result[0].Name);
    }

    [Fact]
    public void Silence30_uses_QualifiesSilence30()
    {
        var rows = new[]
        {
            Row(1, name: "Silêncio", evidence: true, historyDays: 40, lastSale: null, daysWithout: null),
            Row(2, name: "Recente", evidence: true, historyDays: 40, lastSale: DateTime.Today, daysWithout: 2),
            Row(3, name: "Sem evidência", evidence: false, historyDays: 40, lastSale: null),
        };
        var result = Apply(rows, new InventoryIntelligenceUiFilter { Silence30 = true });
        Assert.Single(result);
        Assert.Equal("Silêncio", result[0].Name);
    }

    [Fact]
    public void Silence60_requires_sixty_days()
    {
        var rows = new[]
        {
            Row(1, name: "30", evidence: true, historyDays: 90, lastSale: DateTime.Today.AddDays(-30), daysWithout: 30),
            Row(2, name: "60", evidence: true, historyDays: 90, lastSale: DateTime.Today.AddDays(-60), daysWithout: 60),
        };
        var result = Apply(rows, new InventoryIntelligenceUiFilter { Silence60 = true });
        Assert.Single(result);
        Assert.Equal("60", result[0].Name);
    }

    [Fact]
    public void Silence90_requires_ninety_days()
    {
        var rows = new[]
        {
            Row(1, name: "60", evidence: true, historyDays: 120, lastSale: DateTime.Today.AddDays(-60), daysWithout: 60),
            Row(2, name: "90", evidence: true, historyDays: 120, lastSale: DateTime.Today.AddDays(-90), daysWithout: 90),
        };
        var result = Apply(rows, new InventoryIntelligenceUiFilter { Silence90 = true });
        Assert.Single(result);
        Assert.Equal("90", result[0].Name);
    }

    [Fact]
    public void InsufficientHistory_uses_thirty_day_flag()
    {
        var rows = new[]
        {
            Row(1, name: "Curto", historyDays: 10, insufficient30: true),
            Row(2, name: "Cheio", historyDays: 45, insufficient30: false),
        };
        var result = Apply(rows, new InventoryIntelligenceUiFilter { InsufficientHistory = true });
        Assert.Single(result);
        Assert.Equal("Curto", result[0].Name);
    }

    [Fact]
    public void Combination_card_Critical_and_search_Coca()
    {
        var rows = new[]
        {
            Row(1, name: "Coca Zero", band: InventoryCoverageBand.Critical),
            Row(2, name: "Coca Light", band: InventoryCoverageBand.Low),
            Row(3, name: "Pepsi", band: InventoryCoverageBand.Critical),
        };
        var result = Apply(rows, new InventoryIntelligenceUiFilter
        {
            Card = InventoryIntelligenceCardKind.Critical,
            Search = "Coca",
        });
        Assert.Single(result);
        Assert.Equal("Coca Zero", result[0].Name);
    }

    [Fact]
    public void Combination_card_search_coverage_and_silence()
    {
        var rows = new[]
        {
            Row(1, name: "Coca crítica parada", band: InventoryCoverageBand.Critical,
                evidence: true, historyDays: 40, lastSale: null, daysWithout: null),
            Row(2, name: "Coca crítica recente", band: InventoryCoverageBand.Critical,
                evidence: true, historyDays: 40, lastSale: DateTime.Today, daysWithout: 1),
            Row(3, name: "Coca baixa parada", band: InventoryCoverageBand.Low,
                evidence: true, historyDays: 40, lastSale: null, daysWithout: null),
            Row(4, name: "Pepsi crítica parada", band: InventoryCoverageBand.Critical,
                evidence: true, historyDays: 40, lastSale: null, daysWithout: null),
        };
        var result = Apply(rows, new InventoryIntelligenceUiFilter
        {
            Card = InventoryIntelligenceCardKind.Critical,
            CoverageBand = InventoryCoverageBand.Critical,
            Search = "Coca",
            Silence30 = true,
        });
        Assert.Single(result);
        Assert.Equal("Coca crítica parada", result[0].Name);
    }

    [Fact]
    public void InsufficientHistory_does_not_require_physical_evidence()
    {
        var rows = new[]
        {
            Row(1, name: "Cadastro curto", evidence: false, historyDays: 10, insufficient30: true),
            Row(2, name: "Cheio", evidence: true, historyDays: 45, insufficient30: false),
        };
        var result = Apply(rows, new InventoryIntelligenceUiFilter { InsufficientHistory = true });
        Assert.Single(result);
        Assert.Equal("Cadastro curto", result[0].Name);
    }

    [Fact]
    public void Clear_filter_does_not_drop_other_rows()
    {
        var rows = new[]
        {
            Row(1, name: "A", band: InventoryCoverageBand.Critical),
            Row(2, name: "B", band: InventoryCoverageBand.Low),
        };
        var filtered = Apply(rows, new InventoryIntelligenceUiFilter
        {
            Card = InventoryIntelligenceCardKind.Critical,
            Search = "A",
        });
        Assert.Single(filtered);
        var cleared = Apply(rows, InventoryIntelligenceUiFilter.Cleared());
        Assert.Equal(2, cleared.Count);
    }

    [Fact]
    public void CoverageDays_null_formats_as_dash()
    {
        var row = InventoryIntelligencePresentation.ToGridRow(Row(coverageDays: null));
        Assert.Equal(InventoryIntelligencePresentation.EmDash, row.CoverageDisplay);
    }

    [Fact]
    public void CoverageDays_NaN_and_Infinity_format_as_dash()
    {
        Assert.Equal(InventoryIntelligencePresentation.EmDash,
            InventoryIntelligencePresentation.FormatCoverageDays(double.NaN));
        Assert.Equal(InventoryIntelligencePresentation.EmDash,
            InventoryIntelligencePresentation.FormatCoverageDays(double.PositiveInfinity));
        Assert.Equal(InventoryIntelligencePresentation.EmDash,
            InventoryIntelligencePresentation.FormatCoverageDays(double.NegativeInfinity));
    }

    [Fact]
    public void Vmv30_NaN_and_Infinity_do_not_render_as_NaN()
    {
        Assert.Equal("0", InventoryIntelligencePresentation.FormatVmv30(double.NaN));
        Assert.Equal("0", InventoryIntelligencePresentation.FormatVmv30(double.PositiveInfinity));
        Assert.DoesNotContain("NaN", InventoryIntelligencePresentation.FormatVmv30(double.NaN), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("∞", InventoryIntelligencePresentation.FormatVmv30(double.PositiveInfinity));
    }

    [Fact]
    public void LastSale_uses_ptBR_day_month_year()
    {
        var text = InventoryIntelligencePresentation.FormatLastSale(new DateTime(2026, 8, 30));
        Assert.Equal("30/08/2026", text);
    }

    [Fact]
    public void LastValidSaleDate_null_formats_as_dash()
    {
        var row = InventoryIntelligencePresentation.ToGridRow(Row(lastSale: null));
        Assert.Equal(InventoryIntelligencePresentation.EmDash, row.LastSaleDisplay);
    }

    [Fact]
    public void DaysWithoutSale_null_formats_as_dash()
    {
        var row = InventoryIntelligencePresentation.ToGridRow(Row(daysWithout: null));
        Assert.Equal(InventoryIntelligencePresentation.EmDash, row.DaysWithoutSaleDisplay);
    }

    [Fact]
    public void History_formats_as_days()
    {
        var row = InventoryIntelligencePresentation.ToGridRow(Row(historyDays: 45));
        Assert.Equal("45 dias", row.HistoryDisplay);
    }

    [Fact]
    public void Alert_priority_anomaly_over_idle_over_turnover()
    {
        var all = Row(anomaly: true, idle: true, zeroTurnover: true);
        Assert.Equal("Conferir estoque por local", InventoryIntelligencePresentation.AlertText(all));

        var idle = Row(anomaly: false, idle: true, zeroTurnover: true);
        Assert.Equal("Sem venda há 90+ dias", InventoryIntelligencePresentation.AlertText(idle));

        var giro = Row(anomaly: false, idle: false, zeroTurnover: true);
        Assert.Equal("Há giro recente", InventoryIntelligencePresentation.AlertText(giro));

        var none = Row(anomaly: false, idle: false, zeroTurnover: false);
        Assert.Equal(InventoryIntelligencePresentation.EmDash, InventoryIntelligencePresentation.AlertText(none));
    }

    [Fact]
    public void Negative_plus_location_anomaly_keeps_situation_and_alert()
    {
        var row = Row(band: InventoryCoverageBand.Negative, anomaly: true, stock: -2, fridge: 2, total: 0);
        Assert.Equal("Estoque negativo — conferir", InventoryIntelligencePresentation.SituationText(row));
        Assert.Equal("Conferir estoque por local", InventoryIntelligencePresentation.AlertText(row));
    }

    [Fact]
    public void Idle_plus_NotCalculable_keeps_situation_and_alert()
    {
        var row = Row(band: InventoryCoverageBand.NotCalculable, idle: true, coverageDays: null);
        Assert.Equal("Cobertura não calculável", InventoryIntelligencePresentation.SituationText(row));
        Assert.Equal("Sem venda há 90+ dias", InventoryIntelligencePresentation.AlertText(row));
    }

    [Fact]
    public void Zero_total_with_negative_depot_and_positive_fridge_does_not_hide_coverage()
    {
        var row = Row(
            band: InventoryCoverageBand.Zero,
            anomaly: true,
            stock: -4,
            fridge: 4,
            total: 0,
            zeroTurnover: true);
        Assert.Equal("Sem estoque — há giro recente", InventoryIntelligencePresentation.SituationText(row));
        Assert.Equal("Conferir estoque por local", InventoryIntelligencePresentation.AlertText(row));
    }

    [Theory]
    [InlineData(InventoryCoverageBand.Negative, false, "Estoque negativo — conferir")]
    [InlineData(InventoryCoverageBand.Zero, false, "Sem estoque")]
    [InlineData(InventoryCoverageBand.Zero, true, "Sem estoque — há giro recente")]
    [InlineData(InventoryCoverageBand.Critical, false, "Cobertura crítica")]
    [InlineData(InventoryCoverageBand.Low, false, "Cobertura baixa")]
    [InlineData(InventoryCoverageBand.Attention, false, "Atenção à cobertura")]
    [InlineData(InventoryCoverageBand.Normal, false, "Cobertura normal")]
    [InlineData(InventoryCoverageBand.NotCalculable, false, "Cobertura não calculável")]
    public void Situation_texts(InventoryCoverageBand band, bool zeroTurnover, string expected)
    {
        var row = Row(band: band, zeroTurnover: zeroTurnover);
        Assert.Equal(expected, InventoryIntelligencePresentation.SituationText(row));
    }

    [Fact]
    public void Empty_snapshot_and_empty_filter_messages()
    {
        Assert.Equal(
            InventoryIntelligencePresentation.EmptySnapshotMessage,
            InventoryIntelligencePresentation.EmptyStateMessage(0, 0, null));
        Assert.Equal(
            InventoryIntelligencePresentation.EmptyFilterMessage,
            InventoryIntelligencePresentation.EmptyStateMessage(10, 0, null));
        Assert.Equal(
            InventoryIntelligencePresentation.LoadErrorMessage,
            InventoryIntelligencePresentation.EmptyStateMessage(0, 0, InventoryIntelligencePresentation.LoadErrorMessage));
        Assert.Equal("", InventoryIntelligencePresentation.EmptyStateMessage(10, 3, null));
    }

    [Fact]
    public void Situation_does_not_use_forbidden_words()
    {
        var forbidden = new[] { "baixo giro", "encalhado", "excesso", "sobra", "prejuízo", "perda", "comprar", "promoção" };
        foreach (var band in Enum.GetValues<InventoryCoverageBand>())
        {
            var text = InventoryIntelligencePresentation.SituationText(Row(band: band, zeroTurnover: band == InventoryCoverageBand.Zero));
            foreach (var word in forbidden)
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ResolveLoadFailure_initial_clears_and_uses_controlled_message()
    {
        var decision = InventoryIntelligencePresentation.ResolveLoadFailure(false);
        Assert.False(decision.KeepPreviousSnapshot);
        Assert.Equal(InventoryIntelligencePresentation.LoadErrorMessage, decision.OperatorMessage);
        Assert.DoesNotContain("Exception", decision.OperatorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".db", decision.OperatorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveLoadFailure_refresh_keeps_previous_snapshot()
    {
        var decision = InventoryIntelligencePresentation.ResolveLoadFailure(true);
        Assert.True(decision.KeepPreviousSnapshot);
        Assert.Equal(InventoryIntelligencePresentation.RefreshKeepDataMessage, decision.OperatorMessage);
        Assert.DoesNotContain("Exception", decision.OperatorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NaN", decision.OperatorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
