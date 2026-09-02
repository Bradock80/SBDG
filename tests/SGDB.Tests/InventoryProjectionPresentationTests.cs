using System.Globalization;
using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70D-B3 — apresentação pura em memória. Sem SQLite, UI, motor alterado ou DateTime.Today.
/// </summary>
public class InventoryProjectionPresentationTests
{
    private static InventoryProjectionLotResult Lot(
        int id,
        InventoryProjectionLotKind kind,
        double qty,
        DateTime? expiry = null,
        int? days = null,
        bool expired = false,
        double? surplus = null,
        double? value = null) =>
        new()
        {
            LotId = id,
            Kind = kind,
            Quantity = qty,
            ExpiryDate = expiry,
            DaysUntilExpiry = days,
            AlreadyExpired = expired || kind == InventoryProjectionLotKind.AlreadyExpired,
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

    private static InventoryProjectedProduct Product(
        int id = 1,
        InventorySkuProjectionBlockedReason sku = InventorySkuProjectionBlockedReason.None,
        InventoryExpiryProjectionBlockedReason expiry = InventoryExpiryProjectionBlockedReason.None,
        double? demand = 30,
        double? excess = 0,
        int horizon = 30,
        double tracked = 0,
        double untracked = 0,
        bool fridge = false,
        IReadOnlyList<InventoryProjectionLotResult>? lots = null,
        IReadOnlyList<InventoryProjectedLotCost>? costs = null) =>
        new()
        {
            ProductId = id,
            Projection = new InventoryProjectionResult
            {
                SkuBlockedReason = sku,
                ExpiryBlockedReason = expiry,
                HorizonDays = horizon,
                ProjectedDemand = sku == InventorySkuProjectionBlockedReason.None ? demand : null,
                ProjectedExcessQuantity = sku == InventorySkuProjectionBlockedReason.None ? excess : null,
                TrackedLotQuantity = tracked,
                UntrackedWarehouseQuantity = untracked,
                HasLotLocationLimitation = fridge,
                Lots = lots ?? [],
            },
            LotCosts = costs ?? [],
        };

    private static InventoryProjectedProductPresentation Present(InventoryProjectedProduct product) =>
        InventoryProjectionPresentation.FromProduct(product);

    [Fact]
    public void Sku_blocked_is_unavailable_excess_and_em_dash()
    {
        var row = Present(Product(
            sku: InventorySkuProjectionBlockedReason.NoObservableDemand,
            expiry: InventoryExpiryProjectionBlockedReason.NoObservableDemand,
            excess: null,
            demand: null));
        Assert.Equal(InventoryProjectionExcessStatus.Unavailable, row.ExcessStatus);
        Assert.Equal("Projeção indisponível", row.ExcessStatusDisplay);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.Surplus30Display);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.ProjectedDemandDisplay);
        Assert.Equal("Sem giro observável", row.SkuBlockedShortText);
        Assert.Contains("giro observável", row.SkuBlockedExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Surplus_30d_zero_is_zero_not_dash()
    {
        var row = Present(Product(excess: 0, demand: 50));
        Assert.Equal(InventoryProjectionExcessStatus.NoExcess, row.ExcessStatus);
        Assert.Equal("Sem sobra 30d", row.ExcessStatusDisplay);
        Assert.Equal("0", row.Surplus30Display);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, row.Surplus30Display);
    }

    [Fact]
    public void Surplus_30d_positive_is_projected_excess()
    {
        var row = Present(Product(excess: 12, demand: 18));
        Assert.Equal(InventoryProjectionExcessStatus.ProjectedExcess, row.ExcessStatus);
        Assert.Equal("Sobra projetada 30d", row.ExcessStatusDisplay);
        Assert.Equal("12", row.Surplus30Display);
        Assert.Equal(12, row.ProjectedExcessQuantity);
    }

    [Fact]
    public void Product_without_lots_is_no_lot_not_invalid_expiry()
    {
        var row = Present(Product(untracked: 40, tracked: 0, lots: [], excess: 10, demand: 20));
        Assert.Equal(InventoryProjectionValidityStatus.NoLot, row.ValidityStatus);
        Assert.Equal("Sem lote identificado", row.ValidityRiskDisplay);
        Assert.NotEqual("Sem validade informada", row.ValidityRiskDisplay);
        Assert.NotEqual("Validade cadastrada inválida", row.ValidityRiskDisplay);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.ExpirySurplusDisplay);
    }

    [Fact]
    public void Undated_lot_is_not_invalid_expiry()
    {
        var row = Present(Product(
            tracked: 40,
            lots: [Lot(1, InventoryProjectionLotKind.Undated, 40)]));
        Assert.Equal(InventoryProjectionValidityStatus.Undated, row.ValidityStatus);
        Assert.Equal("Sem validade informada", row.ValidityRiskDisplay);
        Assert.NotEqual("Validade cadastrada inválida", row.ValidityRiskDisplay);
        Assert.NotEqual("Sem lote identificado", row.ValidityRiskDisplay);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.ExpirySurplusDisplay);
    }

    [Fact]
    public void Dated_lot_without_surplus_is_com_validade()
    {
        var expiry = new DateTime(2026, 9, 30);
        var row = Present(Product(
            tracked: 40,
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 40, expiry, days: 30, surplus: 0, value: 0)],
            costs: [Cost(1, LotCostSource.LotRecorded, 2)]));
        Assert.Equal(InventoryProjectionValidityStatus.Dated, row.ValidityStatus);
        Assert.Equal("Com validade", row.ValidityRiskDisplay);
        Assert.Equal("0", row.ExpirySurplusDisplay);
        Assert.Equal(0, row.ProjectedExpirySurplusQuantity);
    }

    [Fact]
    public void Expired_lot_status_is_vencido()
    {
        var row = Present(Product(
            tracked: 8,
            lots: [Lot(1, InventoryProjectionLotKind.AlreadyExpired, 8, new DateTime(2026, 8, 1), expired: true)]));
        Assert.Equal(InventoryProjectionValidityStatus.Expired, row.ValidityStatus);
        Assert.Equal("Vencido", row.ValidityRiskDisplay);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.ExpirySurplusDisplay);
        Assert.True(Assert.Single(row.Lots).AlreadyExpired);
    }

    [Fact]
    public void Expires_today_has_no_numeric_surplus()
    {
        var today = new DateTime(2026, 8, 31);
        var row = Present(Product(
            tracked: 5,
            lots: [Lot(1, InventoryProjectionLotKind.ExpiresToday, 5, today, days: 0)]));
        Assert.Equal(InventoryProjectionValidityStatus.ExpiresToday, row.ValidityStatus);
        Assert.Equal("Vence hoje", row.ValidityRiskDisplay);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.ExpirySurplusDisplay);
        Assert.Equal(InventoryProjectionPresentation.EmDash, Assert.Single(row.Lots).SurplusAtExpiryDisplay);
    }

    [Fact]
    public void Surplus_at_expiry_rolls_up_and_sets_status()
    {
        var row = Present(Product(
            tracked: 50,
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Dated, 50, new DateTime(2026, 9, 10), 10, surplus: 40, value: 80),
            ],
            costs: [Cost(1, LotCostSource.LotRecorded, 2)]));
        Assert.Equal(InventoryProjectionValidityStatus.SurplusAtExpiry, row.ValidityStatus);
        Assert.Equal("Sobra até a validade", row.ValidityRiskDisplay);
        Assert.Equal(40, row.ProjectedExpirySurplusQuantity);
        Assert.Equal("40", row.ExpirySurplusDisplay);
        Assert.Equal(80, row.ProjectedExpirySurplusValue);
        Assert.Equal(InventoryProjectionSurplusValueQuality.CompleteRecorded, row.SurplusValueQuality);
        Assert.Equal("R$ 80,00", row.SurplusValueDisplay);
        Assert.Equal("Valor estimado da sobra", row.SurplusValueCaption);
        Assert.DoesNotContain("prejuízo", row.SurplusValueCaption, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("perda", row.SurplusValueDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_expiry_is_not_undated_or_no_lot()
    {
        var row = Present(Product(
            expiry: InventoryExpiryProjectionBlockedReason.InvalidExpiryDate,
            tracked: 40,
            lots: [],
            costs: [Cost(9, LotCostSource.LotRecorded, 2)]));
        Assert.Equal(InventoryProjectionValidityStatus.InvalidExpiry, row.ValidityStatus);
        Assert.Equal("Validade cadastrada inválida", row.ValidityRiskDisplay);
        Assert.NotEqual("Sem validade informada", row.ValidityRiskDisplay);
        Assert.NotEqual("Sem lote identificado", row.ValidityRiskDisplay);
        Assert.Equal("Validade cadastrada inválida", row.ExpiryBlockedShortText);
        Assert.Contains("não é ausência de validade", row.ExpiryBlockedExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_lot_id_does_not_throw_and_is_unavailable_not_no_lot()
    {
        var product = Product(
            expiry: InventoryExpiryProjectionBlockedReason.DuplicateLotId,
            tracked: 40,
            lots: [],
            costs:
            [
                Cost(7, LotCostSource.LotRecorded, 2),
                Cost(7, LotCostSource.CurrentAverageEstimate, 3),
            ]);
        var row = Present(product);
        Assert.Equal(InventoryProjectionValidityStatus.ProjectionUnavailable, row.ValidityStatus);
        Assert.Equal("Projeção indisponível", row.ValidityRiskDisplay);
        Assert.NotEqual("Sem lote identificado", row.ValidityRiskDisplay);
        Assert.Equal("Lotes duplicados", row.ExpiryBlockedShortText);
        Assert.Equal("Custo do lote", InventoryProjectionPresentation.CostSourceLabel(
            product.LotCosts[0].CostSource));
    }

    [Fact]
    public void Lots_exceed_warehouse_sku_remains_calculable()
    {
        var row = Present(Product(
            expiry: InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse,
            excess: 20,
            demand: 30,
            tracked: 80,
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Dated, 40, new DateTime(2026, 9, 10), 10),
                Lot(2, InventoryProjectionLotKind.Dated, 40, new DateTime(2026, 11, 1), 62),
            ]));
        Assert.Equal(InventoryProjectionExcessStatus.ProjectedExcess, row.ExcessStatus);
        Assert.Equal("20", row.Surplus30Display);
        Assert.Equal(InventoryProjectionValidityStatus.Dated, row.ValidityStatus);
        Assert.Equal("Com validade", row.ValidityRiskDisplay);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.ExpirySurplusDisplay);
        Assert.Equal("Lotes excedem o depósito", row.ExpiryBlockedShortText);
        Assert.Contains(row.Alerts, a => a == "Lotes excedem o depósito");
    }

    [Fact]
    public void Expiry_blocked_sku_calculable_keeps_excess_number()
    {
        var row = Present(Product(
            expiry: InventoryExpiryProjectionBlockedReason.InvalidLotQuantity,
            excess: 0,
            demand: 40,
            tracked: 10,
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 10, new DateTime(2026, 10, 1), 31)]));
        Assert.Equal(InventoryProjectionExcessStatus.NoExcess, row.ExcessStatus);
        Assert.Equal("0", row.Surplus30Display);
        Assert.Equal("Quantidade de lote inválida", row.ExpiryBlockedShortText);
        Assert.Equal("Com validade", row.ValidityRiskDisplay);
    }

    [Fact]
    public void Vmv_zero_with_expired_lot_still_shows_vencido()
    {
        var row = Present(Product(
            sku: InventorySkuProjectionBlockedReason.NoObservableDemand,
            expiry: InventoryExpiryProjectionBlockedReason.NoObservableDemand,
            tracked: 12,
            lots: [Lot(1, InventoryProjectionLotKind.AlreadyExpired, 12, new DateTime(2026, 1, 1), expired: true)]));
        Assert.Equal(InventoryProjectionValidityStatus.Expired, row.ValidityStatus);
        Assert.Equal("Vencido", row.ValidityRiskDisplay);
        Assert.Equal(InventoryProjectionExcessStatus.Unavailable, row.ExcessStatus);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.Surplus30Display);
        Assert.Equal("Sem giro observável", row.SkuBlockedShortText);
        Assert.Contains(row.Alerts, a => a == "Sem giro observável");
    }

    [Fact]
    public void Insufficient_history_with_expired_lot_still_shows_vencido()
    {
        var row = Present(Product(
            sku: InventorySkuProjectionBlockedReason.InsufficientHistory,
            expiry: InventoryExpiryProjectionBlockedReason.InsufficientHistory,
            tracked: 4,
            lots: [Lot(1, InventoryProjectionLotKind.AlreadyExpired, 4, new DateTime(2026, 7, 1), expired: true)]));
        Assert.Equal(InventoryProjectionValidityStatus.Expired, row.ValidityStatus);
        Assert.Equal("Vencido", row.ValidityRiskDisplay);
        Assert.Equal("Histórico insuficiente", row.SkuBlockedShortText);
        Assert.Equal("Projeção indisponível", row.ExcessStatusDisplay);
    }

    [Fact]
    public void Partial_untracked_does_not_say_sem_validade()
    {
        var row = Present(Product(
            tracked: 70,
            untracked: 30,
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Dated, 70, new DateTime(2026, 9, 20), 20, surplus: 10, value: 20),
            ],
            costs: [Cost(1, LotCostSource.LotRecorded, 2)]));
        Assert.True(row.HasUntrackedWarehouse);
        Assert.Equal("30 un. do depósito sem lote identificado", row.UntrackedWarehouseAlert);
        Assert.DoesNotContain("sem validade", row.UntrackedWarehouseAlert, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(row.Alerts, a => a == row.UntrackedWarehouseAlert);
        Assert.Equal("70", row.TrackedLotQuantityDisplay);
        Assert.Equal("30", row.UntrackedWarehouseQuantityDisplay);
    }

    [Fact]
    public void Fridge_limitation_does_not_claim_lot_location()
    {
        var row = Present(Product(
            fridge: true,
            tracked: 40,
            lots: [Lot(1, InventoryProjectionLotKind.Dated, 40, new DateTime(2026, 10, 1), 31, surplus: 0)]));
        Assert.True(row.HasLotLocationLimitation);
        Assert.Equal(
            "Projeção por lote não distingue depósito e geladeira.",
            row.FridgeLimitationAlert);
        Assert.DoesNotContain("está na geladeira", row.FridgeLimitationAlert, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(row.Alerts, a => a == row.FridgeLimitationAlert);
    }

    [Fact]
    public void Value_complete_recorded_uses_lot_cost()
    {
        var row = Present(Product(
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Dated, 10, surplus: 10, value: 25),
            ],
            costs: [Cost(1, LotCostSource.LotRecorded, 2.5)]));
        Assert.Equal(InventoryProjectionSurplusValueQuality.CompleteRecorded, row.SurplusValueQuality);
        Assert.Equal("R$ 25,00", row.SurplusValueDisplay);
        Assert.Equal("Custo do lote", row.SurplusValueQualityDisplay);
        Assert.Equal("Custo do lote", Assert.Single(row.Lots).CostSourceDisplay);
        Assert.DoesNotContain("*", row.SurplusValueDisplay);
        Assert.DoesNotContain("parcial", row.SurplusValueDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Value_complete_with_estimate_is_marked()
    {
        var row = Present(Product(
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Dated, 8, surplus: 8, value: 16),
                Lot(2, InventoryProjectionLotKind.Dated, 4, surplus: 4, value: 12),
            ],
            costs:
            [
                Cost(1, LotCostSource.LotRecorded, 2),
                Cost(2, LotCostSource.CurrentAverageEstimate, 3),
            ]));
        Assert.Equal(InventoryProjectionSurplusValueQuality.CompleteWithEstimate, row.SurplusValueQuality);
        Assert.Equal(28, row.ProjectedExpirySurplusValue);
        Assert.Equal("R$ 28,00*", row.SurplusValueDisplay);
        Assert.Equal("Estimado pelo custo médio atual", row.SurplusValueQualityDisplay);
        Assert.EndsWith("*", row.Lots.Single(l => l.LotId == 2).SurplusValueDisplay);
    }

    [Fact]
    public void Surplus_lot_without_cost_is_partial_not_full_total()
    {
        var row = Present(Product(
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Dated, 10, surplus: 10, value: 100),
                Lot(2, InventoryProjectionLotKind.Dated, 5, surplus: 5, value: null),
            ],
            costs:
            [
                Cost(1, LotCostSource.LotRecorded, 10),
                Cost(2, LotCostSource.Unavailable),
            ]));
        Assert.Equal(InventoryProjectionSurplusValueQuality.Partial, row.SurplusValueQuality);
        Assert.Equal(100, row.ProjectedExpirySurplusValue);
        Assert.Equal("R$ 100,00 (parcial)", row.SurplusValueDisplay);
        Assert.Equal("Valor parcial", row.SurplusValueQualityDisplay);
        Assert.NotEqual("R$ 100,00", row.SurplusValueDisplay);
        Assert.Equal(15, row.ProjectedExpirySurplusQuantity);
    }

    [Fact]
    public void Zero_surplus_without_cost_is_not_partial()
    {
        var row = Present(Product(
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Dated, 10, surplus: 10, value: 40),
                Lot(2, InventoryProjectionLotKind.Dated, 5, surplus: 0, value: null),
            ],
            costs:
            [
                Cost(1, LotCostSource.LotRecorded, 4),
                Cost(2, LotCostSource.Unavailable),
            ]));
        Assert.Equal(InventoryProjectionSurplusValueQuality.CompleteRecorded, row.SurplusValueQuality);
        Assert.Equal("R$ 40,00", row.SurplusValueDisplay);
        Assert.DoesNotContain("parcial", row.SurplusValueDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, row.ProjectedExpirySurplusQuantity);
    }

    [Fact]
    public void Surplus_without_any_cost_is_unavailable_value()
    {
        var row = Present(Product(
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Dated, 6, surplus: 6, value: null),
                Lot(2, InventoryProjectionLotKind.Dated, 3, surplus: 3, value: null),
            ],
            costs:
            [
                Cost(1, LotCostSource.Unavailable),
                Cost(2, LotCostSource.Unavailable),
            ]));
        Assert.Equal(InventoryProjectionSurplusValueQuality.Unavailable, row.SurplusValueQuality);
        Assert.Null(row.ProjectedExpirySurplusValue);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.SurplusValueDisplay);
        Assert.Equal("Sem custo disponível", row.SurplusValueQualityDisplay);
        Assert.Equal(9, row.ProjectedExpirySurplusQuantity);
    }

    [Fact]
    public void Cost_correlates_by_lot_id_not_list_position()
    {
        var row = Present(Product(
            lots:
            [
                Lot(20, InventoryProjectionLotKind.Dated, 4, surplus: 4, value: 8),
                Lot(10, InventoryProjectionLotKind.Dated, 6, surplus: 6, value: 30),
            ],
            costs:
            [
                Cost(10, LotCostSource.LotRecorded, 5),
                Cost(20, LotCostSource.CurrentAverageEstimate, 2),
            ]));
        Assert.Equal(LotCostSource.CurrentAverageEstimate, row.Lots.Single(l => l.LotId == 20).CostSource);
        Assert.Equal(LotCostSource.LotRecorded, row.Lots.Single(l => l.LotId == 10).CostSource);
        Assert.Equal("Estimado pelo custo médio atual", row.Lots.Single(l => l.LotId == 20).CostSourceDisplay);
        Assert.Equal("Custo do lote", row.Lots.Single(l => l.LotId == 10).CostSourceDisplay);
        Assert.Equal(InventoryProjectionSurplusValueQuality.CompleteWithEstimate, row.SurplusValueQuality);
        Assert.Equal(38, row.ProjectedExpirySurplusValue);
    }

    [Fact]
    public void Duplicate_cost_lot_id_uses_first_and_does_not_throw()
    {
        var row = Present(Product(
            lots: [Lot(3, InventoryProjectionLotKind.Dated, 5, surplus: 5, value: 15)],
            costs:
            [
                Cost(3, LotCostSource.LotRecorded, 3),
                Cost(3, LotCostSource.CurrentAverageEstimate, 99),
            ]));
        Assert.Equal(LotCostSource.LotRecorded, Assert.Single(row.Lots).CostSource);
        Assert.Equal(InventoryProjectionSurplusValueQuality.CompleteRecorded, row.SurplusValueQuality);
        Assert.Equal("R$ 15,00", row.SurplusValueDisplay);
    }

    [Fact]
    public void Formatting_is_pt_br_under_en_us_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            var row = Present(Product(
                excess: 1234,
                demand: 12.5,
                lots:
                [
                    Lot(1, InventoryProjectionLotKind.Dated, 1.5, new DateTime(2026, 9, 5), 5, surplus: 1.25, value: 1234.56),
                ],
                costs: [Cost(1, LotCostSource.LotRecorded, 987.648)]));

            Assert.Equal("1.234", row.Surplus30Display);
            Assert.Equal("12,500", InventoryProjectionPresentation.FormatQty(12.5));
            Assert.Equal("1,500", row.Lots[0].QuantityDisplay);
            Assert.Equal("05/09/2026", row.Lots[0].ExpiryDisplay);
            Assert.Equal("R$ 1.234,56", row.SurplusValueDisplay);
            Assert.DoesNotContain(",", row.Surplus30Display, StringComparison.Ordinal);
            Assert.StartsWith("R$ ", row.SurplusValueDisplay);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void Null_quantities_are_em_dash_zero_is_zero()
    {
        var blocked = Present(Product(
            sku: InventorySkuProjectionBlockedReason.InsufficientHistory,
            expiry: InventoryExpiryProjectionBlockedReason.InsufficientHistory));
        Assert.Equal("—", blocked.Surplus30Display);
        Assert.Equal("—", blocked.ProjectedDemandDisplay);
        Assert.Equal("—", blocked.ExpirySurplusDisplay);
        Assert.Equal("—", blocked.SurplusValueDisplay);

        var zero = Present(Product(excess: 0, demand: 0, lots: [
            Lot(1, InventoryProjectionLotKind.Dated, 1, surplus: 0, value: 0),
        ], costs: [Cost(1, LotCostSource.LotRecorded, 1)]));
        Assert.Equal("0", zero.Surplus30Display);
        Assert.Equal("0", zero.ProjectedDemandDisplay);
        Assert.Equal("0", zero.ExpirySurplusDisplay);
        Assert.Equal("R$ 0,00", zero.SurplusValueDisplay);
    }

    [Fact]
    public void Fractional_quantity_uses_up_to_three_decimals_pt_br()
    {
        Assert.Equal("1,250", InventoryProjectionPresentation.FormatQty(1.25));
        Assert.Equal("0,001", InventoryProjectionPresentation.FormatQty(0.001));
        var row = Present(Product(excess: 2.5, lots: [
            Lot(1, InventoryProjectionLotKind.Dated, 2.5, surplus: 1.125, value: 1),
        ], costs: [Cost(1, LotCostSource.LotRecorded, 1)]));
        Assert.Equal("2,500", row.Surplus30Display);
        Assert.Equal("1,125", row.ExpirySurplusDisplay);
    }

    [Fact]
    public void Multiple_lot_kinds_use_priority_expired_over_surplus()
    {
        var row = Present(Product(
            tracked: 30,
            untracked: 5,
            lots:
            [
                Lot(1, InventoryProjectionLotKind.Undated, 5),
                Lot(2, InventoryProjectionLotKind.Dated, 10, new DateTime(2026, 10, 1), 31, surplus: 8, value: 16),
                Lot(3, InventoryProjectionLotKind.ExpiresToday, 4, new DateTime(2026, 8, 31), 0),
                Lot(4, InventoryProjectionLotKind.AlreadyExpired, 6, new DateTime(2026, 8, 1), expired: true),
            ],
            costs: [Cost(2, LotCostSource.LotRecorded, 2)]));
        Assert.Equal(InventoryProjectionValidityStatus.Expired, row.ValidityStatus);
        Assert.Equal("Vencido", row.ValidityRiskDisplay);
        Assert.Equal(4, row.Lots.Count);
        Assert.Equal(8, row.ProjectedExpirySurplusQuantity);
    }

    [Fact]
    public void Lot_input_order_does_not_change_summary()
    {
        InventoryProjectionLotResult[] Forward() =>
        [
            Lot(8, InventoryProjectionLotKind.Undated, 2),
            Lot(9, InventoryProjectionLotKind.Dated, 10, new DateTime(2026, 9, 15), 15, surplus: 7, value: 21),
            Lot(1, InventoryProjectionLotKind.AlreadyExpired, 3, expired: true),
        ];
        InventoryProjectionLotResult[] Reverse() =>
        [
            Lot(1, InventoryProjectionLotKind.AlreadyExpired, 3, expired: true),
            Lot(9, InventoryProjectionLotKind.Dated, 10, new DateTime(2026, 9, 15), 15, surplus: 7, value: 21),
            Lot(8, InventoryProjectionLotKind.Undated, 2),
        ];

        var a = Present(Product(tracked: 15, lots: Forward(), costs: [Cost(9, LotCostSource.LotRecorded, 3)]));
        var b = Present(Product(tracked: 15, lots: Reverse(), costs: [Cost(9, LotCostSource.LotRecorded, 3)]));
        Assert.Equal(a.ValidityStatus, b.ValidityStatus);
        Assert.Equal(a.ValidityRiskDisplay, b.ValidityRiskDisplay);
        Assert.Equal(a.ProjectedExpirySurplusQuantity, b.ProjectedExpirySurplusQuantity);
        Assert.Equal(a.ProjectedExpirySurplusValue, b.ProjectedExpirySurplusValue);
        Assert.Equal(a.SurplusValueQuality, b.SurplusValueQuality);
        Assert.Equal("Vencido", a.ValidityRiskDisplay);
    }

    [Fact]
    public void Apply_uses_intelligence_order_and_name_without_query()
    {
        var snap = new InventoryProjectionSnapshot
        {
            Today = new DateTime(2026, 8, 31),
            Intelligence = new InventoryIntelligenceSnapshot
            {
                Today = new DateTime(2026, 8, 31),
                Rows =
                [
                    new ProductTurnoverRow { ProductId = 2, Name = "Beta", Code = "B" },
                    new ProductTurnoverRow { ProductId = 1, Name = "Alfa", Code = "A" },
                ],
            },
            ByProductId = new Dictionary<int, InventoryProjectedProduct>
            {
                [1] = Product(id: 1, excess: 3),
                [2] = Product(id: 2, excess: 0),
            },
        };

        var presented = InventoryProjectionPresentation.Apply(snap);
        Assert.Equal(2, presented.Products.Count);
        Assert.Equal(2, presented.Products[0].ProductId);
        Assert.Equal("Beta", presented.Products[0].Name);
        Assert.Equal("0", presented.Products[0].Surplus30Display);
        Assert.Equal("Alfa", presented.Products[1].Name);
        Assert.Equal("3", presented.Products[1].Surplus30Display);
        Assert.Equal("Sobra projetada 30d", presented.ByProductId[1].ExcessStatusDisplay);
        Assert.Equal(new DateTime(2026, 8, 31), presented.Today);
    }

    [Fact]
    public void Demand_caption_states_horizon_and_is_not_a_sale()
    {
        var row = Present(Product(horizon: 30, demand: 12, excess: 0));
        Assert.Equal("Demanda projetada em 30 dias", row.DemandCaption);
        Assert.Equal("12", row.ProjectedDemandDisplay);
        Assert.Equal(30, row.HorizonDays);
        Assert.Contains("Não é venda garantida", InventoryProjectionPresentation.DemandNotGuaranteed, StringComparison.Ordinal);
    }

    [Fact]
    public void Composition_and_no_evidence_blocked_texts()
    {
        var composition = Present(Product(
            sku: InventorySkuProjectionBlockedReason.CompositionProduct,
            expiry: InventoryExpiryProjectionBlockedReason.CompositionProduct));
        Assert.Equal("Produto composto", composition.SkuBlockedShortText);
        Assert.Equal("Produto composto", composition.ExpiryBlockedShortText);

        var evidence = Present(Product(
            sku: InventorySkuProjectionBlockedReason.NoPhysicalEvidence,
            expiry: InventoryExpiryProjectionBlockedReason.NoPhysicalEvidence));
        Assert.Equal("Sem histórico confiável", evidence.SkuBlockedShortText);

        var invalid = Present(Product(sku: InventorySkuProjectionBlockedReason.InvalidInput));
        Assert.Equal("Dados inválidos", invalid.SkuBlockedShortText);

        Assert.Equal(
            "Estoque inconsistente",
            InventoryProjectionPresentation.SkuBlockedText(InventorySkuProjectionBlockedReason.NegativeStock).ShortText);
        Assert.Equal(
            "Estoque inconsistente",
            InventoryProjectionPresentation.ExpiryBlockedText(InventoryExpiryProjectionBlockedReason.NegativeWarehouseStock).ShortText);
    }

    [Fact]
    public void Expires_today_beats_dated_surplus_in_summary()
    {
        var row = Present(Product(
            lots:
            [
                Lot(1, InventoryProjectionLotKind.ExpiresToday, 2, days: 0),
                Lot(2, InventoryProjectionLotKind.Dated, 9, surplus: 4, value: 8),
            ],
            costs: [Cost(2, LotCostSource.LotRecorded, 2)]));
        Assert.Equal(InventoryProjectionValidityStatus.ExpiresToday, row.ValidityStatus);
        Assert.Equal("Vence hoje", row.ValidityRiskDisplay);
        Assert.Equal(4, row.ProjectedExpirySurplusQuantity);
    }

    [Fact]
    public void Invalid_expiry_beats_expired_when_engine_cleared_lots()
    {
        var row = Present(Product(
            expiry: InventoryExpiryProjectionBlockedReason.InvalidExpiryDate,
            tracked: 10,
            lots: [Lot(1, InventoryProjectionLotKind.AlreadyExpired, 10, expired: true)]));
        Assert.Equal(InventoryProjectionValidityStatus.InvalidExpiry, row.ValidityStatus);
        Assert.Equal("Validade cadastrada inválida", row.ValidityRiskDisplay);
    }

    [Fact]
    public void Engine_vmv_zero_and_expired_lot_still_presents_vencido()
    {
        var today = new DateTime(2026, 8, 31);
        var result = InventoryProjectionEngine.Project(new InventoryProjectionRequest
        {
            Today = today,
            Vmv30 = 0,
            HistoryDays = 40,
            HasPhysicalAvailabilityEvidence = true,
            TotalStock = 12,
            WarehouseStock = 12,
            FridgeStock = 0,
            HorizonDays = 30,
            Lots =
            [
                new InventoryProjectionLotInput
                {
                    LotId = 1,
                    Quantity = 12,
                    ExpiryDate = today.AddDays(-1),
                },
            ],
        });
        var row = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = result,
        });
        Assert.Equal(InventorySkuProjectionBlockedReason.NoObservableDemand, result.SkuBlockedReason);
        Assert.Equal(InventoryProjectionValidityStatus.Expired, row.ValidityStatus);
        Assert.Equal("Vencido", row.ValidityRiskDisplay);
        Assert.Equal(InventoryProjectionExcessStatus.Unavailable, row.ExcessStatus);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.Surplus30Display);
    }

    [Fact]
    public void Engine_insufficient_history_and_expired_lot_still_presents_vencido()
    {
        var today = new DateTime(2026, 8, 31);
        var result = InventoryProjectionEngine.Project(new InventoryProjectionRequest
        {
            Today = today,
            Vmv30 = 2,
            HistoryDays = 29,
            IsHistoryInsufficient30 = true,
            HasPhysicalAvailabilityEvidence = true,
            TotalStock = 4,
            WarehouseStock = 4,
            FridgeStock = 0,
            HorizonDays = 30,
            Lots =
            [
                new InventoryProjectionLotInput
                {
                    LotId = 1,
                    Quantity = 4,
                    ExpiryDate = today.AddDays(-3),
                },
            ],
        });
        var row = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = result,
        });
        Assert.Equal(InventorySkuProjectionBlockedReason.InsufficientHistory, result.SkuBlockedReason);
        Assert.Equal(InventoryProjectionValidityStatus.Expired, row.ValidityStatus);
        Assert.Equal("Vencido", row.ValidityRiskDisplay);
    }

    [Fact]
    public void Presentation_source_has_no_io_or_clock()
    {
        var path = FindSource("InventoryProjectionPresentation.cs");
        Assert.True(File.Exists(path), path);
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetByProductId", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Load(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
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
