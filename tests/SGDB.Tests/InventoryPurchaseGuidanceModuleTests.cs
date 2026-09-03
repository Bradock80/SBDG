using System.IO;
using System.Reflection;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 70G-B4 — módulo Reposição Inteligente. Filtros/contagens puros + inspeção de wiring.
/// Sem instanciar UserControl WPF, sem DB da loja.
/// </summary>
public class InventoryPurchaseGuidanceModuleTests
{
    [Fact]
    public void QueryCount_ui_e_zero() =>
        Assert.Equal(0, InventoryPurchaseGuidanceUi.ExpectedQueryCount);

    [Fact]
    public void Pipeline_total_permanece_9()
    {
        Assert.Equal(9, InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount);
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount
            + InventoryPurchaseGuidanceComposer.ExpectedQueryCount
            + InventoryPurchaseGuidancePresentation.ExpectedQueryCount
            + InventoryPurchaseGuidanceUi.ExpectedQueryCount);
    }

    [Fact]
    public void ModuleId_registrado() =>
        Assert.Equal("reposicao_inteligente", InventoryPurchaseGuidanceUi.ModuleId);

    [Fact]
    public void Count_Consider()
    {
        var counts = InventoryPurchaseGuidanceUi.CountCards(
        [
            Row(InventoryPurchaseGuidanceAction.ConsiderReplenishment),
            Row(InventoryPurchaseGuidanceAction.ConsiderReplenishment, productId: 2),
            Row(InventoryPurchaseGuidanceAction.Monitor, productId: 3),
        ]);
        Assert.Equal(2, counts.ConsiderReplenishment);
        Assert.Equal(3, counts.All);
    }

    [Fact]
    public void Count_DoNot()
    {
        var counts = InventoryPurchaseGuidanceUi.CountCards(
        [
            Row(InventoryPurchaseGuidanceAction.DoNotReplenishNow),
            Row(InventoryPurchaseGuidanceAction.Monitor, productId: 2),
        ]);
        Assert.Equal(1, counts.DoNotReplenishNow);
    }

    [Fact]
    public void Count_Monitor()
    {
        var counts = InventoryPurchaseGuidanceUi.CountCards(
        [
            Row(InventoryPurchaseGuidanceAction.Monitor),
            Row(InventoryPurchaseGuidanceAction.Monitor, productId: 2),
            Row(InventoryPurchaseGuidanceAction.ReviewData, productId: 3),
        ]);
        Assert.Equal(2, counts.Monitor);
    }

    [Fact]
    public void Count_Review()
    {
        var counts = InventoryPurchaseGuidanceUi.CountCards(
        [
            Row(InventoryPurchaseGuidanceAction.ReviewData),
        ]);
        Assert.Equal(1, counts.ReviewData);
    }

    [Fact]
    public void Count_NA_nao_entra_nos_cards()
    {
        var counts = InventoryPurchaseGuidanceUi.CountCards(
        [
            Row(InventoryPurchaseGuidanceAction.None, InventoryPurchaseGuidanceStatus.NotApplicable),
            Row(InventoryPurchaseGuidanceAction.ConsiderReplenishment, productId: 2),
        ]);
        Assert.Equal(1, counts.NotApplicable);
        Assert.Equal(1, counts.All);
        Assert.Equal(1, counts.ConsiderReplenishment);
    }

    [Fact]
    public void Count_populacao_vazia()
    {
        var counts = InventoryPurchaseGuidanceUi.CountCards([]);
        Assert.Equal(0, counts.All);
        Assert.Equal(0, counts.ConsiderReplenishment);
        Assert.Equal(0, counts.NotApplicable);
    }

    [Fact]
    public void Count_multiplos()
    {
        var counts = InventoryPurchaseGuidanceUi.CountCards(
        [
            Row(InventoryPurchaseGuidanceAction.ConsiderReplenishment),
            Row(InventoryPurchaseGuidanceAction.DoNotReplenishNow, productId: 2),
            Row(InventoryPurchaseGuidanceAction.Monitor, productId: 3),
            Row(InventoryPurchaseGuidanceAction.ReviewData, productId: 4),
            Row(InventoryPurchaseGuidanceAction.None, InventoryPurchaseGuidanceStatus.NotApplicable, 5),
        ]);
        Assert.Equal(4, counts.All);
        Assert.Equal(1, counts.ConsiderReplenishment);
        Assert.Equal(1, counts.DoNotReplenishNow);
        Assert.Equal(1, counts.Monitor);
        Assert.Equal(1, counts.ReviewData);
        Assert.Equal(1, counts.NotApplicable);
    }

    [Fact]
    public void Filtro_card_Consider()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            Card = InventoryPurchaseGuidanceCardKind.ConsiderReplenishment,
        });
        Assert.All(rows, r => Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, r.Action));
        Assert.Single(rows);
    }

    [Fact]
    public void Filtro_Todos_operacionais_exclui_NA()
    {
        var rows = Apply(InventoryPurchaseGuidanceUiFilter.Cleared());
        Assert.Equal(5, rows.Count);
        Assert.DoesNotContain(rows, r => r.Action == InventoryPurchaseGuidanceAction.None);
        Assert.DoesNotContain(rows, r => r.Status == InventoryPurchaseGuidanceStatus.NotApplicable);
    }

    [Fact]
    public void Filtro_DoNot()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            Card = InventoryPurchaseGuidanceCardKind.DoNotReplenishNow,
        });
        Assert.Single(rows);
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, rows[0].Action);
    }

    [Fact]
    public void Filtro_Monitor()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            Card = InventoryPurchaseGuidanceCardKind.Monitor,
        });
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, r.Action));
    }

    [Fact]
    public void Filtro_Review()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            Card = InventoryPurchaseGuidanceCardKind.ReviewData,
        });
        Assert.Single(rows);
        Assert.Equal(InventoryPurchaseGuidanceAction.ReviewData, rows[0].Action);
    }

    [Fact]
    public void Busca_nome()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter { Search = "alfa" });
        Assert.Single(rows);
        Assert.Equal("Alfa", rows[0].Name);
    }

    [Fact]
    public void Busca_codigo()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter { Search = "c-2" });
        Assert.Single(rows);
        Assert.Equal(2, rows[0].ProductId);
    }

    [Fact]
    public void Busca_case_insensitive()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter { Search = "ALFA" });
        Assert.Single(rows);
    }

    [Fact]
    public void Busca_vazia_nao_filtra()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter { Search = "   " });
        Assert.Equal(5, rows.Count);
    }

    [Fact]
    public void Cobertura_Critical()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            CoverageBand = InventoryCoverageBand.Critical,
        });
        Assert.Single(rows);
        Assert.Equal(InventoryCoverageBand.Critical, rows[0].CoverageBand);
    }

    [Fact]
    public void Cobertura_Low()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            CoverageBand = InventoryCoverageBand.Low,
        });
        Assert.Single(rows);
        Assert.Equal(InventoryCoverageBand.Low, rows[0].CoverageBand);
    }

    [Fact]
    public void Cobertura_Attention()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            CoverageBand = InventoryCoverageBand.Attention,
        });
        Assert.Single(rows);
    }

    [Fact]
    public void Cobertura_Normal()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            CoverageBand = InventoryCoverageBand.Normal,
        });
        Assert.Single(rows);
        Assert.Equal(2, rows[0].ProductId);
    }

    [Fact]
    public void Cobertura_NotCalculable()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            CoverageBand = InventoryCoverageBand.NotCalculable,
        });
        Assert.Single(rows);
        Assert.Equal(4, rows[0].ProductId);
    }

    [Fact]
    public void Combinacao_busca_e_Action()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            Card = InventoryPurchaseGuidanceCardKind.ConsiderReplenishment,
            Search = "naoexiste",
        });
        Assert.Empty(rows);
    }

    [Fact]
    public void Combinacao_busca_e_coverage()
    {
        var rows = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            CoverageBand = InventoryCoverageBand.Critical,
            Search = "alfa",
        });
        Assert.Single(rows);
        Assert.Equal("Alfa", rows[0].Name);
    }

    [Fact]
    public void Selecao_detail_usa_B3()
    {
        var rows = Apply(InventoryPurchaseGuidanceUiFilter.Cleared());
        var consider = rows.First(r => r.Action == InventoryPurchaseGuidanceAction.ConsiderReplenishment);
        Assert.Equal("Considerar reposição", consider.ActionLabel);
        Assert.False(string.IsNullOrWhiteSpace(consider.ShortExplanation));
        Assert.Contains("considerar a reposição", consider.DetailExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            InventoryPurchaseGuidancePresentation.ConsiderLimitationNote,
            consider.DetailExplanation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_selecao_hint() =>
        Assert.Equal("Selecione uma linha para ver o detalhe.", InventoryPurchaseGuidanceUi.SelectRowHint);

    [Fact]
    public void ReviewData_visivel_no_Todos()
    {
        var rows = Apply(InventoryPurchaseGuidanceUiFilter.Cleared());
        Assert.Contains(rows, r => r.Action == InventoryPurchaseGuidanceAction.ReviewData);
    }

    [Fact]
    public void NA_ausente_do_padrao_operacional()
    {
        var rows = Apply(InventoryPurchaseGuidanceUiFilter.Cleared());
        Assert.DoesNotContain(rows, r => r.Status == InventoryPurchaseGuidanceStatus.NotApplicable);
    }

    [Fact]
    public void Empty_state()
    {
        Assert.Equal(
            InventoryPurchaseGuidanceUi.EmptySnapshotMessage,
            InventoryPurchaseGuidanceUi.EmptyStateMessage(0, 0, null));
        Assert.Equal(
            InventoryPurchaseGuidanceUi.EmptyFilterMessage,
            InventoryPurchaseGuidanceUi.EmptyStateMessage(4, 0, null));
        Assert.Equal("", InventoryPurchaseGuidanceUi.EmptyStateMessage(4, 2, null));
    }

    [Fact]
    public void RelatoriosAcesso_permite()
    {
        TestDataHelper.SetSessionRole("gestor");
        Assert.True(AccessControl.CanAccessModule(InventoryPurchaseGuidanceUi.ModuleId));
        Assert.True(AccessControl.Can("RelatoriosAcesso"));
    }

    [Fact]
    public void Admin_permite()
    {
        TestDataHelper.SetSessionRole("admin");
        Assert.True(AccessControl.CanAccessModule(InventoryPurchaseGuidanceUi.ModuleId));
    }

    [Fact]
    public void Vendedor_nao_abre()
    {
        TestDataHelper.SetSessionRole("vendedor");
        Assert.False(AccessControl.CanAccessModule(InventoryPurchaseGuidanceUi.ModuleId));
    }

    [Fact]
    public void EstoqueAjustar_sozinho_nao_concede()
    {
        TestDataHelper.SetSessionCustomPermissions("vendedor", p =>
        {
            p.EstoqueAjustar = true;
            p.RelatoriosAcesso = false;
        });
        Assert.False(AccessControl.CanAccessModule(InventoryPurchaseGuidanceUi.ModuleId));
        Assert.True(AccessControl.Can("EstoqueAjustar"));
    }

    [Fact]
    public void Cliente_RedeLoja_bloqueado_na_regra()
    {
        var mode = ReadSource("src", "SGDB.App", "Services", "StoreNetworkMode.cs");
        var main = ReadSource("src", "SGDB.App", "MainWindow.xaml.cs");
        Assert.Contains("or \"reposicao_inteligente\"", mode, StringComparison.Ordinal);
        var blockIdx = main.IndexOf("StoreNetworkMode.IsModuleBlockedOnClient(moduleId)", StringComparison.Ordinal);
        var viewIdx = main.IndexOf("new InventoryPurchaseGuidanceModuleView()", StringComparison.Ordinal);
        Assert.InRange(blockIdx, 0, viewIdx - 1);
    }

    [Fact]
    public void Menu_e_toolbar_chamam_mesmo_modulo()
    {
        var xaml = ReadSource("src", "SGDB.App", "MainWindow.xaml");
        Assert.Contains("Tag=\"reposicao_inteligente\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Reposição Inteligente\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Label=\"Reposição\"", xaml, StringComparison.Ordinal);
        var menuIdx = xaml.IndexOf("Header=\"Estoque Inteligente\"", StringComparison.Ordinal);
        var repoIdx = xaml.IndexOf("Header=\"Reposição Inteligente\"", StringComparison.Ordinal);
        var comprasMenu = xaml.IndexOf("Header=\"Compra\" Tag=\"compras\"", StringComparison.Ordinal);
        Assert.True(menuIdx >= 0 && repoIdx > menuIdx);
        var toolbarRepo = xaml.IndexOf("x:Name=\"BtnReposicao\"", StringComparison.Ordinal);
        var toolbarCompras = xaml.IndexOf("x:Name=\"BtnCompras\"", StringComparison.Ordinal);
        Assert.True(toolbarRepo >= 0 && toolbarCompras > toolbarRepo);
        Assert.True(comprasMenu > repoIdx);
        Assert.Contains("IconKey=\"relatorio\" Label=\"Reposição\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void View_carrega_pipeline_uma_vez_e_compoe_70G()
    {
        var cs = ReadViewCs();
        Assert.Contains("InventoryProjectionService.Load()", cs, StringComparison.Ordinal);
        Assert.Contains("InventoryPurchaseGuidanceComposer.Compose(snapshot)", cs, StringComparison.Ordinal);
        Assert.Contains("InventoryPurchaseGuidancePresentation.Apply", cs, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(cs, "InventoryProjectionService.Load("));
        Assert.DoesNotContain("InventoryIntelligenceService.Load", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchaseService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("SuggestedQuantity", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void View_bloqueia_cliente_antes_do_load()
    {
        var cs = ReadViewCs();
        var clientIdx = cs.IndexOf("StoreNetworkMode.IsClient", StringComparison.Ordinal);
        var loadIdx = cs.IndexOf("InventoryProjectionService.Load()", StringComparison.Ordinal);
        Assert.InRange(clientIdx, 0, loadIdx - 1);
        Assert.Contains("ShowClientBlocked();", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void UI_nao_decide_Action()
    {
        var cs = ReadViewCs();
        var ui = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidanceUi.cs");
        Assert.DoesNotContain("InventoryPurchaseGuidanceEngine", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPurchaseGuidanceEngine", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("IsIdle", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("IsIdle", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectedExcess", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectedExcess", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("if (row.CoverageBand", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("if (row.CoverageBand", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("Action = InventoryPurchaseGuidanceAction.ConsiderReplenishment", ui, StringComparison.Ordinal);
        Assert.Contains("InventoryPurchaseGuidanceUi.Apply", cs, StringComparison.Ordinal);
        Assert.Contains("row.Action == InventoryPurchaseGuidanceAction.ConsiderReplenishment", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyView_nao_reconsulta_nem_reclassifica()
    {
        var apply = MethodBody(ReadViewCs(), "private void ApplyView()");
        Assert.Contains("InventoryPurchaseGuidanceUi.Apply", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPurchaseGuidanceComposer", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPurchaseGuidanceEngine", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPurchaseGuidancePresentation.Apply", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialFactsService", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_filtro_de_grupo()
    {
        var xaml = ReadViewXaml();
        var ui = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidanceUi.cs");
        Assert.DoesNotContain("Grupo", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupId", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void DoNot_nao_parece_nunca()
    {
        var row = Apply(new InventoryPurchaseGuidanceUiFilter
        {
            Card = InventoryPurchaseGuidanceCardKind.DoNotReplenishNow,
        })[0];
        Assert.Equal("Não repor agora", row.ActionLabel);
        Assert.DoesNotContain("nunca", row.ActionLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nunca", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não se justifica agora", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sem_botao_Comprar()
    {
        var xaml = ReadViewXaml();
        Assert.DoesNotContain("Comprar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Gerar compra", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Criar pedido", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Fornecedor", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Orientação\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Produto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"*\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_dependencias_proibidas()
    {
        var cs = ReadViewCs();
        var ui = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidanceUi.cs");
        foreach (var text in new[] { cs, ui })
        {
            Assert.DoesNotContain("PurchaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SupplierService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SuggestedQuantity", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PurchaseScore", text, StringComparison.Ordinal);
            Assert.DoesNotContain("min_stock", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MinStock", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Disclaimer_e_titulos_B3()
    {
        var xaml = ReadViewXaml();
        Assert.Contains("InventoryPurchaseGuidancePresentation.ModuleTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("InventoryPurchaseGuidancePresentation.GuidanceDisclaimer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Compra Automática", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Teclado_F5_Esc_CtrlF()
    {
        var cs = ReadViewCs();
        Assert.Contains("Key.F5", cs, StringComparison.Ordinal);
        Assert.Contains("Key.Escape", cs, StringComparison.Ordinal);
        Assert.Contains("Key.F", cs, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Control", cs, StringComparison.Ordinal);
        Assert.Contains("SearchBox.Focus()", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Ver_analise_abre_detalhe_tecnico_existente()
    {
        var cs = ReadViewCs();
        Assert.Contains("InventoryProjectionDetail.TryCreate", cs, StringComparison.Ordinal);
        Assert.Contains("InventoryProjectionDetailWindow", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("new Purchase", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_abre_modulo()
    {
        var cs = ReadSource("src", "SGDB.App", "MainWindow.xaml.cs");
        Assert.Contains("InventoryPurchaseGuidanceModuleView", cs, StringComparison.Ordinal);
        Assert.Contains("InventoryPurchaseGuidanceUi.ModuleId", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowModule(\"compras\")", cs.Substring(
            cs.IndexOf("InventoryPurchaseGuidanceModuleView", StringComparison.Ordinal),
            400), StringComparison.Ordinal);
    }

    [Fact]
    public void Tone_nao_usa_verde_de_compra()
    {
        Assert.Equal("attention", InventoryPurchaseGuidanceGridRow.ToneOf(
            InventoryPurchaseGuidanceAction.ConsiderReplenishment));
        Assert.Equal("info", InventoryPurchaseGuidanceGridRow.ToneOf(
            InventoryPurchaseGuidanceAction.DoNotReplenishNow));
        Assert.DoesNotContain("green", InventoryPurchaseGuidanceGridRow.ToneOf(
            InventoryPurchaseGuidanceAction.ConsiderReplenishment), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Snapshot_nao_tem_quantidade_fornecedor_score()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidanceGridRow),
            "SuggestedQuantity", "SupplierId", "PurchaseScore", "MinStock");
        AssertNoMember(typeof(InventoryPurchaseGuidanceUi),
            "SuggestedQuantity", "PurchaseService");
    }

    static IReadOnlyList<InventoryPurchaseGuidanceGridRow> Apply(InventoryPurchaseGuidanceUiFilter filter)
    {
        var presented = new InventoryPurchaseGuidancePresentationSnapshot
        {
            Rows =
            [
                Present(1, InventoryPurchaseGuidanceAction.ConsiderReplenishment,
                    InventoryPurchaseGuidanceReason.CriticalCoverage),
                Present(2, InventoryPurchaseGuidanceAction.DoNotReplenishNow,
                    InventoryPurchaseGuidanceReason.ProjectedExcess30),
                Present(3, InventoryPurchaseGuidanceAction.Monitor,
                    InventoryPurchaseGuidanceReason.None),
                Present(4, InventoryPurchaseGuidanceAction.ReviewData,
                    InventoryPurchaseGuidanceReason.NoPhysicalEvidence),
                Present(5, InventoryPurchaseGuidanceAction.None,
                    InventoryPurchaseGuidanceReason.CompositionProduct,
                    InventoryPurchaseGuidanceStatus.NotApplicable),
                Present(6, InventoryPurchaseGuidanceAction.Monitor,
                    InventoryPurchaseGuidanceReason.None),
            ],
        };
        var turnover = new[]
        {
            Turnover(1, "Alfa", "C-1", InventoryCoverageBand.Critical),
            Turnover(2, "Beta", "C-2", InventoryCoverageBand.Normal),
            Turnover(3, "Gama", "C-3", InventoryCoverageBand.Low),
            Turnover(4, "Delta", "C-4", InventoryCoverageBand.NotCalculable),
            Turnover(5, "Kit", "C-5", InventoryCoverageBand.Normal),
            Turnover(6, "Epsilon", "C-6", InventoryCoverageBand.Attention),
        };
        return InventoryPurchaseGuidanceUi.Apply(presented, turnover, filter);
    }

    static InventoryPurchaseGuidancePresentationRow Row(
        InventoryPurchaseGuidanceAction action,
        InventoryPurchaseGuidanceStatus status = InventoryPurchaseGuidanceStatus.GuidanceAvailable,
        int productId = 1) =>
        Present(productId, action, InventoryPurchaseGuidanceReason.None, status);

    static InventoryPurchaseGuidancePresentationRow Present(
        int productId,
        InventoryPurchaseGuidanceAction action,
        InventoryPurchaseGuidanceReason reason,
        InventoryPurchaseGuidanceStatus? status = null)
    {
        var resolved = status ?? action switch
        {
            InventoryPurchaseGuidanceAction.ConsiderReplenishment
                or InventoryPurchaseGuidanceAction.DoNotReplenishNow =>
                InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.Monitor => InventoryPurchaseGuidanceStatus.Monitor,
            InventoryPurchaseGuidanceAction.ReviewData => InventoryPurchaseGuidanceStatus.ReviewData,
            _ => InventoryPurchaseGuidanceStatus.NotApplicable,
        };
        return InventoryPurchaseGuidancePresentation.FromResult(new InventoryPurchaseGuidanceResult
        {
            ProductId = productId,
            Action = action,
            Status = resolved,
            PrimaryReason = reason,
            Confidence = action == InventoryPurchaseGuidanceAction.ConsiderReplenishment
                ? InventoryAttentionConfidence.Limited
                : InventoryAttentionConfidence.Reliable,
        });
    }

    static ProductTurnoverRow Turnover(
        int id, string name, string code, InventoryCoverageBand band) =>
        new()
        {
            ProductId = id,
            Name = name,
            Code = code,
            CoverageBand = band,
            TotalStock = 10,
            Vmv30 = 1,
            CoverageDays = band == InventoryCoverageBand.NotCalculable ? null : 10,
        };

    static void AssertNoMember(Type type, params string[] names)
    {
        var members = type
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in names)
            Assert.False(members.Contains(name), $"{type.Name} não deve expor {name}");
    }

    static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var idx = text.IndexOf(token, start, StringComparison.Ordinal);
            if (idx < 0)
                return count;
            count++;
            start = idx + token.Length;
        }
    }

    static string ReadViewCs() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryPurchaseGuidanceModuleView.xaml.cs");

    static string ReadViewXaml() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryPurchaseGuidanceModuleView.xaml");

    static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"método não encontrado: {signature}");
        var brace = source.IndexOf('{', start);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[brace..(i + 1)];
            }
        }

        return source[brace..];
    }

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
