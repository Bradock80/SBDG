using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 71A-B7 — módulo visual Combos Inteligentes. Filtros/contagens puros + inspeção de wiring.
/// Sem instanciar UserControl WPF, sem DB da loja.
/// </summary>
public class InventoryComboIntelligenceModuleTests
{
    static readonly string[] Forbidden =
    [
        "promoção ativa",
        "promocao ativa",
        "melhor combo",
        "vai vender",
        "venda garantida",
        "lucro esperado",
        "ganho garantido",
        "desconto aplicado",
        "combos ativos",
        "produtos em promoção",
        "vendas previstas",
    ];

    [Fact]
    public void QueryCount_ui_e_loader_sao_zero()
    {
        Assert.Equal(0, InventoryComboIntelligenceUi.ExpectedQueryCount);
        Assert.Equal(0, InventoryComboIntelligenceLoader.ExpectedQueryCount);
        Assert.Equal(0, InventoryComboPresentation.ExpectedQueryCount);
        Assert.Equal(10, InventoryComboIntelligenceLoader.ExpectedPipelineQueryCount);
    }

    [Fact]
    public void ModuleId_registrado() =>
        Assert.Equal("combos_inteligentes", InventoryComboIntelligenceUi.ModuleId);

    [Fact]
    public void Cards_cinco_targets_sete_sugestoes()
    {
        var presented = SampleSnapshot();
        var counts = InventoryComboIntelligenceUi.CountCards(presented.Targets);
        Assert.Equal(5, counts.NeedTurnover);
        Assert.Equal(3, counts.WithSuggestions);
        Assert.Equal(2, counts.WithoutSafeCombination);
        Assert.Equal(7, counts.Combinations);
        Assert.Equal(5, presented.Targets.Count);
    }

    [Fact]
    public void Filtro_nao_altera_snapshot()
    {
        var presented = SampleSnapshot();
        var before = presented.Targets.Count;
        var rows = InventoryComboIntelligenceUi.Apply(presented, new InventoryComboUiFilter
        {
            Status = InventoryComboUiStatusFilter.WithSuggestions,
        });
        Assert.Equal(3, rows.Count);
        Assert.Equal(before, presented.Targets.Count);
        Assert.Equal(5, InventoryComboIntelligenceUi.CountCards(presented.Targets).NeedTurnover);
    }

    [Fact]
    public void Filtro_todos()
    {
        var rows = InventoryComboIntelligenceUi.Apply(SampleSnapshot(), InventoryComboUiFilter.Cleared());
        Assert.Equal(5, rows.Count);
    }

    [Fact]
    public void Filtro_com_sugestoes()
    {
        var rows = InventoryComboIntelligenceUi.Apply(SampleSnapshot(), new InventoryComboUiFilter
        {
            Status = InventoryComboUiStatusFilter.WithSuggestions,
        });
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.True(r.SuggestionCount > 0));
    }

    [Fact]
    public void Filtro_sem_combinacao_segura()
    {
        var rows = InventoryComboIntelligenceUi.Apply(SampleSnapshot(), new InventoryComboUiFilter
        {
            Status = InventoryComboUiStatusFilter.WithoutSafeCombination,
        });
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(0, r.SuggestionCount));
        Assert.Contains(rows, r => r.ProductId == 4);
        Assert.Contains(rows, r => r.ProductId == 5);
    }

    [Theory]
    [InlineData(InventoryComboUiReasonFilter.ExpirySurplus, ComboTargetEligibilityReason.ExpirySurplus)]
    [InlineData(InventoryComboUiReasonFilter.ProjectedExcess, ComboTargetEligibilityReason.ProjectedExcess)]
    [InlineData(InventoryComboUiReasonFilter.Idle, ComboTargetEligibilityReason.Idle)]
    public void Filtro_motivo(InventoryComboUiReasonFilter filter, ComboTargetEligibilityReason reason)
    {
        var rows = InventoryComboIntelligenceUi.Apply(SampleSnapshot(), new InventoryComboUiFilter
        {
            Reason = filter,
        });
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(reason, r.Reason));
    }

    [Fact]
    public void Busca_por_nome_e_codigo_em_memoria()
    {
        var byName = InventoryComboIntelligenceUi.Apply(SampleSnapshot(), new InventoryComboUiFilter
        {
            Search = "Alvo Quatro",
        });
        Assert.Equal(4, Assert.Single(byName).ProductId);

        var byCode = InventoryComboIntelligenceUi.Apply(SampleSnapshot(), new InventoryComboUiFilter
        {
            Search = "T2",
        });
        Assert.Equal(2, Assert.Single(byCode).ProductId);
    }

    [Fact]
    public void Busca_por_ancora_opcional()
    {
        var rows = InventoryComboIntelligenceUi.Apply(SampleSnapshot(), new InventoryComboUiFilter
        {
            Search = "A8",
        });
        Assert.Equal(1, Assert.Single(rows).ProductId);
    }

    [Fact]
    public void Target_zero_sugestao_aparece()
    {
        var rows = InventoryComboIntelligenceUi.Apply(SampleSnapshot(), InventoryComboUiFilter.Cleared());
        var empty = Assert.Single(rows, r => r.ProductId == 4);
        Assert.Equal("0", empty.CombinationsText);
        Assert.Equal(InventoryComboPresentation.EmptyTargetMessage, empty.EmptyMessage);
        Assert.Equal(InventoryComboPresentation.EmptyTargetMessage, empty.CombinationsStatusText);
        Assert.Empty(empty.Suggestions);
    }

    [Fact]
    public void Ranking_B4_preservado()
    {
        var presented = SampleSnapshot();
        var rows = InventoryComboIntelligenceUi.Apply(presented, InventoryComboUiFilter.Cleared());
        var target = Assert.Single(rows, r => r.ProductId == 1);
        Assert.Equal(new[] { 8, 3, 20 }, target.Suggestions.Select(s => s.AnchorProductId).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => r.ProductId).ToArray());
    }

    [Fact]
    public void Empty_snapshot()
    {
        Assert.Equal(
            InventoryComboPresentation.EmptySnapshotMessage,
            InventoryComboIntelligenceUi.EmptyStateMessage(0, 0, null));
        Assert.Equal(
            InventoryComboIntelligenceUi.EmptyFilterMessage,
            InventoryComboIntelligenceUi.EmptyStateMessage(4, 0, null));
        Assert.Equal("", InventoryComboIntelligenceUi.EmptyStateMessage(4, 2, null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Layout_1_2_3_sugestoes_sem_fantasma(int n)
    {
        var suggestions = Enumerable.Range(0, n)
            .Select(i => Suggestion(anchorId: 30 + i))
            .ToArray();
        var presented = InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot
        {
            Targets = [Group(9, ComboTargetEligibilityReason.Idle, "T9", "Alvo N", suggestions)],
        });
        var row = Assert.Single(InventoryComboIntelligenceUi.Apply(presented, InventoryComboUiFilter.Cleared()));
        Assert.Equal(n, row.Suggestions.Count);
        Assert.Equal(n, row.SuggestionCount);
        Assert.DoesNotContain(row.Suggestions, s => s is null);
    }

    [Fact]
    public void Current_only_nao_tem_referencia()
    {
        var presented = InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot
        {
            Targets = [Group(1, ComboTargetEligibilityReason.Idle, "T1", "Alvo", Suggestion(referencePrice: null))],
        });
        var suggestion = Assert.Single(Assert.Single(presented.Targets).Suggestions);
        Assert.False(suggestion.HasReferenceScenario);
        Assert.Equal(InventoryComboPresentation.EmDash, suggestion.ReferencePriceText);
        Assert.Single(suggestion.Scenarios);
    }

    [Fact]
    public void Current_e_referencia_distintos()
    {
        var presented = InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot
        {
            Targets =
            [
                Group(1, ComboTargetEligibilityReason.Idle, "T1", "Alvo",
                    Suggestion(price: 30, referencePrice: 27.5, reduction: 2.5, referenceProfit: 11.5, referenceMargin: 0.4)),
            ],
        });
        var suggestion = Assert.Single(Assert.Single(presented.Targets).Suggestions);
        Assert.True(suggestion.HasReferenceScenario);
        Assert.Equal("R$ 30,00", suggestion.CurrentPriceText);
        Assert.Equal("R$ 27,50", suggestion.ReferencePriceText);
        Assert.Equal("R$ 2,50", suggestion.ReductionText);
        Assert.Equal(InventoryComboPresentation.ReferenceSubtitle, suggestion.ReferenceSubtitle);
        Assert.DoesNotContain("promoção", suggestion.ReferenceSubtitle, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, suggestion.Scenarios.Count);
    }

    [Fact]
    public void Evidence_e_confidence_vem_de_B6()
    {
        var presented = SampleSnapshot();
        var target = presented.Targets[0];
        Assert.Equal(InventoryComboPresentation.EvidenceObserved, target.Suggestions[0].EvidenceText);
        Assert.Equal("weak", target.Suggestions[1].EvidenceTone);
        Assert.Equal("insufficient", target.Suggestions[2].EvidenceTone);
        Assert.Equal(InventoryAttentionPresentation.ConfidenceReliable, target.ConfidenceText);
        Assert.DoesNotContain("% confiança", target.Suggestions[0].ConfidenceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RelatoriosAcesso_permite()
    {
        TestDataHelper.SetSessionRole("gestor");
        Assert.True(AccessControl.CanAccessModule(InventoryComboIntelligenceUi.ModuleId));
        Assert.True(AccessControl.Can("RelatoriosAcesso"));
    }

    [Fact]
    public void Admin_permite()
    {
        TestDataHelper.SetSessionRole("admin");
        Assert.True(AccessControl.CanAccessModule(InventoryComboIntelligenceUi.ModuleId));
    }

    [Fact]
    public void Vendedor_nao_abre()
    {
        TestDataHelper.SetSessionRole("vendedor");
        Assert.False(AccessControl.CanAccessModule(InventoryComboIntelligenceUi.ModuleId));
    }

    [Fact]
    public void EstoqueAjustar_sozinho_nao_concede()
    {
        TestDataHelper.SetSessionCustomPermissions("vendedor", p =>
        {
            p.EstoqueAjustar = true;
            p.RelatoriosAcesso = false;
        });
        Assert.False(AccessControl.CanAccessModule(InventoryComboIntelligenceUi.ModuleId));
    }

    [Fact]
    public void Cliente_RedeLoja_bloqueado_na_regra()
    {
        var mode = ReadSource("src", "SGDB.App", "Services", "StoreNetworkMode.cs");
        var main = ReadSource("src", "SGDB.App", "MainWindow.xaml.cs");
        Assert.Contains("or \"combos_inteligentes\"", mode, StringComparison.Ordinal);
        var blockIdx = main.IndexOf("StoreNetworkMode.IsModuleBlockedOnClient(moduleId)", StringComparison.Ordinal);
        var viewIdx = main.IndexOf("new InventoryComboIntelligenceModuleView()", StringComparison.Ordinal);
        Assert.InRange(blockIdx, 0, viewIdx - 1);
    }

    [Fact]
    public void Menu_e_toolbar_chamam_mesmo_modulo()
    {
        var xaml = ReadSource("src", "SGDB.App", "MainWindow.xaml");
        Assert.Contains("Tag=\"combos_inteligentes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Combos Inteligentes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Label=\"Combos\"", xaml, StringComparison.Ordinal);
        var estoqueIdx = xaml.IndexOf("Header=\"Estoque Inteligente\"", StringComparison.Ordinal);
        var repoIdx = xaml.IndexOf("Header=\"Reposição Inteligente\"", StringComparison.Ordinal);
        var comboIdx = xaml.IndexOf("Header=\"Combos Inteligentes\"", StringComparison.Ordinal);
        Assert.True(estoqueIdx >= 0 && repoIdx > estoqueIdx && comboIdx > repoIdx);
        var toolbarRepo = xaml.IndexOf("x:Name=\"BtnReposicao\"", StringComparison.Ordinal);
        var toolbarCombo = xaml.IndexOf("x:Name=\"BtnCombos\"", StringComparison.Ordinal);
        var toolbarCompras = xaml.IndexOf("x:Name=\"BtnCompras\"", StringComparison.Ordinal);
        Assert.True(toolbarRepo >= 0 && toolbarCombo > toolbarRepo && toolbarCompras > toolbarCombo);
    }

    [Fact]
    public void View_carrega_B5_uma_vez_e_aplica_B6()
    {
        var cs = ReadViewCs();
        Assert.Contains("InventoryComboIntelligenceLoader.Load()", cs, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(cs, "InventoryComboIntelligenceLoader.Load("));
        Assert.DoesNotContain("InventoryComboTargetEligibilityEngine", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryComboSuggestionEngine", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryComboCoOccurrenceService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProductService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductFormWindow", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchaseService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductCompositionService", cs, StringComparison.Ordinal);
        var ctor = MethodBody(cs, "public InventoryComboIntelligenceModuleView()");
        Assert.DoesNotContain("InventoryComboIntelligenceLoader", ctor);
        Assert.Contains("Loaded +=", ctor);
        var selection = MethodBody(cs, "private void Grid_SelectionChanged");
        Assert.DoesNotContain("Loader.Load", selection);
        Assert.Contains("if (_loading)", MethodBody(cs, "private void Load()"));
    }

    [Fact]
    public void View_bloqueia_cliente_antes_do_load()
    {
        var cs = ReadViewCs();
        var clientIdx = cs.IndexOf("StoreNetworkMode.IsClient", StringComparison.Ordinal);
        var loadIdx = cs.IndexOf("InventoryComboIntelligenceLoader.Load()", StringComparison.Ordinal);
        Assert.InRange(clientIdx, 0, loadIdx - 1);
        Assert.Contains("ShowClientBlocked();", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void View_nao_muta_comercial()
    {
        var cs = ReadViewCs();
        var xaml = ReadViewXaml();
        Assert.DoesNotContain("Ativar promoção", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Criar combo", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Alterar preço", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vender agora", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT ", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CanUserSortColumns=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HasReferenceScenario", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("#FEE2E2", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"640\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Linguagem_proibida_ausente()
    {
        var texts = new List<string>
        {
            InventoryComboIntelligenceUi.ModuleTitle,
            InventoryComboIntelligenceUi.Subtitle,
            InventoryComboIntelligenceUi.EmptyFilterMessage,
            InventoryComboPresentation.DisclaimerText,
            InventoryComboPresentation.EmptyTargetMessage,
            InventoryComboPresentation.EmptySnapshotMessage,
        };
        texts.AddRange(InventoryComboIntelligenceUi.Cards.Select(c => c.Title));
        texts.AddRange(InventoryComboIntelligenceUi.StatusOptions.Select(c => c.Title));
        texts.Add(ReadViewXaml());
        foreach (var text in texts)
        {
            foreach (var banned in Forbidden)
                Assert.DoesNotContain(banned, text ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AccessControl_mapeia_RelatoriosAcesso()
    {
        var source = ReadSource("src", "SGDB.App", "Services", "AccessControl.cs");
        Assert.Contains("or \"combos_inteligentes\"", source, StringComparison.Ordinal);
        var start = source.IndexOf("or \"combos_inteligentes\"", StringComparison.Ordinal);
        var slice = source[start..(start + 80)];
        Assert.Contains("RelatoriosAcesso", source[start..(start + 120)], StringComparison.Ordinal);
        _ = slice;
    }

    static InventoryComboPresentationSnapshot SampleSnapshot()
    {
        return InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot
        {
            QueryCount = 10,
            ProductTitles = Titles(
                (1, "T1", "Alvo Um"),
                (2, "T2", "Alvo Dois"),
                (3, "T3", "Alvo Tres"),
                (4, "T4", "Alvo Quatro"),
                (5, "T5", "Alvo Cinco"),
                (8, "A8", "Oito"),
                (3, "T3", "Alvo Tres"),
                (20, "A20", "Vinte"),
                (30, "A30", "Trinta"),
                (31, "A31", "Trinta e um"),
                (32, "A32", "Trinta e dois")),
            Targets =
            [
                Group(1, ComboTargetEligibilityReason.ExpirySurplus, "T1", "Alvo Um",
                    Suggestion(anchorId: 8, evidence: InventoryComboPairEvidence.Observed),
                    Suggestion(anchorId: 3, evidence: InventoryComboPairEvidence.Weak),
                    Suggestion(anchorId: 20, evidence: InventoryComboPairEvidence.InsufficientHistory)),
                Group(2, ComboTargetEligibilityReason.ProjectedExcess, "T2", "Alvo Dois",
                    Suggestion(targetId: 2, anchorId: 30),
                    Suggestion(targetId: 2, anchorId: 31)),
                Group(3, ComboTargetEligibilityReason.Idle, "T3", "Alvo Tres",
                    Suggestion(targetId: 3, anchorId: 30),
                    Suggestion(targetId: 3, anchorId: 32)),
                Group(4, ComboTargetEligibilityReason.ExpirySurplus, "T4", "Alvo Quatro"),
                Group(5, ComboTargetEligibilityReason.Idle, "T5", "Alvo Cinco"),
            ],
        });
    }

    static InventoryComboTargetSuggestionGroup Group(
        int id,
        ComboTargetEligibilityReason reason,
        string code,
        string name,
        params InventoryComboSuggestion[] suggestions) =>
        new()
        {
            ProductId = id,
            Code = code,
            Name = name,
            Eligibility = new InventoryComboTargetEligibility
            {
                ProductId = id,
                Status = ComboEligibilityStatus.Eligible,
                Reason = reason,
                Confidence = InventoryAttentionConfidence.Reliable,
            },
            Suggestions = suggestions,
        };

    static InventoryComboSuggestion Suggestion(
        int targetId = 1,
        int anchorId = 8,
        InventoryComboPairEvidence evidence = InventoryComboPairEvidence.Observed,
        double price = 30,
        double? referencePrice = null,
        double? reduction = null,
        double? referenceProfit = null,
        double? referenceMargin = null)
    {
        var scenarios = new List<InventoryComboPairFinancialScenario>
        {
            new()
            {
                Kind = InventoryComboPairFinancialScenarioKind.CurrentPrices,
                PairPrice = price,
                GrossProfit = 14,
                GrossMargin = 14d / 30d,
                ReductionFromCurrent = 0,
            },
        };
        if (referencePrice is double refPrice)
        {
            scenarios.Add(new InventoryComboPairFinancialScenario
            {
                Kind = InventoryComboPairFinancialScenarioKind.TargetReductionReference,
                PairPrice = refPrice,
                GrossProfit = referenceProfit ?? 11.5,
                GrossMargin = referenceMargin ?? 0.4,
                ReductionFromCurrent = reduction ?? 0,
            });
        }

        return new InventoryComboSuggestion
        {
            TargetProductId = targetId,
            AnchorProductId = anchorId,
            TargetReason = ComboTargetEligibilityReason.ExpirySurplus,
            AnchorReason = ComboAnchorEligibilityReason.HealthyNormalCoverage,
            PairEvidence = evidence,
            NormalPairPrice = price,
            PairFloorPrice = 20,
            Scenarios = scenarios,
            TargetStock = 80,
            AnchorStock = 40,
            AnchorCoverageDays = 22.4,
            PairTransactions = evidence == InventoryComboPairEvidence.InsufficientHistory ? 1 : 4,
            TargetTransactions = 10,
            ConfidenceTargetToAnchor = evidence == InventoryComboPairEvidence.InsufficientHistory ? null : 0.4,
            Confidence = InventoryAttentionConfidence.Reliable,
            Limitations = [],
        };
    }

    static Dictionary<int, InventoryComboProductTitle> Titles(
        params (int Id, string Code, string Name)[] items)
    {
        var map = new Dictionary<int, InventoryComboProductTitle>();
        foreach (var item in items)
        {
            map[item.Id] = new InventoryComboProductTitle
            {
                ProductId = item.Id,
                Code = item.Code,
                Name = item.Name,
            };
        }

        return map;
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
        ReadSource("src", "SGDB.App", "Views", "InventoryComboIntelligenceModuleView.xaml.cs");

    static string ReadViewXaml() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryComboIntelligenceModuleView.xaml");

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
