using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 70C-B3B — mapeamento de permissão do módulo Estoque Inteligente.
/// Não abre WPF. Não toca o motor.
/// </summary>
public class InventoryIntelligenceAccessTests
{
    [Theory]
    [InlineData("admin")]
    [InlineData("gestor")]
    public void RelatoriosAcesso_allows_estoque_inteligente(string role)
    {
        TestDataHelper.SetSessionRole(role);
        Assert.True(AccessControl.CanAccessModule("estoque_inteligente"));
        Assert.True(AccessControl.CanAccessModule("reposicao_inteligente"));
        Assert.True(AccessControl.CanAccessModule("combos_inteligentes"));
        Assert.True(AccessControl.Can("RelatoriosAcesso"));
    }

    [Fact]
    public void Vendedor_cannot_open_estoque_inteligente()
    {
        TestDataHelper.SetSessionRole("vendedor");
        Assert.False(AccessControl.Can("RelatoriosAcesso"));
        Assert.False(AccessControl.CanAccessModule("estoque_inteligente"));
        Assert.False(AccessControl.CanAccessModule("reposicao_inteligente"));
        Assert.False(AccessControl.CanAccessModule("combos_inteligentes"));
    }

    [Fact]
    public void RelatoriosAcesso_does_not_grant_ProdutosEditar()
    {
        TestDataHelper.SetSessionCustomPermissions("vendedor", p =>
        {
            p.RelatoriosAcesso = true;
            p.ProdutosEditar = false;
        });
        Assert.True(AccessControl.CanAccessModule("estoque_inteligente"));
        Assert.True(AccessControl.CanAccessModule("reposicao_inteligente"));
        Assert.True(AccessControl.CanAccessModule("combos_inteligentes"));
        Assert.True(AccessControl.Can("RelatoriosAcesso"));
        Assert.False(AccessControl.Can("ProdutosEditar"));
        Assert.False(AccessControl.Can("EstoqueAjustar"));
    }

    [Fact]
    public void EstoqueAjustar_sozinho_nao_abre_reposicao_inteligente()
    {
        TestDataHelper.SetSessionCustomPermissions("vendedor", p =>
        {
            p.EstoqueAjustar = true;
            p.RelatoriosAcesso = false;
        });
        Assert.True(AccessControl.Can("EstoqueAjustar"));
        Assert.False(AccessControl.CanAccessModule("reposicao_inteligente"));
        Assert.False(AccessControl.CanAccessModule("estoque_inteligente"));
        Assert.False(AccessControl.CanAccessModule("combos_inteligentes"));
    }
}
