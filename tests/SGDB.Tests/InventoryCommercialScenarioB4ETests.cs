using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 70F-B4E — integração read-only no detalhe. Sem EXE, sem banco da loja, sem writes.
/// </summary>
public class InventoryCommercialScenarioB4ETests
{
    [Fact]
    public void Pipeline_query_budget_e_9()
    {
        Assert.Equal(9, InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount);
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialEligibilityComposer.ExpectedQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount
            + InventoryCommercialMarginPolicyResolver.ExpectedQueryCount
            + InventoryCommercialPriceFloorEngine.ExpectedQueryCount
            + InventoryCommercialScenarioEngine.ExpectedQueryCount
            + InventoryCommercialScenarioComposer.ExpectedQueryCount
            + InventoryCommercialScenarioPresentation.ExpectedQueryCount);
        Assert.Equal(0, InventoryCommercialEligibilityComposer.ExpectedQueryCount);
        Assert.Equal(0, InventoryCommercialScenarioPresentation.ExpectedQueryCount);
    }

    [Fact]
    public void Load_compõe_B4_apos_70e_e_atribui_somente_no_sucesso()
    {
        var cs = ReadViewCs();
        var load = MethodBody(cs, "private void Load()");
        Assert.Contains("InventoryCommercialEligibilityComposer.Build(snapshot, attention)", load, StringComparison.Ordinal);
        Assert.Contains("InventoryCommercialFactsService.Load(", load, StringComparison.Ordinal);
        Assert.Contains("InventoryCommercialMarginSettingsService.Load()", load, StringComparison.Ordinal);
        Assert.Contains("InventoryCommercialMarginPolicyResolver.Resolve(setting)", load, StringComparison.Ordinal);
        Assert.Contains("InventoryCommercialScenarioComposer.Compose(", load, StringComparison.Ordinal);
        Assert.Contains("InventoryCommercialScenarioPresentation.Apply(commercial)", load, StringComparison.Ordinal);

        var facts = load.IndexOf("InventoryCommercialFactsService.Load(", StringComparison.Ordinal);
        var settings = load.IndexOf("InventoryCommercialMarginSettingsService.Load()", StringComparison.Ordinal);
        var compose = load.IndexOf("InventoryCommercialScenarioComposer.Compose(", StringComparison.Ordinal);
        var present = load.IndexOf("InventoryCommercialScenarioPresentation.Apply(commercial)", StringComparison.Ordinal);
        var assign = load.IndexOf("_commercialPresented = commercialPresented;", StringComparison.Ordinal);
        Assert.True(facts > 0 && settings > facts && compose > settings && present > compose && assign > present);

        Assert.Equal(1, CountOccurrences(cs, "InventoryProjectionService.Load("));
        Assert.Equal(1, CountOccurrences(cs, "InventoryCommercialFactsService.Load("));
        Assert.Equal(1, CountOccurrences(cs, "InventoryCommercialMarginSettingsService.Load("));
        Assert.Equal(1, CountOccurrences(cs, "InventoryCommercialScenarioComposer.Compose("));
        Assert.Equal(1, CountOccurrences(cs, "InventoryCommercialScenarioPresentation.Apply("));
        Assert.Equal(1, CountOccurrences(cs, "InventoryCommercialEligibilityComposer.Build("));
        Assert.DoesNotContain("InventoryIntelligenceService.Load", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_e_B2_uma_vez_no_Load()
    {
        var cs = ReadViewCs();
        Assert.Equal(1, CountOccurrences(cs, "InventoryCommercialMarginSettingsService.Load("));
        Assert.Equal(1, CountOccurrences(cs, "InventoryCommercialFactsService.Load("));
        Assert.DoesNotContain("MarginSettingsService.Load()", MethodBody(cs, "private void ApplyView()"), StringComparison.Ordinal);
        Assert.DoesNotContain("FactsService.Load(", MethodBody(cs, "private void ApplyView()"), StringComparison.Ordinal);
    }

    [Fact]
    public void Detalhe_e_ApplyView_zero_query()
    {
        var cs = ReadViewCs();
        var open = MethodBody(cs, "private void OpenProjectionDetail_Click");
        Assert.Contains("_commercialPresented", open, StringComparison.Ordinal);
        Assert.Contains("ByProductId", ReadDetailCs());
        Assert.DoesNotContain("InventoryCommercialFactsService", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialMarginSettingsService", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialScenarioComposer", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialScenarioEngine", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialEligibilityEngine", open, StringComparison.Ordinal);

        var apply = MethodBody(cs, "private void ApplyView()");
        Assert.DoesNotContain("FactsService.Load", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsService.Load", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("ScenarioComposer", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionService.Load", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Lookup_usa_ByProductId()
    {
        var detail = ReadDetailCs();
        Assert.Contains("ResolveForDetail(commercial, productId)", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefault", detail, StringComparison.Ordinal);
        var presentation = ReadSource("src", "SGDB.App", "Models", "InventoryCommercialScenarioPresentation.cs");
        var resolve = MethodBody(presentation, "public static InventoryCommercialScenarioPresentationRow ResolveForDetail");
        Assert.Contains("TryGetValue", resolve, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefault", resolve, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_falho_preserva_B4_com_70d_70e()
    {
        var cs = ReadViewCs();
        var keep = cs.IndexOf("failure.Value.KeepPreviousSnapshot", StringComparison.Ordinal);
        var nextElse = cs.IndexOf("else", keep, StringComparison.Ordinal);
        var keepBlock = cs[keep..nextElse];
        Assert.DoesNotContain("_commercial =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_commercialPresented =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_snapshot =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_attentionPresented =", keepBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_sucesso_substitui_B4()
    {
        var load = MethodBody(ReadViewCs(), "private void Load()");
        Assert.Contains("_commercial = commercial;", load, StringComparison.Ordinal);
        Assert.Contains("_commercialPresented = commercialPresented;", load, StringComparison.Ordinal);
    }

    [Fact]
    public void Double_click_e_botao_detalhe_preservados()
    {
        var cs = ReadViewCs();
        var xaml = ReadViewXaml();
        Assert.Contains("Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenProduct();", cs, StringComparison.Ordinal);
        Assert.Contains("Content=\"Detalhar projeção\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenProjectionDetail_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenLots_Click", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenProjectionDetail", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Grid_sem_coluna_comercial_nova()
    {
        var xaml = ReadViewXaml();
        Assert.DoesNotContain("Header=\"Cenário comercial\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Piso financeiro\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Margem mínima\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Motivo\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Prioridade\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Nenhum_write_promo_ou_sale_price()
    {
        foreach (var path in new[]
                 {
                     ReadViewCs(),
                     ReadWindowCs(),
                     ReadWindowXaml(),
                     ReadDetailCs(),
                     ReadSource("src", "SGDB.App", "Services", "InventoryCommercialEligibilityComposer.cs"),
                 })
        {
            Assert.DoesNotContain("sale_price", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preco_promocional", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_inicio", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_fim", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("desconto_percent", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE ", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Aplicar preço", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Criar promoção", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Ativar cenário", path, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Sem_RPC_e_cliente_bloqueado()
    {
        var cs = ReadViewCs();
        var clientIdx = cs.IndexOf("StoreNetworkMode.IsClient", StringComparison.Ordinal);
        var loadIdx = cs.IndexOf("InventoryProjectionService.Load()", StringComparison.Ordinal);
        var factsIdx = cs.IndexOf("InventoryCommercialFactsService.Load(", StringComparison.Ordinal);
        Assert.InRange(clientIdx, 0, loadIdx - 1);
        Assert.True(factsIdx > loadIdx);
        var host = ReadSource("src", "SGDB.App", "Services", "StoreNetworkHost.cs");
        Assert.DoesNotContain("InventoryCommercialScenario", host, StringComparison.Ordinal);
        Assert.DoesNotContain("70F-B4", host, StringComparison.Ordinal);
        var blocked = MethodBody(cs, "private void ShowClientBlocked()");
        Assert.Contains("new InventoryCommercialScenarioSnapshot()", blocked, StringComparison.Ordinal);
        Assert.DoesNotContain("FactsService.Load", blocked, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_somente_presentation_B4D()
    {
        var cs = ReadWindowCs();
        var xaml = ReadWindowXaml();
        Assert.DoesNotContain("using SGDB.Services", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialScenarioEngine", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialScenarioComposer", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialPriceFloorEngine", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductPriceHelper", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatMoney", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatPercent", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ReasonLabel(", MethodBody(cs, "private void BindCommercial"), StringComparison.Ordinal);
        Assert.DoesNotContain("StatusLabel(", MethodBody(cs, "private void BindCommercial"), StringComparison.Ordinal);
        Assert.DoesNotContain("ThesisLabel(", MethodBody(cs, "private void BindCommercial"), StringComparison.Ordinal);
        Assert.DoesNotContain("KindLabel(", MethodBody(cs, "private void BindCommercial"), StringComparison.Ordinal);
        Assert.DoesNotContain("switch", MethodBody(cs, "private void BindCommercial"), StringComparison.Ordinal);
        Assert.Contains("commercial.StatusLabel", cs, StringComparison.Ordinal);
        Assert.Contains("commercial.ThesisLabel", cs, StringComparison.Ordinal);
        Assert.Contains("commercial.Scenarios", cs, StringComparison.Ordinal);
        Assert.Contains("Text=\"CENÁRIOS COMERCIAIS\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Fechar (Esc)\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Aplicar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Criar promoção", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Alterar preço", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Confirmar desconto", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Salvar cenário", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"820\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"480\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"960\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"680\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Venda por", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Preço sugerido", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Preço promocional", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Você precisa vender", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Promova", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Click=\"Apply", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Piso_nao_esta_no_template_de_cenario()
    {
        var xaml = ReadWindowXaml();
        var list = xaml.IndexOf("x:Name=\"CommercialScenariosList\"", StringComparison.Ordinal);
        var warnings = xaml.IndexOf("x:Name=\"CommercialWarningsPanel\"", StringComparison.Ordinal);
        Assert.True(list > 0 && warnings > list);
        var template = xaml[list..warnings];
        Assert.Contains("KindLabel", template, StringComparison.Ordinal);
        Assert.Contains("SimulatedPriceText", template, StringComparison.Ordinal);
        Assert.Contains("ReductionSummaryText", template, StringComparison.Ordinal);
        Assert.Contains("GrossMarginText", template, StringComparison.Ordinal);
        Assert.DoesNotContain("FloorPriceText", template, StringComparison.Ordinal);
        Assert.DoesNotContain("CommercialFloorText", template, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommercialFloorText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FloorLimitHint", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Exemplo_10_820_940_880()
    {
        var presented = InventoryCommercialScenarioPresentation.FromResult(AvailableTwo());
        Assert.Equal(ProductPriceHelper.MoneyBr(10), presented.CurrentCatalogPriceText);
        Assert.Equal(ProductPriceHelper.MoneyBr(8.20), presented.FloorPriceText);
        Assert.Equal(ProductPriceHelper.MoneyBr(1.80), presented.FinancialRoomText);
        Assert.Equal(2, presented.Scenarios.Count);
        Assert.Equal(ProductPriceHelper.MoneyBr(9.40), presented.Scenarios[0].SimulatedPriceText);
        Assert.Equal(ProductPriceHelper.MoneyBr(8.80), presented.Scenarios[1].SimulatedPriceText);
        Assert.DoesNotContain(presented.Scenarios, s => s.SimulatedPriceText == presented.FloorPriceText);
        Assert.Equal("Piso financeiro", presented.FloorPriceLabel);
        Assert.Equal("Cenário leve", presented.Scenarios[0].KindLabel);
        Assert.Equal("Cenário moderado", presented.Scenarios[1].KindLabel);

        var detail = DetailWith(presented);
        Assert.Equal(presented.FloorPriceText, detail.Commercial.FloorPriceText);
        Assert.Equal(2, detail.Commercial.Scenarios.Count);
    }

    [Fact]
    public void Available_um_cenario()
    {
        var presented = InventoryCommercialScenarioPresentation.FromResult(AvailableOne());
        Assert.True(presented.IsScenarioAvailable);
        Assert.Single(presented.Scenarios);
        Assert.Equal("Cenário leve", presented.Scenarios[0].KindLabel);
    }

    [Fact]
    public void Expired_oculta_opcoes()
    {
        var presented = InventoryCommercialScenarioPresentation.FromResult(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.Expired,
            PrimaryReason = InventoryCommercialScenarioReason.Expired,
            Scenarios = AvailableTwo().Scenarios,
        });
        var detail = DetailWith(presented);
        Assert.Equal("Produto vencido — retirar/conferir.", detail.Commercial.Explanation);
        Assert.False(detail.Commercial.ShowScenarioOptions);
        Assert.False(detail.Commercial.ShowFinancialAnalysis);
        Assert.Empty(detail.Commercial.Scenarios);
    }

    [Fact]
    public void ExpiresToday_Idle_Limited_Policy_e_financial()
    {
        Assert.Contains("priorizar saída", Present(Monitor(InventoryCommercialScenarioReason.ExpiresToday)).Explanation);
        Assert.Contains("parado", Present(Monitor(InventoryCommercialScenarioReason.Idle, InventoryCommercialScenarioThesis.Idle)).Explanation);
        Assert.Contains("limitações", Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.MonitorOnly,
            PrimaryReason = InventoryCommercialScenarioReason.LimitedConfidence,
            Confidence = InventoryAttentionConfidence.Limited,
        }).Explanation);
        var missing = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.PolicyMissing,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyMissing,
        });
        Assert.Contains("Política comercial", missing.ActionGuidance);
        Assert.Equal("—", missing.MinimumGrossMarginText);
        var invalid = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.PolicyInvalid,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyInvalid,
        });
        Assert.Contains("Política comercial", invalid.ActionGuidance);
        var zero = Present(AvailableTwo(minMargin: 0));
        Assert.Equal("0%", zero.MinimumGrossMarginText);
        Assert.DoesNotContain("não configurada", zero.StatusLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("não configurada", zero.MinimumGrossMarginText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("custo", Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.UnknownCost,
        }).Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não vendável", Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.NotSellable,
        }).Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("composto", Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.CompositionProduct,
        }).Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ambígua", Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.AmbiguousSaleUnit,
        }).Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("piso", Present(Monitor(InventoryCommercialScenarioReason.PriceAtFloor)).Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("abaixo", Present(Monitor(InventoryCommercialScenarioReason.PriceBelowFloor)).Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("espaço", Present(Monitor(InventoryCommercialScenarioReason.NoFinancialRoom)).Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_B4_nao_inventa_cenario()
    {
        var snap = Snapshot70C(1);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var detail = InventoryProjectionDetail.TryCreate(snap, presented, 1);
        Assert.NotNull(detail);
        Assert.True(detail!.Commercial.IsJoinMissing);
        Assert.Equal(InventoryCommercialScenarioPresentation.MissingAnalysis, detail.Commercial.Explanation);
        Assert.Empty(detail.Commercial.Scenarios);
        Assert.False(detail.Commercial.IsScenarioAvailable);
    }

    [Fact]
    public void Eligibility_composer_e_On_e_nao_escolhe_extra()
    {
        var source = ReadSource("src", "SGDB.App", "Services", "InventoryCommercialEligibilityComposer.cs");
        Assert.DoesNotContain("FirstOrDefault", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseService", source, StringComparison.Ordinal);
        var row = new ProductTurnoverRow { ProductId = 3, TotalStock = 10, Stock = 10 };
        var snapshot = new InventoryProjectionSnapshot
        {
            Intelligence = new InventoryIntelligenceSnapshot { Rows = [row] },
            ByProductId = new Dictionary<int, InventoryProjectedProduct>
            {
                [3] = new() { ProductId = 3 },
                [99] = new() { ProductId = 99 },
            },
        };
        var attention = new InventoryAttentionSnapshot
        {
            Results = [new InventoryAttentionResult { ProductId = 3, Confidence = InventoryAttentionConfidence.Reliable }],
            ByProductId = new Dictionary<int, InventoryAttentionResult>
            {
                [3] = new() { ProductId = 3, Confidence = InventoryAttentionConfidence.Reliable },
            },
        };
        var eligibility = InventoryCommercialEligibilityComposer.Build(snapshot, attention);
        Assert.Single(eligibility);
        Assert.Equal(3, eligibility[0].ProductId);
        Assert.Equal(new[] { 3 }, InventoryCommercialEligibilityComposer.ProductIds(snapshot));
    }

    [Fact]
    public void Window_tem_hierarquia_e_scroll()
    {
        var xaml = ReadWindowXaml();
        var heading = xaml.IndexOf("CENÁRIOS COMERCIAIS", StringComparison.Ordinal);
        var status = xaml.IndexOf("CommercialStatusText", StringComparison.Ordinal);
        var finance = xaml.IndexOf("CommercialFinancePanel", StringComparison.Ordinal);
        var qty = xaml.IndexOf("CommercialQuantityPanel", StringComparison.Ordinal);
        var scenarios = xaml.IndexOf("CommercialScenariosList", StringComparison.Ordinal);
        var footer = xaml.IndexOf("CommercialFooterText", StringComparison.Ordinal);
        var fechar = xaml.IndexOf("Fechar (Esc)", StringComparison.Ordinal);
        Assert.True(heading > 0 && status > heading && finance > status && qty > finance
                    && scenarios > qty && footer > scenarios && fechar > footer);
        var scroll = xaml.IndexOf("<ScrollViewer", StringComparison.Ordinal);
        Assert.True(scroll > 0 && heading > scroll && fechar > xaml.IndexOf("</ScrollViewer>", StringComparison.Ordinal));
        Assert.Contains("Outras observações", xaml, StringComparison.Ordinal);
        Assert.Contains("CommercialWarningsPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Ver produto\"", ReadViewXaml(), StringComparison.Ordinal);
        Assert.Contains("Content=\"Lotes e validades\"", ReadViewXaml(), StringComparison.Ordinal);
        Assert.Contains("Content=\"Detalhar projeção\"", ReadViewXaml(), StringComparison.Ordinal);
        Assert.Contains("Key.Escape", ReadWindowCs(), StringComparison.Ordinal);
    }

    [Fact]
    public void Fixtures_visuais_A_a_I()
    {
        var two = Present(AvailableTwo());
        Assert.Equal(InventoryCommercialScenarioPresentation.StatusAvailable, two.StatusLabel);
        Assert.Equal(InventoryCommercialScenarioPresentation.ThesisExcess30, two.ThesisLabel);
        Assert.Equal(ProductPriceHelper.MoneyBr(10), two.CurrentCatalogPriceText);
        Assert.Equal(ProductPriceHelper.MoneyBr(8.20), two.FloorPriceText);
        Assert.Equal(ProductPriceHelper.MoneyBr(1.80), two.FinancialRoomText);
        Assert.Equal(2, two.Scenarios.Count);
        Assert.Equal(ProductPriceHelper.MoneyBr(9.40), two.Scenarios[0].SimulatedPriceText);
        Assert.Equal(ProductPriceHelper.MoneyBr(8.80), two.Scenarios[1].SimulatedPriceText);
        Assert.Equal(InventoryCommercialScenarioPresentation.FloorCaption, two.FloorPriceLabel);
        Assert.DoesNotContain(two.Scenarios, s => s.KindLabel.Contains("Piso", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("projeção 30 dias", two.AttentionQuantityLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InventoryCommercialScenarioPresentation.SimulationDisclaimerText, two.SimulationDisclaimer);
        Assert.Equal(InventoryCommercialScenarioPresentation.OperatorFooterText, two.OperatorFooter);

        var one = Present(AvailableOne());
        Assert.True(one.IsScenarioAvailable);
        Assert.Single(one.Scenarios);
        Assert.Equal(InventoryCommercialScenarioPresentation.KindLight, one.Scenarios[0].KindLabel);

        Assert.Equal(
            InventoryCommercialScenarioPresentation.ExpiredExplanation,
            Present(new InventoryCommercialScenarioResult
            {
                Status = InventoryCommercialScenarioStatus.Expired,
                PrimaryReason = InventoryCommercialScenarioReason.Expired,
            }).Explanation);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.ExpiresTodayExplanation,
            Present(Monitor(InventoryCommercialScenarioReason.ExpiresToday)).Explanation);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.IdleExplanation,
            Present(Monitor(InventoryCommercialScenarioReason.Idle, InventoryCommercialScenarioThesis.Idle)).Explanation);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.LimitedExplanation,
            Present(new InventoryCommercialScenarioResult
            {
                Status = InventoryCommercialScenarioStatus.MonitorOnly,
                PrimaryReason = InventoryCommercialScenarioReason.LimitedConfidence,
                Confidence = InventoryAttentionConfidence.Limited,
            }).Explanation);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.PolicyMissingExplanation,
            Present(new InventoryCommercialScenarioResult
            {
                Status = InventoryCommercialScenarioStatus.PolicyMissing,
                PrimaryReason = InventoryCommercialScenarioReason.PolicyMissing,
            }).Explanation);
        Assert.Equal("0%", Present(AvailableTwo(minMargin: 0)).MinimumGrossMarginText);
        Assert.Contains(
            "custo",
            Present(new InventoryCommercialScenarioResult
            {
                Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
                PrimaryReason = InventoryCommercialScenarioReason.UnknownCost,
            }).Explanation,
            StringComparison.OrdinalIgnoreCase);
    }

    static InventoryCommercialScenarioPresentationRow Present(InventoryCommercialScenarioResult result) =>
        InventoryCommercialScenarioPresentation.FromResult(result);

    static InventoryProjectionDetail DetailWith(InventoryCommercialScenarioPresentationRow commercial)
    {
        var snap = Snapshot70C(1);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var commercialSnap = new InventoryCommercialScenarioPresentationSnapshot
        {
            Rows = [commercial],
            ByProductId = new Dictionary<int, InventoryCommercialScenarioPresentationRow>
            {
                [1] = commercial,
            },
        };
        return InventoryProjectionDetail.TryCreate(snap, presented, 1, commercial: commercialSnap)!;
    }

    static InventoryProjectionSnapshot Snapshot70C(int id) =>
        new()
        {
            Intelligence = new InventoryIntelligenceSnapshot
            {
                Rows = [new ProductTurnoverRow { ProductId = id, Name = "P", TotalStock = 10, Stock = 10 }],
            },
            ByProductId = new Dictionary<int, InventoryProjectedProduct>
            {
                [id] = new() { ProductId = id },
            },
        };

    static InventoryCommercialScenarioResult AvailableTwo(double? minMargin = 20) =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.Available,
            PrimaryReason = InventoryCommercialScenarioReason.ProjectedExcess30,
            Thesis = InventoryCommercialScenarioThesis.ProjectedExcess30,
            CurrentCatalogPrice = 10,
            CurrentGrossMarginPercent = 40,
            MinimumGrossMarginPercent = minMargin,
            MinimumAllowedCatalogPrice = 8.20,
            FinancialRoomAmount = 1.80,
            AttentionQuantity = 8,
            AttentionQuantitySource = InventoryCommercialAttentionQuantitySource.ProjectedExcess30,
            Scenarios =
            [
                new InventoryCommercialScenario
                {
                    Kind = InventoryCommercialScenarioKind.Light,
                    SimulatedCatalogPrice = 9.40,
                    ReductionAmount = 0.60,
                    ReductionPercent = 6,
                    GrossMarginPercent = 36.17,
                },
                new InventoryCommercialScenario
                {
                    Kind = InventoryCommercialScenarioKind.Moderate,
                    SimulatedCatalogPrice = 8.80,
                    ReductionAmount = 1.20,
                    ReductionPercent = 12,
                    GrossMarginPercent = 31.82,
                },
            ],
        };

    static InventoryCommercialScenarioResult AvailableOne() =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.Available,
            PrimaryReason = InventoryCommercialScenarioReason.ProjectedExcess30,
            Thesis = InventoryCommercialScenarioThesis.ProjectedExcess30,
            CurrentCatalogPrice = 10,
            MinimumAllowedCatalogPrice = 9.98,
            FinancialRoomAmount = 0.02,
            Scenarios =
            [
                new InventoryCommercialScenario
                {
                    Kind = InventoryCommercialScenarioKind.Light,
                    SimulatedCatalogPrice = 9.99,
                    ReductionAmount = 0.01,
                    ReductionPercent = 0.1,
                    GrossMarginPercent = 40,
                },
            ],
        };

    static InventoryCommercialScenarioResult Monitor(
        InventoryCommercialScenarioReason reason,
        InventoryCommercialScenarioThesis thesis = InventoryCommercialScenarioThesis.None) =>
        new()
        {
            Status = InventoryCommercialScenarioStatus.MonitorOnly,
            PrimaryReason = reason,
            Thesis = thesis,
        };

    static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var brace = source.IndexOf('{', start);
        Assert.True(brace > start);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        return source[start..];
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

    static string ReadViewCs() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml.cs");

    static string ReadViewXaml() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml");

    static string ReadWindowCs() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml.cs");

    static string ReadWindowXaml() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml");

    static string ReadDetailCs() =>
        ReadSource("src", "SGDB.App", "Models", "InventoryProjectionDetail.cs");

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

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}
