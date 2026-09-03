using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70F-B5E — integração read-only da sugestão comercial no detalhe.
/// Sem EXE, sem banco da loja, sem writes, sem PDV.
/// </summary>
public class InventoryPromotionSuggestionB5ETests
{
    [Fact]
    public void Pipeline_query_budget_permanece_9()
    {
        Assert.Equal(9, InventoryPromotionSuggestionComposer.ExpectedPipelineQueryCount);
        Assert.Equal(0, InventoryPromotionSuggestionComposer.ExpectedQueryCount);
        Assert.Equal(0, InventoryPromotionSuggestionEngine.ExpectedQueryCount);
        Assert.Equal(0, InventoryPromotionSuggestionPresentation.ExpectedQueryCount);
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
            + InventoryCommercialScenarioPresentation.ExpectedQueryCount
            + InventoryPromotionSuggestionComposer.ExpectedQueryCount
            + InventoryPromotionSuggestionPresentation.ExpectedQueryCount);
    }

    [Fact]
    public void Load_compoe_B5C_e_B5D_depois_da_B4_e_atribui_somente_no_sucesso()
    {
        var load = MethodBody(ReadViewCs(), "private void Load()");
        Assert.Contains("InventoryPromotionSuggestionComposer.Compose(snapshot.Intelligence, commercial)", load, StringComparison.Ordinal);
        Assert.Contains("InventoryPromotionSuggestionPresentation.Apply(promotion)", load, StringComparison.Ordinal);

        var presentB4 = load.IndexOf("InventoryCommercialScenarioPresentation.Apply(commercial)", StringComparison.Ordinal);
        var composeB5 = load.IndexOf("InventoryPromotionSuggestionComposer.Compose(", StringComparison.Ordinal);
        var presentB5 = load.IndexOf("InventoryPromotionSuggestionPresentation.Apply(promotion)", StringComparison.Ordinal);
        var assign = load.IndexOf("_promotionPresented = promotionPresented;", StringComparison.Ordinal);
        Assert.True(presentB4 > 0 && composeB5 > presentB4 && presentB5 > composeB5 && assign > presentB5);

        Assert.Equal(1, CountOccurrences(ReadViewCs(), "InventoryPromotionSuggestionComposer.Compose("));
        Assert.Equal(1, CountOccurrences(ReadViewCs(), "InventoryPromotionSuggestionPresentation.Apply("));
        Assert.Equal(1, CountOccurrences(ReadViewCs(), "InventoryProjectionService.Load("));
    }

    [Fact]
    public void Refresh_sucesso_substitui_snapshot_B5()
    {
        var load = MethodBody(ReadViewCs(), "private void Load()");
        Assert.Contains("_promotion = promotion;", load, StringComparison.Ordinal);
        Assert.Contains("_promotionPresented = promotionPresented;", load, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_falho_preserva_snapshot_B5()
    {
        var cs = ReadViewCs();
        var keep = cs.IndexOf("failure.Value.KeepPreviousSnapshot", StringComparison.Ordinal);
        var nextElse = cs.IndexOf("else", keep, StringComparison.Ordinal);
        var keepBlock = cs[keep..nextElse];
        Assert.DoesNotContain("_promotion =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_promotionPresented =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_commercialPresented =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_snapshot =", keepBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void B5C_e_B5D_nao_adicionam_query()
    {
        Assert.Equal(0, InventoryPromotionSuggestionComposer.ExpectedQueryCount);
        Assert.Equal(0, InventoryPromotionSuggestionPresentation.ExpectedQueryCount);
        var load = MethodBody(ReadViewCs(), "private void Load()");
        Assert.DoesNotContain("InventoryPromotionSuggestionEngine", load, StringComparison.Ordinal);
        Assert.Contains("snapshot.Intelligence", load, StringComparison.Ordinal);
    }

    [Fact]
    public void Populacao_permanece_70C()
    {
        var load = MethodBody(ReadViewCs(), "private void Load()");
        Assert.Contains("Compose(snapshot.Intelligence, commercial)", load, StringComparison.Ordinal);
        var apply = MethodBody(ReadViewCs(), "private void ApplyView()");
        Assert.Contains("_snapshot.Intelligence.Rows", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("PromotionSuggestion", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Sugestão\"", ReadViewXaml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Grid_sem_coluna_nova()
    {
        var xaml = ReadViewXaml();
        Assert.DoesNotContain("Header=\"Ação comercial\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Sugestão\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Motivo\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Prioridade\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Detalhe_e_ApplyView_zero_query()
    {
        var cs = ReadViewCs();
        var open = MethodBody(cs, "private void OpenProjectionDetail_Click");
        Assert.Contains("_promotionPresented", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPromotionSuggestionComposer", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPromotionSuggestionEngine", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", open, StringComparison.Ordinal);
        Assert.DoesNotContain("FactsService.Load", open, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefault", open, StringComparison.Ordinal);

        var apply = MethodBody(cs, "private void ApplyView()");
        Assert.DoesNotContain("PromotionSuggestionComposer", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionService.Load", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Lookup_usa_ByProductId_O1()
    {
        var detail = ReadDetailCs();
        Assert.Contains("ResolveForDetail(promotion, productId)", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefault", detail, StringComparison.Ordinal);
        var presentation = ReadSource("src", "SGDB.App", "Models", "InventoryPromotionSuggestionPresentation.cs");
        var resolve = MethodBody(presentation, "public static InventoryPromotionSuggestionPresentationRow ResolveForDetail");
        Assert.Contains("TryGetValue", resolve, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefault", resolve, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach", resolve, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_B5_e_seguro()
    {
        var snap = Snapshot70C(1);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var detail = InventoryProjectionDetail.TryCreate(snap, presented, 1);
        Assert.NotNull(detail);
        Assert.True(detail!.PromotionSuggestion.IsJoinMissing);
        Assert.Equal(InventoryPromotionSuggestionPresentation.MissingAnalysis, detail.PromotionSuggestion.Explanation);
        Assert.Empty(detail.PromotionSuggestion.ScenarioOptions);
        Assert.False(detail.PromotionSuggestion.IsSuggested);
    }

    [Fact]
    public void Suggested_aparece_com_acao_objetivo_e_quantidade()
    {
        var excess = DetailFrom(Eval(AvailableExcess()));
        Assert.Equal(InventoryPromotionSuggestionPresentation.StatusSuggested, excess.PromotionSuggestion.StatusLabel);
        Assert.Equal(InventoryPromotionSuggestionPresentation.ActionConsiderPromotion, excess.PromotionSuggestion.ActionLabel);
        Assert.Equal(InventoryPromotionSuggestionPresentation.ObjectiveReduceExcess30, excess.PromotionSuggestion.ObjectiveLabel);
        Assert.Equal("Quantidade em atenção", excess.PromotionSuggestion.AttentionQuantityLabel);
        Assert.Equal(InventoryProjectionPresentation.FormatQty(8), excess.PromotionSuggestion.AttentionQuantityText);
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.QuantitySourceExcess30,
            excess.PromotionSuggestion.AttentionQuantitySourceLabel);
        Assert.Equal(InventoryAttentionPresentation.ConfidenceReliable, excess.PromotionSuggestion.ConfidenceLabel);
        Assert.Contains("não altera preços nem ativa promoções automaticamente", excess.PromotionSuggestion.DisclaimerText, StringComparison.OrdinalIgnoreCase);

        var expiry = DetailFrom(Eval(AvailableExpiry()));
        Assert.Equal(InventoryPromotionSuggestionPresentation.ObjectiveReduceExpiry, expiry.PromotionSuggestion.ObjectiveLabel);
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.QuantitySourceExpiry,
            expiry.PromotionSuggestion.AttentionQuantitySourceLabel);
    }

    [Fact]
    public void Priority_presente_e_ausente()
    {
        var with = DetailFrom(WithPriority(Eval(AvailableExcess()), InventoryAttentionPriority.High));
        Assert.Equal("Alta", with.PromotionSuggestion.PriorityLabel);
        var missing = DetailFrom(Eval(AvailableExcess()));
        Assert.Equal(InventoryProjectionPresentation.EmDash, missing.PromotionSuggestion.PriorityLabel);
        Assert.NotEqual("Normal", missing.PromotionSuggestion.PriorityLabel);
    }

    [Fact]
    public void Warnings_0_e_atacado()
    {
        var zero = DetailFrom(Eval(Clone(AvailableExcess(), minMargin: 0)));
        Assert.Contains(
            InventoryPromotionSuggestionPresentation.WarningMinimumMarginAllowsAtCost,
            zero.PromotionSuggestion.WarningLabels);
        Assert.True(zero.PromotionSuggestion.IsSuggested);

        var wholesale = DetailFrom(InventoryPromotionSuggestionEngine.Evaluate(
            AvailableExcess(), hasWholesalePricing: true));
        Assert.Contains(
            InventoryPromotionSuggestionPresentation.WarningWholesalePricingMayDiffer,
            wholesale.PromotionSuggestion.WarningLabels);
        Assert.Contains("pode", wholesale.PromotionSuggestion.WarningLabels[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expired_ExpiresToday_Idle_HighCoverage_Limited_sem_reducao()
    {
        var expired = DetailFrom(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.Expired,
            PrimaryReason = InventoryCommercialScenarioReason.Expired,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light(), Moderate()],
        }));
        Assert.Equal("Produto vencido", expired.PromotionSuggestion.StatusLabel);
        Assert.Equal("Retirar / conferir", expired.PromotionSuggestion.ActionLabel);
        Assert.Empty(expired.PromotionSuggestion.ScenarioOptions);
        Assert.Empty(InventoryPromotionSuggestionDetailUi.PossibilityLines(expired.PromotionSuggestion));
        Assert.Contains("incentivo", expired.PromotionSuggestion.DisclaimerText, StringComparison.OrdinalIgnoreCase);

        var today = DetailFrom(Eval(Monitor(InventoryCommercialScenarioReason.ExpiresToday, scenarios: [Light()])));
        Assert.Equal("Priorizar saída / exposição", today.PromotionSuggestion.ActionLabel);
        Assert.Empty(today.PromotionSuggestion.ScenarioOptions);
        Assert.False(InventoryPromotionSuggestionDetailUi.ShowPossibilities(today.PromotionSuggestion));

        var idle = DetailFrom(Eval(Monitor(
            InventoryCommercialScenarioReason.Idle, InventoryCommercialScenarioThesis.Idle, [Light()])));
        Assert.Equal("Produto parado", idle.PromotionSuggestion.PrimaryReasonLabel);
        Assert.Empty(idle.PromotionSuggestion.ScenarioOptions);

        var coverage = DetailFrom(Eval(Monitor(
            InventoryCommercialScenarioReason.HighCoverageMonitoring,
            InventoryCommercialScenarioThesis.HighCoverage)));
        Assert.Equal("Monitorar", coverage.PromotionSuggestion.ActionLabel);
        Assert.Empty(coverage.PromotionSuggestion.ScenarioOptions);

        var limited = DetailFrom(Eval(Clone(AvailableExcess(), confidence: InventoryAttentionConfidence.Limited)));
        Assert.Equal("Análise com limitações", limited.PromotionSuggestion.ConfidenceLabel);
        Assert.Empty(limited.PromotionSuggestion.ScenarioOptions);
        Assert.False(InventoryPromotionSuggestionDetailUi.ShowPossibilities(limited.PromotionSuggestion));
    }

    [Fact]
    public void Review_Missing_Duplicate_Policy_Financial()
    {
        var review = DetailFrom(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.ReviewData,
            PrimaryReason = InventoryCommercialScenarioReason.InvalidInput,
            Confidence = InventoryAttentionConfidence.Reliable,
        }));
        Assert.True(review.PromotionSuggestion.IsReviewData);
        Assert.Equal("Revisar dados", review.PromotionSuggestion.StatusLabel);

        var missing = DetailFrom(new InventoryPromotionSuggestionResult
        {
            Status = InventoryPromotionSuggestionStatus.ReviewData,
            Action = InventoryPromotionSuggestionAction.ReviewData,
            PrimaryReason = InventoryPromotionSuggestionReason.ScenarioMissing,
        });
        Assert.Contains("estrutural", missing.PromotionSuggestion.Explanation, StringComparison.OrdinalIgnoreCase);

        var duplicate = DetailFrom(new InventoryPromotionSuggestionResult
        {
            Status = InventoryPromotionSuggestionStatus.ReviewData,
            Action = InventoryPromotionSuggestionAction.ReviewData,
            PrimaryReason = InventoryPromotionSuggestionReason.DuplicateScenario,
        });
        Assert.Contains("mais de um cenário", duplicate.PromotionSuggestion.Explanation, StringComparison.OrdinalIgnoreCase);

        var policyMissing = DetailFrom(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.PolicyMissing,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyMissing,
            Confidence = InventoryAttentionConfidence.Reliable,
        }));
        Assert.Contains("Sistema → Política comercial", policyMissing.PromotionSuggestion.Explanation, StringComparison.Ordinal);
        Assert.Empty(policyMissing.PromotionSuggestion.ScenarioOptions);

        var policyInvalid = DetailFrom(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.PolicyInvalid,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyInvalid,
            Confidence = InventoryAttentionConfidence.Reliable,
        }));
        Assert.Contains("inválida", policyInvalid.PromotionSuggestion.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(policyInvalid.PromotionSuggestion.ScenarioOptions);

        var financial = DetailFrom(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.UnknownCost,
            Confidence = InventoryAttentionConfidence.Reliable,
        }));
        Assert.Equal("Análise financeira indisponível", financial.PromotionSuggestion.StatusLabel);
        Assert.DoesNotContain("R$ 0,00", financial.PromotionSuggestion.Explanation, StringComparison.Ordinal);
        Assert.Empty(financial.PromotionSuggestion.ScenarioOptions);
    }

    [Fact]
    public void Quantidade_null_e_zero()
    {
        var none = DetailFrom(WithQuantity(Eval(AvailableExcess()), null, InventoryCommercialAttentionQuantitySource.None));
        Assert.Equal(InventoryProjectionPresentation.EmDash, none.PromotionSuggestion.AttentionQuantityText);
        Assert.False(InventoryPromotionSuggestionDetailUi.ShowQuantity(none.PromotionSuggestion));

        var zero = DetailFrom(WithQuantity(
            Eval(AvailableExcess()), 0, InventoryCommercialAttentionQuantitySource.ProjectedExcess30));
        Assert.Equal("0", zero.PromotionSuggestion.AttentionQuantityText);
        Assert.True(InventoryPromotionSuggestionDetailUi.ShowQuantity(zero.PromotionSuggestion));
    }

    [Fact]
    public void Possibilidades_nao_duplicam_cards_B4()
    {
        var two = DetailFrom(Eval(AvailableExcess(Light(), Moderate())));
        var lines = InventoryPromotionSuggestionDetailUi.PossibilityLines(two.PromotionSuggestion);
        Assert.Equal(2, lines.Count);
        Assert.Equal("• Cenário leve — ver cenário comercial acima", lines[0]);
        Assert.Equal("• Cenário moderado — ver cenário comercial acima", lines[1]);
        Assert.DoesNotContain("R$", string.Join(' ', lines), StringComparison.Ordinal);
        Assert.DoesNotContain("9,40", string.Join(' ', lines), StringComparison.Ordinal);
        Assert.DoesNotContain("recomendado", string.Join(' ', lines), StringComparison.OrdinalIgnoreCase);

        var one = DetailFrom(Eval(AvailableExcess(Light())));
        Assert.Single(InventoryPromotionSuggestionDetailUi.PossibilityLines(one.PromotionSuggestion));
        Assert.DoesNotContain(
            InventoryPromotionSuggestionDetailUi.PossibilityLines(one.PromotionSuggestion),
            l => l.Contains("moderado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Secao_visual_depois_dos_cenarios_B4()
    {
        var xaml = ReadWindowXaml();
        var commercial = xaml.IndexOf("x:Name=\"CommercialSection\"", StringComparison.Ordinal);
        var action = xaml.IndexOf("x:Name=\"CommercialActionSection\"", StringComparison.Ordinal);
        var heading = xaml.IndexOf("InventoryPromotionSuggestionDetailUi.Heading", StringComparison.Ordinal);
        var footer = xaml.IndexOf("CommercialFooterText", StringComparison.Ordinal);
        var fechar = xaml.IndexOf("Fechar (Esc)", StringComparison.Ordinal);
        var scroll = xaml.IndexOf("<ScrollViewer", StringComparison.Ordinal);
        var scrollEnd = xaml.IndexOf("</ScrollViewer>", StringComparison.Ordinal);
        Assert.True(commercial > 0 && footer > commercial && action > footer && heading > action);
        Assert.True(scroll > 0 && action > scroll && fechar > scrollEnd);
        Assert.Contains("AÇÃO COMERCIAL", InventoryPromotionSuggestionDetailUi.Heading, StringComparison.Ordinal);
        Assert.DoesNotContain("PROMOÇÃO RECOMENDADA", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PREÇO IDEAL", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SimulatedPriceText", xaml[action..], StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"820\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"480\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"960\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"680\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_somente_presentation_sem_botao_aplicar()
    {
        var cs = ReadWindowCs();
        var xaml = ReadWindowXaml();
        Assert.DoesNotContain("using SGDB.Services", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPromotionSuggestionEngine", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPromotionSuggestionComposer", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("switch", MethodBody(cs, "private void BindPromotion"), StringComparison.Ordinal);
        Assert.Contains("suggestion.StatusLabel", cs, StringComparison.Ordinal);
        Assert.Contains("suggestion.ActionLabel", cs, StringComparison.Ordinal);
        Assert.Contains("PossibilityLines", cs, StringComparison.Ordinal);
        Assert.Contains("Content=\"Fechar (Esc)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key.Escape", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("Aplicar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Ativar promoção", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Alterar preço", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Enviar ao PDV", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Criar promoção", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Usar preço", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Click=\"Apply", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("você precisa vender", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preço recomendado", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("melhor preço", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Navegacao_preservada()
    {
        var cs = ReadViewCs();
        var xaml = ReadViewXaml();
        var window = ReadWindowXaml();
        Assert.Contains("Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenProduct();", cs, StringComparison.Ordinal);
        Assert.Contains("Content=\"Detalhar projeção\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenProjectionDetail_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenLots_Click", cs, StringComparison.Ordinal);
        Assert.Contains("Content=\"Ver produto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Lotes e validades\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Fechar (Esc)\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenProjectionDetail", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_write_pdv_sql_schema_combo_meta_compra()
    {
        foreach (var path in new[]
                 {
                     ReadViewCs(),
                     ReadWindowCs(),
                     ReadWindowXaml(),
                     ReadDetailCs(),
                     ReadSource("src", "SGDB.App", "Models", "InventoryPromotionSuggestionDetailUi.cs"),
                 })
        {
            Assert.DoesNotContain("sale_price", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preco_promocional", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_inicio", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_fim", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("desconto_percent", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE ", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT ", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CREATE TABLE", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PdvService", path, StringComparison.Ordinal);
            Assert.DoesNotContain("PdvCartHelper", path, StringComparison.Ordinal);
            Assert.DoesNotContain("StoreNetworkHost", path, StringComparison.Ordinal);
            Assert.DoesNotContain("meta mensal", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Pedido sugerido", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("produto complementar", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Comprar", path, StringComparison.Ordinal);
            Assert.DoesNotContain("Repor", path, StringComparison.Ordinal);
        }

        var host = ReadSource("src", "SGDB.App", "Services", "StoreNetworkHost.cs");
        Assert.DoesNotContain("InventoryPromotionSuggestion", host, StringComparison.Ordinal);
        Assert.DoesNotContain("70F-B5", host, StringComparison.Ordinal);

        var csproj = ReadSource("src", "SGDB.App", "SGDB.App.csproj");
        Assert.Contains("<Version>0.3.19</Version>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Cliente_bloqueado_reseta_B5()
    {
        var blocked = MethodBody(ReadViewCs(), "private void ShowClientBlocked()");
        Assert.Contains("new InventoryPromotionSuggestionSnapshot()", blocked, StringComparison.Ordinal);
        Assert.Contains("new InventoryPromotionSuggestionPresentationSnapshot()", blocked, StringComparison.Ordinal);
        Assert.DoesNotContain("PromotionSuggestionComposer", blocked, StringComparison.Ordinal);
    }

    [Fact]
    public void Linguagem_proibida_ausente_no_detalhe()
    {
        var presented = InventoryPromotionSuggestionPresentation.FromResult(Eval(AvailableExcess()));
        var blob = string.Join(' ',
            presented.StatusLabel,
            presented.ActionLabel,
            presented.ObjectiveLabel,
            presented.Explanation,
            presented.DisclaimerText,
            string.Join(' ', InventoryPromotionSuggestionDetailUi.PossibilityLines(presented)));
        Assert.DoesNotContain("promoção recomendada", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preço recomendado", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("você precisa vender", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("garantido", blob, StringComparison.OrdinalIgnoreCase);
    }

    static InventoryProjectionDetail DetailFrom(InventoryPromotionSuggestionResult result)
    {
        var id = result.ProductId != 0 ? result.ProductId : 1;
        var presented = InventoryPromotionSuggestionPresentation.FromResult(
            result.ProductId == id ? result : WithProductId(result, id));
        var snap = Snapshot70C(id);
        var projection = InventoryProjectionPresentation.Apply(snap);
        var promotionSnap = new InventoryPromotionSuggestionPresentationSnapshot
        {
            Rows = [presented],
            ByProductId = new Dictionary<int, InventoryPromotionSuggestionPresentationRow>
            {
                [id] = presented,
            },
        };
        return InventoryProjectionDetail.TryCreate(snap, projection, id, promotion: promotionSnap)!;
    }

    static InventoryPromotionSuggestionResult WithProductId(
        InventoryPromotionSuggestionResult source,
        int productId) =>
        new()
        {
            ProductId = productId,
            Status = source.Status,
            Action = source.Action,
            Thesis = source.Thesis,
            Objective = source.Objective,
            Confidence = source.Confidence,
            AttentionPriority = source.AttentionPriority,
            PrimaryReason = source.PrimaryReason,
            SecondaryReasons = source.SecondaryReasons,
            Warnings = source.Warnings,
            AttentionQuantity = source.AttentionQuantity,
            AttentionQuantitySource = source.AttentionQuantitySource,
            Scenarios = source.Scenarios,
        };

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

    static InventoryPromotionSuggestionResult Eval(InventoryCommercialScenarioResult scenario) =>
        InventoryPromotionSuggestionEngine.Evaluate(scenario);

    static InventoryCommercialScenarioResult AvailableExcess(params InventoryCommercialScenario[] scenarios) =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.Available,
            PrimaryReason = InventoryCommercialScenarioReason.ProjectedExcess30,
            Thesis = InventoryCommercialScenarioThesis.ProjectedExcess30,
            Confidence = InventoryAttentionConfidence.Reliable,
            CurrentCatalogPrice = 10,
            CurrentGrossMarginPercent = 40,
            MinimumAllowedCatalogPrice = 8.20,
            MinimumGrossMarginPercent = 20,
            FinancialRoomAmount = 1.80,
            AttentionQuantity = 8,
            AttentionQuantitySource = InventoryCommercialAttentionQuantitySource.ProjectedExcess30,
            Scenarios = scenarios.Length == 0 ? [Light(), Moderate()] : scenarios,
        };

    static InventoryCommercialScenarioResult AvailableExpiry() =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.Available,
            PrimaryReason = InventoryCommercialScenarioReason.ExpirySurplus,
            Thesis = InventoryCommercialScenarioThesis.ExpirySurplus,
            Confidence = InventoryAttentionConfidence.Reliable,
            AttentionQuantity = 3.5,
            AttentionQuantitySource = InventoryCommercialAttentionQuantitySource.ExpirySurplus,
            CurrentCatalogPrice = 10,
            MinimumGrossMarginPercent = 20,
            MinimumAllowedCatalogPrice = 8.20,
            Scenarios = [Light(), Moderate()],
        };

    static InventoryCommercialScenarioResult Monitor(
        InventoryCommercialScenarioReason primary,
        InventoryCommercialScenarioThesis thesis = InventoryCommercialScenarioThesis.None,
        IReadOnlyList<InventoryCommercialScenario>? scenarios = null) =>
        new()
        {
            ProductId = 2,
            Status = InventoryCommercialScenarioStatus.MonitorOnly,
            PrimaryReason = primary,
            Thesis = thesis,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = scenarios ?? [],
        };

    static InventoryCommercialScenarioResult Clone(
        InventoryCommercialScenarioResult source,
        InventoryAttentionConfidence? confidence = null,
        double? minMargin = null)
    {
        var secondary = new List<InventoryCommercialScenarioReason>(source.SecondaryReasons ?? []);
        return new InventoryCommercialScenarioResult
        {
            ProductId = source.ProductId,
            Status = source.Status,
            PrimaryReason = source.PrimaryReason,
            SecondaryReasons = secondary,
            Thesis = source.Thesis,
            Confidence = confidence ?? source.Confidence,
            CurrentCatalogPrice = source.CurrentCatalogPrice,
            CurrentGrossMarginPercent = source.CurrentGrossMarginPercent,
            MinimumAllowedCatalogPrice = source.MinimumAllowedCatalogPrice,
            MinimumGrossMarginPercent = minMargin ?? source.MinimumGrossMarginPercent,
            FinancialRoomAmount = source.FinancialRoomAmount,
            AttentionQuantity = source.AttentionQuantity,
            AttentionQuantitySource = source.AttentionQuantitySource,
            Scenarios = source.Scenarios,
        };
    }

    static InventoryPromotionSuggestionResult WithPriority(
        InventoryPromotionSuggestionResult source,
        InventoryAttentionPriority priority) =>
        new()
        {
            ProductId = source.ProductId,
            Status = source.Status,
            Action = source.Action,
            Thesis = source.Thesis,
            Objective = source.Objective,
            Confidence = source.Confidence,
            AttentionPriority = priority,
            PrimaryReason = source.PrimaryReason,
            SecondaryReasons = source.SecondaryReasons,
            Warnings = source.Warnings,
            AttentionQuantity = source.AttentionQuantity,
            AttentionQuantitySource = source.AttentionQuantitySource,
            Scenarios = source.Scenarios,
        };

    static InventoryPromotionSuggestionResult WithQuantity(
        InventoryPromotionSuggestionResult source,
        double? quantity,
        InventoryCommercialAttentionQuantitySource qtySource) =>
        new()
        {
            ProductId = source.ProductId,
            Status = source.Status,
            Action = source.Action,
            Thesis = source.Thesis,
            Objective = source.Objective,
            Confidence = source.Confidence,
            AttentionPriority = source.AttentionPriority,
            PrimaryReason = source.PrimaryReason,
            SecondaryReasons = source.SecondaryReasons,
            Warnings = source.Warnings,
            AttentionQuantity = quantity,
            AttentionQuantitySource = qtySource,
            Scenarios = source.Scenarios,
        };

    static InventoryCommercialScenario Light() =>
        new()
        {
            Kind = InventoryCommercialScenarioKind.Light,
            SimulatedCatalogPrice = 9.40,
            ReductionAmount = 0.60,
            ReductionPercent = 6,
            GrossMarginPercent = 36.17,
        };

    static InventoryCommercialScenario Moderate() =>
        new()
        {
            Kind = InventoryCommercialScenarioKind.Moderate,
            SimulatedCatalogPrice = 8.80,
            ReductionAmount = 1.20,
            ReductionPercent = 12,
            GrossMarginPercent = 31.82,
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
