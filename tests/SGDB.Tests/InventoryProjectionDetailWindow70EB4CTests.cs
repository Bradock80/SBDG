using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70E-B4C — seção ATENÇÃO na janela existente. Sem instanciar WPF, sem EXE, sem banco.
/// </summary>
public class InventoryProjectionDetailWindow70EB4CTests
{
    [Fact]
    public void Attention_section_is_after_product_and_before_numbers()
    {
        var xaml = ReadWindowXaml();
        var name = xaml.IndexOf("x:Name=\"ProductNameText\"", StringComparison.Ordinal);
        var code = xaml.IndexOf("x:Name=\"ProductCodeText\"", StringComparison.Ordinal);
        var attention = xaml.IndexOf("x:Name=\"AttentionSection\"", StringComparison.Ordinal);
        var heading = xaml.IndexOf("Text=\"ATENÇÃO\"", StringComparison.Ordinal);
        var stock = xaml.IndexOf("Text=\"Estoque e giro\"", StringComparison.Ordinal);
        Assert.True(name >= 0 && code > name && attention > code && heading > attention && stock > heading);

        var section = xaml[attention..stock];
        Assert.Contains("AttentionPriorityText", section, StringComparison.Ordinal);
        Assert.Contains("AttentionReasonText", section, StringComparison.Ordinal);
        Assert.Contains("AttentionActionText", section, StringComparison.Ordinal);
        Assert.Contains("AttentionConfidenceText", section, StringComparison.Ordinal);
        Assert.Contains("AttentionExplanationText", section, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", section, StringComparison.Ordinal);
        Assert.Contains("Outras atenções", section, StringComparison.Ordinal);
        Assert.Contains("AttentionSecondaryPanel", section, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Surplus30Text", section, StringComparison.Ordinal);
        Assert.DoesNotContain("VmvText", section, StringComparison.Ordinal);
        Assert.DoesNotContain("StockText", section, StringComparison.Ordinal);
        Assert.DoesNotContain("R$", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Promoção", section, StringComparison.Ordinal);
        Assert.DoesNotContain("desconto", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("combo", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Window_binds_presentation_strings_without_reinterpreting_enums()
    {
        var cs = ReadWindowCs();
        var bind = MethodBody(cs, "private void Bind(InventoryProjectionDetail detail)");
        Assert.Contains("attention.PriorityDisplay", bind, StringComparison.Ordinal);
        Assert.Contains("attention.PrimaryReasonDisplay", bind, StringComparison.Ordinal);
        Assert.Contains("attention.ActionDisplay", bind, StringComparison.Ordinal);
        Assert.Contains("attention.ConfidenceDisplay", bind, StringComparison.Ordinal);
        Assert.Contains("attention.Explanation", bind, StringComparison.Ordinal);
        Assert.Contains("attention.SecondaryReasonDisplays", bind, StringComparison.Ordinal);
        Assert.Contains("InventoryAttentionPresentation.MissingRow", bind, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionEngine", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionComposer", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("PriorityLabel", bind, StringComparison.Ordinal);
        Assert.DoesNotContain("ReasonLabel", bind, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionLabel", bind, StringComparison.Ordinal);
        Assert.DoesNotContain("switch", bind, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionPriority.", bind, StringComparison.Ordinal);
        Assert.DoesNotContain("using SGDB.Services", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_detail_uses_loaded_attention_without_query()
    {
        var cs = ReadViewCs();
        var open = MethodBody(cs, "private void OpenProjectionDetail_Click");
        Assert.Contains("_attentionPresented", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionComposer", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionEngine", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", open, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionPresentation.Apply(", open, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", open, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new InventoryProjectionDetailWindow(detail)", open, StringComparison.Ordinal);

        var apply = MethodBody(cs, "private void ApplyView()");
        Assert.DoesNotContain("InventoryAttentionComposer", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", apply, StringComparison.Ordinal);

        Assert.Equal(1, CountOccurrences(cs, "InventoryProjectionService.Load("));
        Assert.Equal(1, CountOccurrences(cs, "InventoryAttentionComposer.Build("));
        Assert.Equal(6, InventoryIntelligenceService.ExpectedQueryCount);
        Assert.Equal(7, InventoryProjectionService.ExpectedQueryCount);
    }

    [Fact]
    public void Footer_is_short_attention_without_explanation()
    {
        var detail = MethodBody(ReadViewCs(), "private void UpdateDetail()");
        Assert.Contains("row.PriorityDisplay", detail, StringComparison.Ordinal);
        Assert.Contains("row.PrimaryReasonDisplay", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Explanation", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("SecondaryReason", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionDisplay", detail, StringComparison.Ordinal);
        Assert.Contains("giro.SituationDisplay", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_keeps_70d_and_70e_together()
    {
        var cs = ReadViewCs();
        var keep = cs.IndexOf("failure.Value.KeepPreviousSnapshot", StringComparison.Ordinal);
        var nextElse = cs.IndexOf("else", keep, StringComparison.Ordinal);
        var keepBlock = cs[keep..nextElse];
        Assert.DoesNotContain("_attentionPresented =", keepBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_presented =", keepBlock, StringComparison.Ordinal);

        var load = MethodBody(cs, "private void Load()");
        var compose = load.IndexOf("InventoryAttentionComposer.Build(snapshot)", StringComparison.Ordinal);
        var assignSnap = load.IndexOf("_snapshot = snapshot;", StringComparison.Ordinal);
        var assignAttention = load.IndexOf("_attentionPresented = attentionPresented;", StringComparison.Ordinal);
        Assert.True(compose > 0 && assignSnap > compose && assignAttention > assignSnap);
    }

    [Fact]
    public void Client_still_blocks_before_load_and_detail()
    {
        var cs = ReadViewCs();
        var clientIdx = cs.IndexOf("StoreNetworkMode.IsClient", StringComparison.Ordinal);
        var loadIdx = cs.IndexOf("InventoryProjectionService.Load()", StringComparison.Ordinal);
        Assert.InRange(clientIdx, 0, loadIdx - 1);
        var open = MethodBody(cs, "private void OpenProjectionDetail_Click");
        Assert.Contains("if (_clientBlocked)", open, StringComparison.Ordinal);
        var blockedIdx = open.IndexOf("if (_clientBlocked)", StringComparison.Ordinal);
        var tryIdx = open.IndexOf("InventoryProjectionDetail.TryCreate", StringComparison.Ordinal);
        Assert.True(blockedIdx >= 0 && tryIdx > blockedIdx);

        var mode = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "StoreNetworkMode.cs"));
        var host = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "StoreNetworkHost.cs"));
        Assert.DoesNotContain("InventoryAttention", mode, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttention", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Cards_and_coverage_filter_remain_70c()
    {
        Assert.Equal(7, InventoryIntelligencePresentation.Cards.Length);
        Assert.Contains(
            InventoryIntelligencePresentation.CoverageOptions,
            o => o.Title == "Atenção");
        var filterType = typeof(InventoryIntelligenceUiFilter);
        Assert.Null(filterType.GetProperty("Priority"));
        Assert.Null(filterType.GetProperty("Family"));
        var viewXaml = ReadViewXaml();
        Assert.DoesNotContain("Header=\"Atenção\"", viewXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Motivo\"", viewXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_keeps_close_esc_and_no_commercial_actions()
    {
        var xaml = ReadWindowXaml();
        Assert.Contains("Fechar (Esc)", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Promoção", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Desconto", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Combo", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Comprar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Nova Window", xaml, StringComparison.OrdinalIgnoreCase);
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

    private static string ReadWindowXaml() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml");

    private static string ReadWindowCs() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml.cs");

    private static string ReadViewCs() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml.cs");

    private static string ReadViewXaml() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml");

    private static string FindSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return "";
    }

    private static string ReadSource(params string[] relative) => File.ReadAllText(FindSource(relative));
}
