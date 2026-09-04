using System.IO;
using SGDB.Views;

namespace SGDB.Tests;

/// <summary>
/// 71A-B8F — roteamento da roda do mouse no painel de sugestões. Sem WPF, sem banco.
/// </summary>
public class InventoryComboWheelScrollTests
{
    [Fact]
    public void Delta_positivo_sobe_conteudo()
    {
        Assert.True(InventoryComboWheelScroll.TryApplyVertical(200, 400, 120, out var next));
        Assert.Equal(80, next);
    }

    [Fact]
    public void Delta_negativo_desce_conteudo()
    {
        Assert.True(InventoryComboWheelScroll.TryApplyVertical(200, 400, -120, out var next));
        Assert.Equal(320, next);
    }

    [Fact]
    public void Nao_fica_abaixo_de_zero()
    {
        Assert.True(InventoryComboWheelScroll.TryApplyVertical(50, 400, 120, out var next));
        Assert.Equal(0, next);
        Assert.False(InventoryComboWheelScroll.TryApplyVertical(0, 400, 120, out var stuck));
        Assert.Equal(0, stuck);
    }

    [Fact]
    public void Nao_ultrapassa_ScrollableHeight()
    {
        Assert.True(InventoryComboWheelScroll.TryApplyVertical(350, 400, -120, out var next));
        Assert.Equal(400, next);
        Assert.False(InventoryComboWheelScroll.TryApplyVertical(400, 400, -120, out var stuck));
        Assert.Equal(400, stuck);
    }

    [Fact]
    public void Sem_espaco_ou_delta_zero_nao_trata()
    {
        Assert.False(InventoryComboWheelScroll.TryApplyVertical(0, 0, -120, out var none));
        Assert.Equal(0, none);
        Assert.False(InventoryComboWheelScroll.TryApplyVertical(10, 100, 0, out var same));
        Assert.Equal(10, same);
    }

    [Fact]
    public void Inner_com_espaco_nao_encaminha_ao_pai()
    {
        var route = InventoryComboWheelScroll.Route(10, 200, 0, 80, -120);
        Assert.True(route.Handled);
        Assert.True(route.MoveInner);
        Assert.False(route.MoveOuter);
        Assert.Equal(130, route.InnerOffset);
        Assert.Equal(0, route.OuterOffset);
    }

    [Fact]
    public void Inner_no_fim_encaminha_ao_pai()
    {
        var down = InventoryComboWheelScroll.Route(200, 200, 0, 80, -120);
        Assert.True(down.Handled);
        Assert.False(down.MoveInner);
        Assert.True(down.MoveOuter);
        Assert.Equal(200, down.InnerOffset);
        Assert.Equal(80, down.OuterOffset);

        var up = InventoryComboWheelScroll.Route(0, 200, 40, 80, 120);
        Assert.True(up.Handled);
        Assert.False(up.MoveInner);
        Assert.True(up.MoveOuter);
        Assert.Equal(0, up.InnerOffset);
        Assert.Equal(0, up.OuterOffset);
    }

    [Fact]
    public void Sem_movimento_nao_marca_Handled()
    {
        var route = InventoryComboWheelScroll.Route(0, 0, 0, 0, -120);
        Assert.False(route.Handled);
        Assert.False(route.MoveInner);
        Assert.False(route.MoveOuter);
    }

    [Fact]
    public void Preview_so_no_painel_direito()
    {
        var xaml = ReadSource("src", "SGDB.App", "Views", "InventoryComboIntelligenceModuleView.xaml");
        var cs = ReadSource("src", "SGDB.App", "Views", "InventoryComboIntelligenceModuleView.xaml.cs");
        Assert.Contains("x:Name=\"DetailScroll\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseWheel=\"DetailScroll_PreviewMouseWheel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewMouseWheel=\"ModuleScroll", xaml, StringComparison.Ordinal);
        var gridBlock = xaml[xaml.IndexOf("x:Name=\"Grid\"", StringComparison.Ordinal)..xaml.IndexOf("x:Name=\"EmptyOverlay\"", StringComparison.Ordinal)];
        Assert.DoesNotContain("PreviewMouseWheel", gridBlock, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Modifiers == ModifierKeys.Shift", cs, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", MethodBody(cs, "private void DetailScroll_PreviewMouseWheel"), StringComparison.Ordinal);
        Assert.Contains("if (!route.Handled)", MethodBody(cs, "private void DetailScroll_PreviewMouseWheel"), StringComparison.Ordinal);
    }

    static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
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
