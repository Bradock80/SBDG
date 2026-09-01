using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70E-B4B — contrato da tela: 2 colunas, 7 queries, sem cards/filtro 70E, sem detalhe ATENÇÃO.
/// Sem instanciar UserControl WPF, sem EXE, sem banco da loja.
/// </summary>
public class InventoryIntelligenceModuleView70EB4BTests
{
    [Fact]
    public void Load_composes_70e_in_memory_after_successful_projection_load()
    {
        var cs = ReadViewCs();
        var loadIdx = cs.IndexOf("InventoryProjectionService.Load()", StringComparison.Ordinal);
        var presentedIdx = cs.IndexOf("InventoryProjectionPresentation.Apply(snapshot)", StringComparison.Ordinal);
        var composeIdx = cs.IndexOf("InventoryAttentionComposer.Build(snapshot)", StringComparison.Ordinal);
        var attentionIdx = cs.IndexOf(
            "InventoryAttentionPresentation.Apply(attention, presented)",
            StringComparison.Ordinal);
        var assignSnap = cs.IndexOf("_snapshot = snapshot;", StringComparison.Ordinal);
        var assignAttention = cs.IndexOf("_attention = attention;", StringComparison.Ordinal);
        var assignAttentionPresented = cs.IndexOf(
            "_attentionPresented = attentionPresented;",
            StringComparison.Ordinal);

        Assert.True(loadIdx >= 0);
        Assert.True(presentedIdx > loadIdx);
        Assert.True(composeIdx > presentedIdx);
        Assert.True(attentionIdx > composeIdx);
        Assert.True(assignSnap > attentionIdx);
        Assert.True(assignAttention > assignSnap);
        Assert.True(assignAttentionPresented > assignAttention);
        Assert.Equal(1, CountOccurrences(cs, "InventoryProjectionService.Load("));
        Assert.Equal(1, CountOccurrences(cs, "InventoryAttentionComposer.Build("));
        Assert.DoesNotContain("InventoryIntelligenceService.Load", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("GetByProductId", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyView_does_not_recompute_engine_or_query()
    {
        var apply = MethodBody(ReadViewCs(), "private void ApplyView()");
        Assert.Contains("InventoryIntelligenceProjectionPresentation.Apply(", apply, StringComparison.Ordinal);
        Assert.Contains("_attentionPresented", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionComposer", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionEngine", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionPresentation.Apply(", apply, StringComparison.Ordinal);
        Assert.Contains(
            "InventoryIntelligencePresentation.CountCards(_snapshot.Intelligence.Rows)",
            apply,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_failure_keeps_previous_attention_with_projection()
    {
        var cs = ReadViewCs();
        var keep = cs.IndexOf("failure.Value.KeepPreviousSnapshot", StringComparison.Ordinal);
        var nextElse = cs.IndexOf("else", keep, StringComparison.Ordinal);
        Assert.True(keep >= 0 && nextElse > keep);
        var keepBlock = cs[keep..nextElse];
        Assert.DoesNotContain("_snapshot =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_presented =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_attention =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_attentionPresented =", keepBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_is_blocked_before_projection_and_attention()
    {
        var cs = ReadViewCs();
        var clientIdx = cs.IndexOf("StoreNetworkMode.IsClient", StringComparison.Ordinal);
        var loadIdx = cs.IndexOf("InventoryProjectionService.Load()", StringComparison.Ordinal);
        var composeIdx = cs.IndexOf("InventoryAttentionComposer.Build(snapshot)", StringComparison.Ordinal);
        Assert.InRange(clientIdx, 0, loadIdx - 1);
        Assert.True(composeIdx > loadIdx);

        var blocked = MethodBody(cs, "private void ShowClientBlocked()");
        Assert.Contains("new InventoryAttentionSnapshot()", blocked, StringComparison.Ordinal);
        Assert.Contains("new InventoryAttentionPresentationSnapshot()", blocked, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", blocked, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionComposer", blocked, StringComparison.Ordinal);

        var mode = ReadSource("src", "SGDB.App", "Services", "StoreNetworkMode.cs");
        Assert.Contains("estoque_inteligente", mode, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttention", mode, StringComparison.Ordinal);
        var host = ReadSource("src", "SGDB.App", "Services", "StoreNetworkHost.cs");
        Assert.DoesNotContain("InventoryAttention", host, StringComparison.Ordinal);
        Assert.DoesNotContain("70E", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_adds_priority_and_reason_after_code_before_numbers()
    {
        var xaml = ReadViewXaml();
        var produto = xaml.IndexOf("Header=\"Produto\"", StringComparison.Ordinal);
        var codigo = xaml.IndexOf("Header=\"Código\"", StringComparison.Ordinal);
        var prioridade = xaml.IndexOf("Header=\"Prioridade\"", StringComparison.Ordinal);
        var motivo = xaml.IndexOf("Header=\"Motivo\"", StringComparison.Ordinal);
        var deposito = xaml.IndexOf("Header=\"Depósito\"", StringComparison.Ordinal);
        Assert.True(produto >= 0 && codigo > produto && prioridade > codigo && motivo > prioridade && deposito > motivo);

        var priority = ColumnBlock(xaml, "Prioridade");
        Assert.Contains("Binding=\"{Binding PriorityDisplay}\"", priority, StringComparison.Ordinal);
        Assert.Contains("SortMemberPath=\"PrioritySortKey\"", priority, StringComparison.Ordinal);
        Assert.Contains("Width=\"96\"", priority, StringComparison.Ordinal);
        Assert.DoesNotContain("SortMemberPath=\"PriorityDisplay\"", priority, StringComparison.Ordinal);

        var reason = ColumnBlock(xaml, "Motivo");
        Assert.Contains("Binding=\"{Binding PrimaryReasonDisplay}\"", reason, StringComparison.Ordinal);
        Assert.Contains("Width=\"160\"", reason, StringComparison.Ordinal);
        Assert.Contains("Value=\"CharacterEllipsis\"", reason, StringComparison.Ordinal);
        Assert.Contains("ToolTip\" Value=\"{Binding PrimaryReasonDisplay}\"", reason, StringComparison.Ordinal);

        Assert.DoesNotContain("Header=\"Atenção\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Sobra 30d\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Validade / risco\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Situação\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Alerta\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionDisplay", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfidenceDisplay", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Explanation", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FamilyDisplay", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SecondaryReason", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectedExpirySurplus", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SurplusValueQuality", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_and_cards_are_unchanged_in_this_step()
    {
        var cs = ReadViewCs();
        var xaml = ReadViewXaml();
        var detail = MethodBody(cs, "private void UpdateDetail()");
        Assert.Contains("giro.SituationDisplay", detail, StringComparison.Ordinal);
        Assert.Contains("giro.AlertDisplay", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("PriorityDisplay", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("PrimaryReasonDisplay", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionDisplay", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ATENÇÃO", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ATENÇÃO", xaml, StringComparison.Ordinal);

        Assert.Contains("InventoryProjectionDetail.TryCreate(_snapshot, _presented, row.ProductId)", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("TryCreate(_snapshot, _presented, _attention", cs, StringComparison.Ordinal);

        Assert.Contains("InventoryIntelligencePresentation.Cards", cs, StringComparison.Ordinal);
        Assert.Equal(7, InventoryIntelligencePresentation.Cards.Length);
        Assert.DoesNotContain("InventoryAttentionCard", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("AttentionFilter", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionFamily", cs, StringComparison.Ordinal);

        var filterType = typeof(InventoryIntelligenceUiFilter);
        Assert.Null(filterType.GetProperty("Priority"));
        Assert.Null(filterType.GetProperty("Family"));
        Assert.Null(filterType.GetProperty("Attention"));
    }

    [Fact]
    public void Query_count_remains_seven_and_composer_adds_zero()
    {
        Assert.Equal(6, InventoryIntelligenceService.ExpectedQueryCount);
        Assert.Equal(1, InventoryProjectionService.ExpectedLotsQueryCount);
        Assert.Equal(7, InventoryProjectionService.ExpectedQueryCount);

        var built = InventoryAttentionComposer.Build(new InventoryProjectionSnapshot
        {
            QueryCount = InventoryProjectionService.ExpectedQueryCount,
        });
        Assert.Equal(7, built.QueryCount);

        var presented = InventoryAttentionPresentation.Apply(built);
        Assert.Equal(7, presented.QueryCount);

        var view = ReadViewCs();
        Assert.DoesNotContain("SELECT ", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqliteCommand", view, StringComparison.Ordinal);
        var apply = MethodBody(view, "private void ApplyView()");
        Assert.DoesNotContain("Load(", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_actions_stay_on_projection_grid_row()
    {
        var cs = ReadViewCs();
        Assert.Contains("is not InventoryIntelligenceProjectionGridRow row", cs, StringComparison.Ordinal);
        Assert.Contains("OpenProjectionDetail_Click", cs, StringComparison.Ordinal);
        Assert.Contains("Grid_MouseDoubleClick", cs, StringComparison.Ordinal);
        Assert.Contains("OpenLots_Click", cs, StringComparison.Ordinal);
        Assert.Contains("OpenProduct()", cs, StringComparison.Ordinal);
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

    private static string MethodBody(string source, string signature)
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
