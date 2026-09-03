using System.IO;
using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// 70D-B5C — janela somente leitura e botão Detalhar projeção.
/// Sem instanciar WPF, sem Load de banco, sem EXE.
/// </summary>
public class InventoryProjectionDetailWindowB5CTests
{
    [Fact]
    public void Module_adds_explicit_button_and_keeps_double_click_on_product()
    {
        var xaml = ReadViewXaml();
        var cs = ReadViewCs();
        Assert.Contains("Content=\"Detalhar projeção\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BtnDetailProjection\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OpenProjectionDetail_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BtnDetailProjection.IsEnabled", cs, StringComparison.Ordinal);

        Assert.Contains("Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenProduct();", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenProjectionDetail", cs, StringComparison.Ordinal);

        var openProduct = MethodBody(cs, "private void OpenProduct()");
        Assert.Contains("ProductFormWindow", openProduct, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionDetailWindow", openProduct, StringComparison.Ordinal);
        Assert.Contains("ProdutosEditar", openProduct, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_click_uses_in_memory_envelope_without_query_or_mutation()
    {
        var cs = ReadViewCs();
        var body = MethodBody(cs, "private void OpenProjectionDetail_Click");
        Assert.Contains("InventoryProjectionDetail.TryCreate(", body, StringComparison.Ordinal);
        Assert.Contains("row.ProductId, _attentionPresented, _commercialPresented, _promotionPresented)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionComposer", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionEngine", body, StringComparison.Ordinal);
        Assert.Contains("new InventoryProjectionDetailWindow(detail)", body, StringComparison.Ordinal);
        Assert.Contains("ShowDialog()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryIntelligenceService.Load", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ListByProduct", body, StringComparison.Ordinal);
        Assert.DoesNotContain("GetByProductId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionEngine.Project", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductLotsWindow", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductFormWindow", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Load();", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyView()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ProdutosEditar", body, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(cs, "InventoryProjectionService.Load("));
    }

    [Fact]
    public void Window_is_read_only_close_only_and_has_no_forbidden_actions()
    {
        var xaml = ReadWindowXaml();
        var cs = ReadWindowCs();
        Assert.Contains("InventoryProjectionDetailWindow", cs, StringComparison.Ordinal);
        Assert.Contains("Content=\"Fechar (Esc)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"Close_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.Escape", cs, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanUserAddRows=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanUserDeleteRows=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding LotNumberDisplay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding=\"{Binding LotId}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Número do lote\"", xaml, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("Salvar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Editar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Excluir", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Transferir", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Ajustar estoque", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Alterar preço", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Promoção", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Combo", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Comprar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Lotes e validades", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Ver produto", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("prejuízo certo", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("perda garantida", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vai perder", xaml, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("using SGDB.Services", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("GetByProductId", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ListByProduct", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionEngine", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductLotsWindow", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductFormWindow", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectedExcessValue", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Horizon_section_has_quantity_not_money()
    {
        var xaml = ReadWindowXaml();
        var start = xaml.IndexOf("B5C-HORIZON-START", StringComparison.Ordinal);
        var end = xaml.IndexOf("B5C-HORIZON-END", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var horizon = xaml[start..end];
        Assert.Contains("Projeção em 30 dias", horizon, StringComparison.Ordinal);
        Assert.Contains("Surplus30Text", horizon, StringComparison.Ordinal);
        Assert.Contains("DemandNotGuaranteed", horizon, StringComparison.Ordinal);
        Assert.DoesNotContain("R$", horizon, StringComparison.Ordinal);
        Assert.DoesNotContain("SurplusValueDisplay", horizon, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectedExpirySurplusValue", horizon, StringComparison.Ordinal);
        Assert.Contains("ExpiryValueText", xaml, StringComparison.Ordinal);
        Assert.Contains("SurplusValueCaption", ReadWindowCs(), StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_lots_message_keeps_invalid_expiry_distinct_from_no_lot()
    {
        var noLot = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult(),
        });
        Assert.Equal(InventoryProjectionValidityStatus.NoLot, noLot.ValidityStatus);
        Assert.Equal(
            InventoryProjectionPresentation.ValidityNoLotLabel,
            InventoryProjectionDetailUi.EmptyLotsMessage(noLot));

        var invalid = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                ExpiryBlockedReason = InventoryExpiryProjectionBlockedReason.InvalidExpiryDate,
            },
        });
        Assert.Equal(InventoryProjectionValidityStatus.InvalidExpiry, invalid.ValidityStatus);
        var emptyInvalid = InventoryProjectionDetailUi.EmptyLotsMessage(invalid);
        Assert.Equal(invalid.ExpiryBlockedExplanation, emptyInvalid);
        Assert.DoesNotContain("Sem validade informada", emptyInvalid, StringComparison.Ordinal);
        Assert.DoesNotContain(
            InventoryProjectionPresentation.ValidityNoLotLabel,
            emptyInvalid,
            StringComparison.Ordinal);
        Assert.Contains("aaaa-MM-dd", emptyInvalid, StringComparison.Ordinal);

        var dated = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                Lots = [new InventoryProjectionLotResult { LotId = 1, Kind = InventoryProjectionLotKind.Dated, Quantity = 4 }],
            },
        });
        Assert.Equal("", InventoryProjectionDetailUi.EmptyLotsMessage(dated));
    }

    [Fact]
    public void Surplus_value_explanations_match_quality_and_never_call_loss()
    {
        Assert.Equal(
            InventoryProjectionDetailUi.UnavailableValueExplanation,
            InventoryProjectionDetailUi.SurplusValueExplanation(new InventoryProjectedProductPresentation()));

        var recorded = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                Lots = [Lot(1, InventoryProjectionLotKind.Dated, 10, 4, 8)],
            },
            LotCosts = [new InventoryProjectedLotCost { LotId = 1, CostSource = LotCostSource.LotRecorded, UsedCost = 2 }],
        });
        Assert.Equal(InventoryProjectionDetailUi.RecordedValueExplanation, InventoryProjectionDetailUi.SurplusValueExplanation(recorded));

        var estimated = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                Lots = [Lot(1, InventoryProjectionLotKind.Dated, 10, 4, 8)],
            },
            LotCosts = [new InventoryProjectedLotCost { LotId = 1, CostSource = LotCostSource.CurrentAverageEstimate, UsedCost = 2 }],
        });
        Assert.Equal(InventoryProjectionDetailUi.EstimatedValueExplanation, InventoryProjectionDetailUi.SurplusValueExplanation(estimated));
        Assert.Contains("não é o custo lançado no lote", estimated.SurplusValueQualityDisplay + InventoryProjectionDetailUi.EstimatedValueExplanation, StringComparison.OrdinalIgnoreCase);

        var partial = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                Lots =
                [
                    Lot(1, InventoryProjectionLotKind.Dated, 10, 4, 8),
                    Lot(2, InventoryProjectionLotKind.Dated, 10, 3, null),
                ],
            },
            LotCosts =
            [
                new InventoryProjectedLotCost { LotId = 1, CostSource = LotCostSource.LotRecorded, UsedCost = 2 },
                new InventoryProjectedLotCost { LotId = 2, CostSource = LotCostSource.Unavailable },
            ],
        });
        Assert.Equal(InventoryProjectionDetailUi.PartialValueExplanation, InventoryProjectionDetailUi.SurplusValueExplanation(partial));

        var unavailable = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                Lots = [Lot(1, InventoryProjectionLotKind.Dated, 10, 4, null)],
            },
            LotCosts = [new InventoryProjectedLotCost { LotId = 1, CostSource = LotCostSource.Unavailable }],
        });
        Assert.Equal(InventoryProjectionDetailUi.UnavailableValueExplanation, InventoryProjectionDetailUi.SurplusValueExplanation(unavailable));

        foreach (var text in new[]
        {
            InventoryProjectionDetailUi.RecordedValueExplanation,
            InventoryProjectionDetailUi.EstimatedValueExplanation,
            InventoryProjectionDetailUi.PartialValueExplanation,
            InventoryProjectionDetailUi.UnavailableValueExplanation,
        })
        {
            Assert.DoesNotContain("prejuízo", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("perda", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Detail_window_bind_source_covers_lot_states_tracking_and_fridge()
    {
        var expired = Present(Lot(1, InventoryProjectionLotKind.AlreadyExpired, 6));
        var today = Present(Lot(2, InventoryProjectionLotKind.ExpiresToday, 3));
        var surplus = Present(Lot(3, InventoryProjectionLotKind.Dated, 30, 10, 20));
        var dated = Present(Lot(4, InventoryProjectionLotKind.Dated, 30, 0, 0));
        var undated = Present(Lot(5, InventoryProjectionLotKind.Undated, 8));
        Assert.Equal("Vencido", expired.ValidityRiskDisplay);
        Assert.Equal("Vence hoje", today.ValidityRiskDisplay);
        Assert.Equal("Sobra até a validade", surplus.ValidityRiskDisplay);
        Assert.Equal("10", Assert.Single(surplus.Lots).SurplusAtExpiryDisplay);
        Assert.Equal("0", Assert.Single(dated.Lots).SurplusAtExpiryDisplay);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, Assert.Single(dated.Lots).SurplusAtExpiryDisplay);
        Assert.Equal("Sem validade informada", undated.ValidityRiskDisplay);
        Assert.Equal(InventoryProjectionPresentation.EmDash, Assert.Single(undated.Lots).SurplusAtExpiryDisplay);

        var identities = Present(
            Lot(9, InventoryProjectionLotKind.Dated, 12, 2, 4),
            identities: [new InventoryProjectedLotIdentity { LotId = 9, LotNumber = "L-99" }]);
        Assert.Equal("L-99", Assert.Single(identities.Lots).LotNumberDisplay);
        Assert.NotEqual("9", Assert.Single(identities.Lots).LotNumberDisplay);

        var fridge = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                HasLotLocationLimitation = true,
                UntrackedWarehouseQuantity = 5,
                TrackedLotQuantity = 20,
                Lots = [Lot(1, InventoryProjectionLotKind.Dated, 20, 4, 8)],
            },
            LotCosts = [new InventoryProjectedLotCost { LotId = 1, CostSource = LotCostSource.LotRecorded, UsedCost = 2 }],
        });
        Assert.Equal(InventoryProjectionPresentation.FridgeLimitationText, fridge.FridgeLimitationAlert);
        Assert.DoesNotContain("está na geladeira", fridge.FridgeLimitationAlert, StringComparison.OrdinalIgnoreCase);
        Assert.True(fridge.HasUntrackedWarehouse);
        Assert.Contains("sem lote identificado", fridge.UntrackedWarehouseAlert, StringComparison.Ordinal);

        var cs = ReadWindowCs();
        Assert.Contains("HasLotLocationLimitation", cs, StringComparison.Ordinal);
        Assert.Contains("FridgeLimitationAlert", cs, StringComparison.Ordinal);
        Assert.Contains("EmptyLotsMessage", cs, StringComparison.Ordinal);
        Assert.Contains("ToGridRow", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_and_em_dash_stay_distinct_in_detail_displays()
    {
        var zero = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                ProjectedDemand = 30,
                ProjectedExcessQuantity = 0,
                HorizonDays = 30,
            },
        });
        var blocked = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 2,
            Projection = new InventoryProjectionResult
            {
                SkuBlockedReason = InventorySkuProjectionBlockedReason.NoObservableDemand,
                ExpiryBlockedReason = InventoryExpiryProjectionBlockedReason.NoObservableDemand,
                HorizonDays = 30,
            },
        });
        Assert.Equal("0", zero.Surplus30Display);
        Assert.Equal(InventoryProjectionPresentation.EmDash, blocked.Surplus30Display);
        Assert.NotEqual(zero.Surplus30Display, blocked.Surplus30Display);
        Assert.Equal("Sem giro observável no período. Não é possível projetar demanda nem sobra.", blocked.SkuBlockedExplanation);

        var ui = ReadUiSource();
        var window = ReadWindowXaml() + ReadWindowCs();
        Assert.DoesNotContain("prejuízo certo", ui, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("perda garantida", ui, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lote no depósito", window, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lote na geladeira", window, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProjectedExcessValue", ui + window, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", ui, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".Load(", ui, StringComparison.Ordinal);
    }

    static InventoryProjectionLotResult Lot(
        int id,
        InventoryProjectionLotKind kind,
        double qty,
        double? surplus = null,
        double? value = null) =>
        new()
        {
            LotId = id,
            Kind = kind,
            Quantity = qty,
            AlreadyExpired = kind == InventoryProjectionLotKind.AlreadyExpired,
            ProjectedSurplusAtExpiry = surplus,
            ProjectedSurplusValue = value,
        };

    static InventoryProjectedProductPresentation Present(
        InventoryProjectionLotResult lot,
        IReadOnlyList<InventoryProjectedLotIdentity>? identities = null) =>
        InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult { Lots = [lot], TrackedLotQuantity = lot.Quantity },
            LotIdentities = identities ?? [],
        });

    static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var open = source.IndexOf('{', start);
        Assert.True(open > start, signature);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[open..(i + 1)];
            }
        }

        return source[open..];
    }

    static int CountOccurrences(string text, string value)
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

    static string ReadViewCs() => ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml.cs");
    static string ReadViewXaml() => ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml");
    static string ReadWindowCs() => ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml.cs");
    static string ReadWindowXaml() => ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml");
    static string ReadUiSource() => ReadSource("src", "SGDB.App", "Models", "InventoryProjectionDetailUi.cs");

    static string ReadSource(params string[] relative)
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
