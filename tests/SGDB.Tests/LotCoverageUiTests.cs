using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>70B3A-D — rótulos/mensagens da UI (sem clique/pixel).</summary>
[Collection(TempDatabaseCollection.Name)]
public class LotCoverageUiTests
{
    [Fact]
    public void Status_TraduzDominioParaPortugues()
    {
        Assert.Equal("Rastreabilidade completa", LotCoverageUi.TraceabilityLabel(LotCoverageTraceability.Complete));
        Assert.Equal("Rastreabilidade parcial", LotCoverageUi.TraceabilityLabel(LotCoverageTraceability.Partial));
        Assert.Equal("Sem rastreamento", LotCoverageUi.TraceabilityLabel(LotCoverageTraceability.Untracked));
        Assert.Equal("Cobertura inconsistente", LotCoverageUi.ConsistencyLabel(LotCoverageConsistencyStatus.OverTracked));
        Assert.Equal("Estoque negativo", LotCoverageUi.ConsistencyLabel(LotCoverageConsistencyStatus.NegativeStock));
        Assert.Contains("maior que o estoque", LotCoverageUi.ConsistencyHint(LotCoverageConsistencyStatus.OverTracked));
        Assert.Contains("negativo", LotCoverageUi.ConsistencyHint(LotCoverageConsistencyStatus.NegativeStock));
    }

    [Fact]
    public void Erros_MapaPortuguesAmigavel()
    {
        Assert.Contains("sem rastreamento", LotCoverageUi.MapErrorCode(LotCoverageRules.QuantityExceedsUntracked)!);
        Assert.Contains("origens diferentes", LotCoverageUi.MapErrorCode(LotCoverageRules.KeyCollision)!);
        Assert.Contains("inventário", LotCoverageUi.MapErrorCode(LotCoverageRules.OpenInventory)!);
        Assert.Contains("não pode ser dividida", LotCoverageUi.MapPurchaseProtected("split"));
        Assert.Contains("não pode ser alterada", LotCoverageUi.MapPurchaseProtected("quantity"));
        Assert.Contains("não pode ser removida", LotCoverageUi.MapPurchaseProtected("remove"));
    }

    [Fact]
    public void LoteVazio_Traco_ValidadeNull_NaoInformada()
    {
        Assert.Equal("—", LotCoverageUi.LotDisplay(""));
        Assert.Equal("—", LotCoverageUi.LotDisplay("   "));
        Assert.Equal("ABC", LotCoverageUi.LotDisplay("ABC"));
        Assert.Equal("Não informada", LotCoverageUi.ExpiryDisplay(null));
        Assert.Equal("30/09/2026", LotCoverageUi.ExpiryDisplay(new DateTime(2026, 9, 30)));
    }

    [Fact]
    public void Origem_CompraVsManual()
    {
        Assert.Equal("Compra", LotCoverageUi.OriginLabel(12));
        Assert.Equal("Conferência manual", LotCoverageUi.OriginLabel(null));
        Assert.Equal("Compra #12", LotCoverageUi.OriginDetail(12));
        Assert.Equal("Conferência manual", LotCoverageUi.OriginDetail(null));
    }

    [Fact]
    public void LinhaUi_NaoMesclaOrigens_EFormataCampos()
    {
        var purchase = new LotCoverageLine
        {
            Id = 1,
            Quantity = 20,
            ExpiryDate = new DateTime(2026, 9, 30),
            LotNumber = "ABC",
            PurchaseId = 123,
            UnitCost = 2,
            CostSource = LotCostSource.LotRecorded,
            UsedCost = 2,
            Traceability = LotCoverageTraceability.Complete,
        };
        var manual = new LotCoverageLine
        {
            Id = 2,
            Quantity = 30,
            ExpiryDate = new DateTime(2026, 9, 30),
            LotNumber = "ABC",
            PurchaseId = null,
            UnitCost = 0,
            CostSource = LotCostSource.CurrentAverageEstimate,
            UsedCost = 4,
            Traceability = LotCoverageTraceability.Complete,
        };

        var rows = LotCoverageUi.ToRows(new LotCoverageSnapshot
        {
            Lines = [purchase, manual],
        });

        Assert.Equal(2, rows.Count);
        Assert.Equal("Compra #123", rows[0].OriginDisplay);
        Assert.Equal("Conferência manual", rows[1].OriginDisplay);
        Assert.Equal("ABC", rows[0].LotDisplay);
        Assert.Equal("30/09/2026", rows[0].ExpiryDisplay);
        Assert.True(rows[0].IsPurchaseOrigin);
        Assert.False(rows[1].IsPurchaseOrigin);
    }

    [Fact]
    public void Permissao_AdminEGestorMutam_VendedorNao()
    {
        using var _ = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

        TestDataHelper.SetSessionRole("admin");
        Assert.True(AccessControl.CanMutateLotCoverage());
        Assert.True(LotCoverageUi.CanMutateUi());

        TestDataHelper.SetSessionRole("gestor");
        Assert.True(AccessControl.CanMutateLotCoverage());
        Assert.True(LotCoverageUi.CanMutateUi());

        TestDataHelper.SetSessionCustomPermissions("vendedor", p => p.RelatoriosAcesso = true);
        Assert.False(AccessControl.CanMutateLotCoverage());
        Assert.False(LotCoverageUi.CanMutateUi());
    }

    [Fact]
    public void RedeLojaCliente_CanMutateUiFalse()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        Assert.False(LotCoverageUi.CanMutateUi());
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
    }

    [Fact]
    public void Mensagens_RemoveNaoAlteraEstoque_EPurchaseProtected()
    {
        Assert.Contains("NÃO removerá o produto do estoque", LotCoverageUi.RemoveConfirmMessage);
        Assert.Contains("estoque sem rastreamento", LotCoverageUi.RemoveConfirmMessage);
        Assert.Contains("estoque físico", LotCoverageUi.QuantityHint);
        Assert.Contains("não será alterado", LotCoverageUi.QuantityHint);
        Assert.Contains("origem em uma compra", LotCoverageUi.EditPurchaseHint);
    }

    [Fact]
    public void Refresh_AposAdd_SnapshotAtualiza()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "UI70", "UI COBERTURA");

        var before = LotCoverageService.GetSnapshot(id);
        Assert.Equal(100, before.UntrackedQuantity);
        Assert.Equal(0, before.TrackedQuantity);

        LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = id,
            Quantity = 60,
            ExpiryDate = new DateTime(2026, 9, 30),
            LotNumber = "",
        });
        LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = id,
            Quantity = 40,
            ExpiryDate = new DateTime(2026, 11, 30),
            LotNumber = "",
        });

        var after = LotCoverageService.GetSnapshot(id);
        Assert.Equal(100, after.Stock);
        Assert.Equal(100, after.TrackedQuantity);
        Assert.Equal(0, after.UntrackedQuantity);
        Assert.Equal(2, after.Lines.Count);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));

        var rows = LotCoverageUi.ToRows(after);
        Assert.All(rows, r => Assert.Equal("—", r.LotDisplay));
        Assert.All(rows, r => Assert.Equal("Conferência manual", r.OriginDisplay));
        Assert.Contains(rows, r => r.ExpiryDisplay == "30/09/2026" && r.QtyDisplay == "60");
        Assert.Contains(rows, r => r.ExpiryDisplay == "30/11/2026" && r.QtyDisplay == "40");
        Assert.Equal("Há estoque sem rastreamento", LotCoverageUi.ConsistencyLabel(before.ConsistencyStatus));
        Assert.Equal("Cobertura consistente", LotCoverageUi.ConsistencyLabel(after.ConsistencyStatus));
    }

    [Fact]
    public void ParseQtyEValidade_Basicos()
    {
        Assert.True(LotCoverageUi.TryParseQty("40", out var q, out _));
        Assert.Equal(40, q);
        Assert.False(LotCoverageUi.TryParseQty("0", out _, out var err0));
        Assert.Contains("maior que zero", err0);
        Assert.True(LotCoverageUi.TryParseExpiry("30/09/2026", out var d, out _));
        Assert.Equal(new DateTime(2026, 9, 30), d);
        Assert.False(LotCoverageUi.TryParseExpiry("", out _, out var errE));
        Assert.Contains("validade", errE, StringComparison.OrdinalIgnoreCase);
    }
}
