using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>70I-B2 — tradução de ExpirySaleException (sem pixel/MessageBox).</summary>
[Collection(TempDatabaseCollection.Name)]
public class ExpirySaleUiTests
{
    [Fact]
    public void Titulo_VendaDeck_Especifico()
    {
        var sale = ExpirySaleUi.Format(Ex(), ExpirySaleUi.Operation.Sale, _ => "Coca");
        var deck = ExpirySaleUi.Format(Ex(), ExpirySaleUi.Operation.Deck, _ => "Coca");
        Assert.Equal("Venda não realizada", sale.Title);
        Assert.Equal("Venda não realizada", deck.Title);
        Assert.Contains("comanda permanece aberta", deck.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Titulo_Troca_E_Exchange()
    {
        var swap = ExpirySaleUi.Format(Ex(), ExpirySaleUi.Operation.Swap, _ => "Pepsi");
        var exchange = ExpirySaleUi.Format(Ex(), ExpirySaleUi.Operation.Exchange, _ => "Pepsi");
        Assert.Equal("Troca não realizada", swap.Title);
        Assert.Equal("Troca / Devolução não realizada", exchange.Title);
        Assert.DoesNotContain("Venda bloqueada", swap.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Venda bloqueada", exchange.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("item anterior permanece", swap.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Titulo_Transferencia_NaoEVenda()
    {
        var ui = ExpirySaleUi.Format(Ex(), ExpirySaleUi.Operation.Transfer, _ => "Água");
        Assert.Equal("Transferência não realizada", ui.Title);
        Assert.Contains("transferência", ui.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nenhuma transferência foi realizada", ui.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Venda não realizada", ui.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveNome_EFallback()
    {
        var named = ExpirySaleUi.Format(Ex(9), ExpirySaleUi.Operation.Sale, _ => "Coca-Cola 2L");
        Assert.Equal("Coca-Cola 2L", named.ProductName);
        Assert.Contains("Produto: Coca-Cola 2L", named.Body);

        var missing = ExpirySaleUi.Format(Ex(42), ExpirySaleUi.Operation.Sale, _ => null);
        Assert.Equal("Produto #42", missing.ProductName);
        Assert.Contains("Produto #42", missing.Body);

        var blank = ExpirySaleUi.Format(Ex(3), ExpirySaleUi.Operation.Sale, _ => "  ");
        Assert.Equal("Produto #3", blank.ProductName);
    }

    [Fact]
    public void ResolveNome_ViaProductService()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        var id = TestDataHelper.SeedSimpleProduct(10, 5, 2, "C1", "Guaraná Garrafa");
        var ui = ExpirySaleUi.Format(Ex(id), ExpirySaleUi.Operation.Sale);
        Assert.Contains("Guaraná Garrafa", ui.Body);
        Assert.Equal("Guaraná Garrafa", ui.ProductName);
    }

    [Fact]
    public void Quantidades_RotuloDeposito()
    {
        var ui = ExpirySaleUi.Format(Ex(1, requested: 7, sellable: 5, expired: 5),
            ExpirySaleUi.Operation.Sale, _ => "X");
        Assert.Contains("Quantidade solicitada no depósito: 7", ui.Body);
        Assert.Contains("Disponível no depósito sem utilizar unidades vencidas: 5", ui.Body);
        Assert.Contains("Unidades vencidas identificadas: 5", ui.Body);
        Assert.Contains("no depósito", ui.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultiItem_IdentificaProdutoDoDecision()
    {
        var ui = ExpirySaleUi.Format(Ex(88), ExpirySaleUi.Operation.Sale, id =>
            id == 88 ? "Produto B vencível" : "Produto A");
        Assert.Contains("Produto B vencível", ui.Body);
        Assert.DoesNotContain("Produto A", ui.Body);
    }

    [Fact]
    public void NaoUsaLinguagemProibida()
    {
        foreach (var op in Enum.GetValues<ExpirySaleUi.Operation>())
        {
            var ui = ExpirySaleUi.Format(Ex(), op, _ => "Item", canMaintainLots: true);
            AssertForbidden(ui.Body);
            Assert.DoesNotContain("vender mesmo assim", ui.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("continuar mesmo assim", ui.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ignorar", ui.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("forçar", ui.Body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Orientacao_AdminGestor_Vs_Vendedor()
    {
        var admin = ExpirySaleUi.Format(Ex(), ExpirySaleUi.Operation.Sale, _ => "X", canMaintainLots: true);
        var vendor = ExpirySaleUi.Format(Ex(), ExpirySaleUi.Operation.Sale, _ => "X", canMaintainLots: false);
        Assert.Contains("Estoque → Controle de Validades", admin.Body);
        Assert.Contains("administrador ou gestor", vendor.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Controle de Validades", vendor.Body);
    }

    [Fact]
    public void NenhumaBaixa_Afirmada()
    {
        var sale = ExpirySaleUi.Format(Ex(), ExpirySaleUi.Operation.Sale, _ => "X");
        Assert.Contains("Nenhuma baixa desta operação foi realizada", sale.Body);
    }

    [Fact]
    public void FalhaPdv_ExpirySaleUsaMesmoCaminhoPosPagamentoQuePdvException()
    {
        var blocked = Ex();
        Assert.True(ExpirySaleUi.UsesPdvPostPaymentRecovery(blocked));
        Assert.True(ExpirySaleUi.UsesPdvPostPaymentRecovery(new PdvException("falha pdv")));
        Assert.True(ExpirySaleUi.UsesPdvPostPaymentRecovery(new CashOperationException("caixa")));
        Assert.True(ExpirySaleUi.UsesPdvPostPaymentRecovery(new OpenTabException("deck")));
        Assert.False(ExpirySaleUi.UsesPdvPostPaymentRecovery(new InvalidOperationException("outro")));
    }

    private static ExpirySaleException Ex(
        int productId = 7,
        double requested = 7,
        double sellable = 5,
        double expired = 5)
    {
        var decision = new ExpirySaleDecision
        {
            ProductId = productId,
            RequestedWarehouseQty = requested,
            SellableWarehouseQty = sellable,
            ExpiredQty = expired,
            IsBlocked = true,
            ErrorCode = ExpirySaleRules.InsufficientNonExpired,
        };
        return new ExpirySaleException(ExpirySaleRules.InsufficientNonExpired, "bruto do motor", decision);
    }

    private static void AssertForbidden(string body)
    {
        Assert.DoesNotContain("produto vencido", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("todo o estoque está vencido", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("todo estoque está vencido", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("estoque insuficiente", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("produto seguro", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("produto válido", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FEFO", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overtracked", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cobertura", body, StringComparison.OrdinalIgnoreCase);
    }
}
