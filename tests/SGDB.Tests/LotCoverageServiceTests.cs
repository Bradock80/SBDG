using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 70B3A-B — motor de cobertura validade/lote. Bancos isolados em %TEMP%\SGDB.Tests.
/// Não toca AppData\SGDB\deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class LotCoverageServiceTests
{
    private static readonly DateTime ExpA = new(2026, 9, 30);
    private static readonly DateTime ExpB = new(2026, 11, 30);

    private static TempDatabase Begin()
    {
        LotCoverageService.TestBeforeSplitDestination = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void AmbienteIsolado_NaoUsaBancoDaLoja()
    {
        using var db = Begin();
        Assert.Contains("SGDB.Tests", DatabaseService.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deposito.db", DatabaseService.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(
            Path.Combine(Path.GetTempPath(), "SGDB.Tests"),
            Path.GetFullPath(DatabaseService.DatabasePath),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Consulta_Stock100_Lotes0_Untracked100()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(100, snap.Stock);
        Assert.Equal(0, snap.TrackedQuantity);
        Assert.Equal(100, snap.UntrackedQuantity);
        Assert.Equal(0, snap.OverCoverage);
        Assert.Equal(LotCoverageConsistencyStatus.UnderTracked, snap.ConsistencyStatus);
        Assert.Empty(snap.Lines);
    }

    [Fact]
    public void Consulta_Stock100_Lotes60_Untracked40()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        ReceiveLot(id, 60, ExpA, "A");
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(100, snap.Stock);
        Assert.Equal(60, snap.TrackedQuantity);
        Assert.Equal(40, snap.UntrackedQuantity);
        Assert.Equal(LotCoverageConsistencyStatus.UnderTracked, snap.ConsistencyStatus);
    }

    [Fact]
    public void Consulta_Stock100_Lotes100_Untracked0()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        ReceiveLot(id, 100, ExpA, "A");
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(0, snap.UntrackedQuantity);
        Assert.Equal(LotCoverageConsistencyStatus.Consistent, snap.ConsistencyStatus);
    }

    [Fact]
    public void Consulta_Stock80_Lotes100_OverTracked20()
    {
        using var _ = Begin();
        var id = SeedProduct(80);
        ReceiveLot(id, 100, ExpA, "A");
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(80, snap.Stock);
        Assert.Equal(100, snap.TrackedQuantity);
        Assert.Equal(0, snap.UntrackedQuantity);
        Assert.Equal(20, snap.OverCoverage);
        Assert.Equal(LotCoverageConsistencyStatus.OverTracked, snap.ConsistencyStatus);
    }

    [Fact]
    public void Consulta_StockNegativo_NegativeStock()
    {
        using var _ = Begin();
        var id = SeedProduct(10);
        SetStock(id, -5);
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(-5, snap.Stock);
        Assert.Equal(LotCoverageConsistencyStatus.NegativeStock, snap.ConsistencyStatus);
    }

    [Fact]
    public void Consulta_GeladeiraNaoAumentaCapacidade()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        TestDataHelper.SetProductFridge(id, 50);
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(50, snap.StockFridge);
        Assert.Equal(100, snap.UntrackedQuantity);
        Assert.Equal(100, snap.Stock);
    }

    [Fact]
    public void Add_60EmStock100_PreservaStock()
    {
        using var _ = Begin();
        var id = SeedProduct(100, cost: 4);
        var result = Add(id, 60, ExpA, "ABC");
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(100, result.Snapshot.Stock);
        Assert.Equal(60, result.Snapshot.TrackedQuantity);
        Assert.Equal(40, result.Snapshot.UntrackedQuantity);
        Assert.Equal(0, CountMovements(id));
        Assert.Equal(1, CountAudit(LotCoverageRules.ActionAdd));
    }

    [Fact]
    public void Add_Depois40_CoberturaCompleta_Mais1Recusado()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        Add(id, 60, ExpA, "ABC");
        var full = Add(id, 40, ExpB, "XYZ");
        Assert.Equal(100, full.Snapshot.TrackedQuantity);
        Assert.Equal(0, full.Snapshot.UntrackedQuantity);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));

        var ex = Assert.Throws<LotCoverageException>(() => Add(id, 1, ExpB, "ZZZ"));
        Assert.Equal(LotCoverageRules.QuantityExceedsUntracked, ex.ErrorCode);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(100, SumLots(id));
    }

    [Fact]
    public void Add_110_Recusado()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var ex = Assert.Throws<LotCoverageException>(() => Add(id, 110, ExpA, "ABC"));
        Assert.Equal(LotCoverageRules.QuantityExceedsUntracked, ex.ErrorCode);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, SumLots(id));
    }

    [Fact]
    public void Add_QuantityZeroENegativa_Recusado()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var zero = Assert.Throws<LotCoverageException>(() => Add(id, 0, ExpA, "A"));
        Assert.Equal(LotCoverageRules.QuantityInvalid, zero.ErrorCode);
        var neg = Assert.Throws<LotCoverageException>(() => Add(id, -5, ExpA, "A"));
        Assert.Equal(LotCoverageRules.QuantityInvalid, neg.ErrorCode);
        Assert.Equal(0, SumLots(id));
    }

    [Fact]
    public void Add_ValidadeSemLote_Partial()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var result = Add(id, 10, ExpA, lot: "");
        Assert.Equal(LotCoverageTraceability.Partial, result.Snapshot.Lines[0].Traceability);
        Assert.Equal("", result.Snapshot.Lines[0].LotNumber);
        Assert.DoesNotContain("SEMLOTE", result.Snapshot.Lines[0].LotNumber);
        Assert.DoesNotContain("0000", result.Snapshot.Lines[0].LotNumber);
        Assert.DoesNotContain("N/A", result.Snapshot.Lines[0].LotNumber);
    }

    [Fact]
    public void Add_ValidadeComLote_Complete()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var result = Add(id, 10, ExpA, "ABC");
        Assert.Equal(LotCoverageTraceability.Complete, result.Snapshot.Lines[0].Traceability);
    }

    [Fact]
    public void Add_MesmaIdentidade_Merge()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var first = Add(id, 20, ExpA, "ABC");
        var second = Add(id, 15, ExpA, "ABC");
        Assert.Equal(first.ProductLotId, second.ProductLotId);
        Assert.Single(second.Snapshot.Lines);
        Assert.Equal(35, second.Snapshot.TrackedQuantity);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void Add_MesmaValidadeLotesDiferentes_Separados()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        Add(id, 20, ExpA, "ABC");
        Add(id, 10, ExpA, "XYZ");
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(2, snap.Lines.Count);
        Assert.Equal(30, snap.TrackedQuantity);
    }

    [Fact]
    public void Add_LoteVazioValidadesDiferentes_Separados()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        Add(id, 20, ExpA, "");
        Add(id, 10, ExpB, "");
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(2, snap.Lines.Count);
        Assert.All(snap.Lines, l => Assert.Equal(LotCoverageTraceability.Partial, l.Traceability));
    }

    [Fact]
    public void Custo_CoberturaLegado_UnitCostZero_EstimaMediaQuandoHouver()
    {
        using var _ = Begin();
        var id = SeedProduct(100, cost: 4.5);
        var result = Add(id, 10, ExpA, "ABC");
        Assert.Equal(0, result.Snapshot.Lines[0].UnitCost);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, result.Snapshot.Lines[0].CostSource);
        Assert.Equal(4.5, result.Snapshot.Lines[0].UsedCost);

        var resolved = ValidityControlEngine.ResolveLotCost(0, 4.5);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, resolved.Source);
        Assert.Equal(4.5, resolved.UsedCost);
    }

    [Fact]
    public void Custo_Indisponivel_PermaneceUnavailable()
    {
        using var _ = Begin();
        var id = SeedProduct(100, cost: 0);
        var result = Add(id, 10, ExpA, "ABC");
        Assert.Equal(0, result.Snapshot.Lines[0].UnitCost);
        Assert.Equal(LotCostSource.Unavailable, result.Snapshot.Lines[0].CostSource);
        Assert.Null(result.Snapshot.Lines[0].UsedCost);
        Assert.Equal(LotCostSource.Unavailable, ValidityControlEngine.ResolveLotCost(0, 0).Source);
    }

    [Fact]
    public void Edit_CorrigeValidadeELote_PreservaStockEId()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var added = Add(id, 40, ExpA, "ABC");
        var lotId = added.ProductLotId!.Value;
        var edited = LotCoverageService.EditCoverage(new LotCoverageEditInput
        {
            ProductLotId = lotId,
            ExpiryDate = ExpB,
            LotNumber = "XYZ",
            Reason = "Correção de etiqueta",
        });
        Assert.Equal(lotId, edited.ProductLotId);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(40, edited.Snapshot.TrackedQuantity);
        Assert.Equal("XYZ", edited.Snapshot.Lines[0].LotNumber);
        Assert.Equal(ExpB, edited.Snapshot.Lines[0].ExpiryDate);
        Assert.Equal(1, CountAudit(LotCoverageRules.ActionEdit));
    }

    [Fact]
    public void Edit_MotivoAusente_Recusado()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var lotId = Add(id, 10, ExpA, "A").ProductLotId!.Value;
        var ex = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.EditCoverage(new LotCoverageEditInput
            {
                ProductLotId = lotId,
                ExpiryDate = ExpB,
                LotNumber = "A",
                Reason = "  ",
            }));
        Assert.Equal(LotCoverageRules.ReasonRequired, ex.ErrorCode);
    }

    [Fact]
    public void Edit_CorrecaoDeVencido_SensivelEAuditada()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var expired = DateTime.Today.AddDays(-10);
        var lotId = Add(id, 10, expired, "VENC").ProductLotId!.Value;
        var edited = LotCoverageService.EditCoverage(new LotCoverageEditInput
        {
            ProductLotId = lotId,
            ExpiryDate = DateTime.Today.AddDays(20),
            LotNumber = "VENC",
            Reason = "Data da nota estava errada",
        });
        Assert.True(edited.SensitiveExpiryCorrection);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Contains("sensitive_expiry_correction", LastAuditDetails(LotCoverageRules.ActionEdit));
    }

    [Fact]
    public void Edit_ColisaoDeChave_RecusadaSemMerge()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var a = Add(id, 20, ExpA, "ABC").ProductLotId!.Value;
        var b = Add(id, 10, ExpB, "XYZ").ProductLotId!.Value;
        var ex = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.EditCoverage(new LotCoverageEditInput
            {
                ProductLotId = b,
                ExpiryDate = ExpA,
                LotNumber = "ABC",
                Reason = "tentativa de unir",
            }));
        Assert.Equal(LotCoverageRules.KeyCollision, ex.ErrorCode);
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(2, snap.Lines.Count);
        Assert.Equal(a, snap.Lines.Single(l => l.LotNumber == "ABC").Id);
        Assert.Equal(b, snap.Lines.Single(l => l.LotNumber == "XYZ").Id);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void Quantidade_60Para50_UntrackedAumenta10()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var lotId = Add(id, 60, ExpA, "A").ProductLotId!.Value;
        var result = LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
        {
            ProductLotId = lotId,
            Quantity = 50,
            Reason = "Contagem da caixa",
        });
        Assert.Equal(100, result.Snapshot.Stock);
        Assert.Equal(50, result.Snapshot.TrackedQuantity);
        Assert.Equal(50, result.Snapshot.UntrackedQuantity);
        Assert.Equal(1, CountAudit(LotCoverageRules.ActionQuantityCorrect));
    }

    [Fact]
    public void Quantidade_60Para80_PermitidoComCapacidade()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var lotId = Add(id, 60, ExpA, "A").ProductLotId!.Value;
        var result = LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
        {
            ProductLotId = lotId,
            Quantity = 80,
            Reason = "Faltavam 20 na cobertura",
        });
        Assert.Equal(80, result.Snapshot.TrackedQuantity);
        Assert.Equal(20, result.Snapshot.UntrackedQuantity);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void Quantidade_60Para110_Recusado()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var lotId = Add(id, 60, ExpA, "A").ProductLotId!.Value;
        var ex = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
            {
                ProductLotId = lotId,
                Quantity = 110,
                Reason = "forçar 110",
            }));
        Assert.Equal(LotCoverageRules.QuantityExceedsUntracked, ex.ErrorCode);
        Assert.Equal(60, SumLots(id));
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void Split_100Em60e40_StockInalterado_SomaConservada()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var originId = Add(id, 100, ExpA, "").ProductLotId!.Value;
        var result = LotCoverageService.SplitCoverage(new LotCoverageSplitInput
        {
            ProductLotId = originId,
            DestinationQuantity = 40,
            DestinationExpiryDate = ExpB,
            DestinationLotNumber = "",
            Reason = "Duas validades na mesma pilha",
        });
        Assert.Equal(100, result.Snapshot.Stock);
        Assert.Equal(100, result.Snapshot.TrackedQuantity);
        Assert.Equal(0, result.Snapshot.UntrackedQuantity);
        Assert.Equal(2, result.Snapshot.Lines.Count);
        Assert.Equal(60, result.Snapshot.Lines.Single(l => l.Id == originId).Quantity);
        Assert.Equal(40, result.Snapshot.Lines.Single(l => l.Id == result.DestinationLotId).Quantity);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(1, CountAudit(LotCoverageRules.ActionSplit));
    }

    [Fact]
    public void Split_FalhaNoDestino_RollbackCompleto()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var originId = Add(id, 100, ExpA, "A").ProductLotId!.Value;
        LotCoverageService.TestBeforeSplitDestination = () =>
            throw new InvalidOperationException("falha injetada no destino");

        Assert.Throws<InvalidOperationException>(() =>
            LotCoverageService.SplitCoverage(new LotCoverageSplitInput
            {
                ProductLotId = originId,
                DestinationQuantity = 40,
                DestinationExpiryDate = ExpB,
                DestinationLotNumber = "B",
                Reason = "dividir",
            }));

        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(100, SumLots(id));
        Assert.Single(LotCoverageService.GetSnapshot(id).Lines);
        Assert.Equal(0, CountAudit(LotCoverageRules.ActionSplit));
    }

    [Fact]
    public void Split_ColisaoDestino_Recusada()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var originId = Add(id, 60, ExpA, "A").ProductLotId!.Value;
        Add(id, 20, ExpB, "B");
        var ex = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.SplitCoverage(new LotCoverageSplitInput
            {
                ProductLotId = originId,
                DestinationQuantity = 10,
                DestinationExpiryDate = ExpB,
                DestinationLotNumber = "B",
                Reason = "destino já existe",
            }));
        Assert.Equal(LotCoverageRules.KeyCollision, ex.ErrorCode);
        Assert.Equal(60, GetLotQty(originId));
    }

    [Fact]
    public void Remove_NaoAlteraStock_AumentaUntracked_MotivoObrigatorio()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        Add(id, 40, ExpA, "KEEP");
        var removeId = Add(id, 20, ExpB, "DROP").ProductLotId!.Value;
        var missing = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.RemoveCoverage(new LotCoverageRemoveInput { ProductLotId = removeId, Reason = "" }));
        Assert.Equal(LotCoverageRules.ReasonRequired, missing.ErrorCode);

        var result = LotCoverageService.RemoveCoverage(new LotCoverageRemoveInput
        {
            ProductLotId = removeId,
            Reason = "Etiqueta ilegível",
        });
        Assert.Equal(100, result.Snapshot.Stock);
        Assert.Equal(40, result.Snapshot.TrackedQuantity);
        Assert.Equal(60, result.Snapshot.UntrackedQuantity);
        Assert.Equal(0, CountMovements(id));
        Assert.Equal(1, CountAudit(LotCoverageRules.ActionRemove));
    }

    [Fact]
    public void Bloqueio_ProdutoInexistente()
    {
        using var _ = Begin();
        var ex = Assert.Throws<LotCoverageException>(() => Add(99999, 10, ExpA, "A"));
        Assert.Equal(LotCoverageRules.ProductNotFound, ex.ErrorCode);
        Assert.Equal(LotCoverageConsistencyStatus.ProductNotFound, LotCoverageService.GetSnapshot(99999).ConsistencyStatus);
    }

    [Fact]
    public void Bloqueio_ProdutoInativo()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        SetActive(id, 0);
        var ex = Assert.Throws<LotCoverageException>(() => Add(id, 10, ExpA, "A"));
        Assert.Equal(LotCoverageRules.InactiveProduct, ex.ErrorCode);
    }

    [Fact]
    public void Bloqueio_ProdutoAbsorbUnificado()
    {
        using var _ = Begin();
        var keep = SeedProduct(50, code: "KEEP1", name: "KEEP");
        var absorb = SeedProduct(30, code: "ABS1", name: "ABSORB");
        ProductService.MergeProducts(keep, absorb);
        var ex = Assert.Throws<LotCoverageException>(() => Add(absorb, 10, ExpA, "A"));
        Assert.Equal(LotCoverageRules.AbsorbedProduct, ex.ErrorCode);
    }

    [Fact]
    public void Bloqueio_StockZeroParaAdd()
    {
        using var _ = Begin();
        var id = SeedProduct(0);
        var ex = Assert.Throws<LotCoverageException>(() => Add(id, 10, ExpA, "A"));
        Assert.Equal(LotCoverageRules.ZeroStock, ex.ErrorCode);
        Assert.Equal(LotCoverageConsistencyStatus.ZeroStock, LotCoverageService.GetSnapshot(id).ConsistencyStatus);
    }

    [Fact]
    public void Bloqueio_StockNegativoParaAdd()
    {
        using var _ = Begin();
        var id = SeedProduct(10);
        SetStock(id, -5);
        var ex = Assert.Throws<LotCoverageException>(() => Add(id, 1, ExpA, "A"));
        Assert.Equal(LotCoverageRules.NegativeStock, ex.ErrorCode);
        Assert.Equal(-5, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void Bloqueio_OverTracked_NaoAumentaCobertura()
    {
        using var _ = Begin();
        var id = SeedProduct(80);
        ReceiveLot(id, 100, ExpA, "A");
        var lotId = LotCoverageService.GetSnapshot(id).Lines[0].Id;

        var add = Assert.Throws<LotCoverageException>(() => Add(id, 1, ExpB, "B"));
        Assert.Equal(LotCoverageRules.OverTracked, add.ErrorCode);

        var qty = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
            {
                ProductLotId = lotId,
                Quantity = 110,
                Reason = "piorar over",
            }));
        Assert.Equal(LotCoverageRules.OverTracked, qty.ErrorCode);
        Assert.Equal(80, TestDataHelper.GetProductStock(id));
        Assert.Equal(100, SumLots(id));
    }

    [Fact]
    public void AdvogadoDoDiabo_NaoRastreia110ComStock100()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        Add(id, 60, ExpA, "A");
        Assert.Throws<LotCoverageException>(() => Add(id, 50, ExpB, "B"));
        Assert.Throws<LotCoverageException>(() => Add(id, 41, ExpB, "B"));
        var lotId = LotCoverageService.GetSnapshot(id).Lines[0].Id;
        Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
            {
                ProductLotId = lotId,
                Quantity = 110,
                Reason = "110",
            }));
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.True(SumLots(id) <= 100);
    }

    [Fact]
    public void Bloqueio_InventarioAberto()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        InventoryService.CreateSession();
        var ex = Assert.Throws<LotCoverageException>(() => Add(id, 10, ExpA, "A"));
        Assert.Equal(LotCoverageRules.OpenInventory, ex.ErrorCode);
        Assert.NotNull(InventoryService.GetOpenSession());
    }

    [Fact]
    public void Bloqueio_Vendedor()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        TestDataHelper.SetSessionCustomPermissions("vendedor", p => p.RelatoriosAcesso = true);
        var ex = Assert.Throws<LotCoverageException>(() => Add(id, 10, ExpA, "A"));
        Assert.Equal(LotCoverageRules.AccessDenied, ex.ErrorCode);
        Assert.False(AccessControl.CanMutateLotCoverage());
    }

    [Fact]
    public void GestorPodeMutar()
    {
        using var _ = Begin();
        TestDataHelper.SetSessionRole("gestor");
        var id = SeedProduct(100);
        var result = Add(id, 10, ExpA, "A");
        Assert.True(result.Ok);
        Assert.Equal(10, result.Snapshot.TrackedQuantity);
    }

    [Fact]
    public void Bloqueio_ClienteRedeLoja()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        Assert.Throws<StoreNetworkClientBlockedException>(() => Add(id, 10, ExpA, "A"));
        Assert.Equal(0, SumLots(id));
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void Regressao_ProductLotReceive_ContinuaSemAlterarStock()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = id,
            Quantity = 10,
            LotNumber = "L-REC",
            ExpiryDate = ExpA,
            UnitCost = 3,
        });
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(10, SumLots(id));
    }

    [Fact]
    public void Regressao_DeductFefo_BaixaValidadeMaisProxima()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        ReceiveLot(id, 60, DateTime.Today.AddDays(10), "A");
        ReceiveLot(id, 40, DateTime.Today.AddDays(40), "B");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        ProductLotService.DeductFefo(conn, tx, id, 10);
        tx.Commit();
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(50, GetLotQtyByNumber(id, "A"));
        Assert.Equal(40, GetLotQtyByNumber(id, "B"));
    }

    [Fact]
    public void Regressao_GeladeiraForaDosLotes()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        TestDataHelper.SetProductFridge(id, 50);
        Add(id, 100, ExpA, "A");
        Assert.Equal(50, TestDataHelper.GetProductFridge(id));
        Assert.Equal(100, SumLots(id));
        Assert.Equal(0, LotCoverageService.GetSnapshot(id).UntrackedQuantity);
    }

    [Fact]
    public void Regressao_ValidityControlEngine_ContinuaResolvendoCusto()
    {
        using var _ = Begin();
        var recorded = ValidityControlEngine.ResolveLotCost(2.5, 4);
        Assert.Equal(LotCostSource.LotRecorded, recorded.Source);
        var estimate = ValidityControlEngine.ResolveLotCost(0, 4);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, estimate.Source);
        var missing = ValidityControlEngine.ResolveLotCost(0, 0);
        Assert.Equal(LotCostSource.Unavailable, missing.Source);
    }

    [Fact]
    public void Regressao_CompraComLote_AindaSobeStockELote()
    {
        using var _ = Begin();
        var id = SeedProduct(0, code: "C70B", name: "COMPRA LOTE");
        var supplierId = SeedSupplier();
        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-70B3A-B",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = id,
                    ProductName = "COMPRA LOTE",
                    Quantity = 50,
                    UnitPrice = 4,
                    LotNumber = "LOTE-A",
                    ExpiryDate = ExpA,
                },
            ],
        }, closeOnSave: true);
        Assert.Equal(50, TestDataHelper.GetProductStock(id));
        Assert.Equal(50, SumLots(id));
    }

    [Fact]
    public void Add_MotivoPadraoConferenciaFisica()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        Add(id, 10, ExpA, "A");
        var details = LastAuditDetails(LotCoverageRules.ActionAdd);
        Assert.True(AuditPayloadBuilder.TryParse(details, out var doc));
        Assert.Equal(LotCoverageRules.PhysicalConferenceReason, doc.Payload.GetProperty("reason").GetString());
        Assert.Equal(LotCoverageRules.OriginLegacyConference, doc.Payload.GetProperty("origin").GetString());
    }

    [Fact]
    public void Consulta_LoteSemValidade_UninformedExpiry()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = id,
            Quantity = 10,
            LotNumber = "",
            ExpiryDate = null,
        });
        var snap = LotCoverageService.GetSnapshot(id);
        Assert.Equal(LotCoverageTraceability.UninformedExpiry, snap.Lines[0].Traceability);
        Assert.Equal(90, snap.UntrackedQuantity);
    }

    // ---------- 70B3A-B1: origem financeira × cobertura manual ----------

    [Fact]
    public void B1_AddManual_NaoMergeComCompra_DuasOrigens_ManualNaoLotRecorded()
    {
        using var _ = Begin();
        var id = SeedProduct(50, cost: 4);
        var purchaseLotId = InsertPurchaseLot(id, qty: 20, lot: "ABC", expiry: ExpA, purchaseId: 123, unitCost: 2);
        var fridgeBefore = TestDataHelper.GetProductFridge(id);

        var added = Add(id, 30, ExpA, "ABC");
        var snap = LotCoverageService.GetSnapshot(id);

        Assert.Equal(2, snap.Lines.Count);
        Assert.NotEqual(purchaseLotId, added.ProductLotId);

        var bought = snap.Lines.Single(l => l.Id == purchaseLotId);
        Assert.Equal(20, bought.Quantity);
        Assert.Equal(123, bought.PurchaseId);
        Assert.Equal(2, bought.UnitCost);
        Assert.Equal(LotCostSource.LotRecorded, bought.CostSource);

        var manual = snap.Lines.Single(l => l.Id == added.ProductLotId);
        Assert.Equal(30, manual.Quantity);
        Assert.Null(manual.PurchaseId);
        Assert.Equal(0, manual.UnitCost);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, manual.CostSource);
        Assert.NotEqual(LotCostSource.LotRecorded, manual.CostSource);

        Assert.Equal(50, TestDataHelper.GetProductStock(id));
        Assert.Equal(fridgeBefore, TestDataHelper.GetProductFridge(id));
        Assert.Equal(50, snap.TrackedQuantity);
    }

    [Fact]
    public void B1_AddManual_SemprePurchaseIdNull_UnitCostZero()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var result = Add(id, 10, ExpA, "M1");
        var (purchaseId, unitCost) = GetLotOrigin(result.ProductLotId!.Value);
        Assert.Null(purchaseId);
        Assert.Equal(0, unitCost);
    }

    [Fact]
    public void B1_DuasManuaisEquivalentes_Consolidam()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var first = Add(id, 20, ExpA, "ABC");
        var second = Add(id, 15, ExpA, "ABC");
        Assert.Equal(first.ProductLotId, second.ProductLotId);
        Assert.Single(second.Snapshot.Lines);
        Assert.Equal(35, second.Snapshot.TrackedQuantity);
        var (purchaseId, unitCost) = GetLotOrigin(first.ProductLotId!.Value);
        Assert.Null(purchaseId);
        Assert.Equal(0, unitCost);
    }

    [Fact]
    public void B1_ManualNaoAbsorveCompra_NemCompraAbsorveManual()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var purchaseLotId = InsertPurchaseLot(id, 20, "ABC", ExpA, 55, 2.5);
        var manualId = Add(id, 10, ExpA, "ABC").ProductLotId!.Value;
        Assert.NotEqual(purchaseLotId, manualId);

        // Segunda manual consolida só com a manual, não com a compra.
        var again = Add(id, 5, ExpA, "ABC");
        Assert.Equal(manualId, again.ProductLotId);
        Assert.Equal(2, again.Snapshot.Lines.Count);
        Assert.Equal(20, GetLotQty(purchaseLotId));
        Assert.Equal(15, GetLotQty(manualId));
        Assert.Equal(2.5, GetLotOrigin(purchaseLotId).UnitCost);
        Assert.Equal(0, GetLotOrigin(manualId).UnitCost);
    }

    [Fact]
    public void B1_RemoveManual_Permitido_StockInalterado()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        InsertPurchaseLot(id, 20, "BUY", ExpA, 1, 2);
        var manualId = Add(id, 10, ExpB, "MAN").ProductLotId!.Value;
        var fridge = TestDataHelper.GetProductFridge(id);

        var result = LotCoverageService.RemoveCoverage(new LotCoverageRemoveInput
        {
            ProductLotId = manualId,
            Reason = "Etiqueta ilegível",
        });

        Assert.Equal(100, result.Snapshot.Stock);
        Assert.Equal(20, result.Snapshot.TrackedQuantity);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(fridge, TestDataHelper.GetProductFridge(id));
    }

    [Fact]
    public void B1_RemoveLinhaDeCompra_Bloqueado()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var purchaseLotId = InsertPurchaseLot(id, 20, "ABC", ExpA, 9, 2);
        var ex = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.RemoveCoverage(new LotCoverageRemoveInput
            {
                ProductLotId = purchaseLotId,
                Reason = "tentar apagar compra",
            }));
        Assert.Equal(LotCoverageRules.PurchaseOriginProtected, ex.ErrorCode);
        Assert.Equal(20, GetLotQty(purchaseLotId));
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void B1_SplitManual_Permitido_TotalConstante()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var originId = Add(id, 100, ExpA, "").ProductLotId!.Value;
        var result = LotCoverageService.SplitCoverage(new LotCoverageSplitInput
        {
            ProductLotId = originId,
            DestinationQuantity = 40,
            DestinationExpiryDate = ExpB,
            DestinationLotNumber = "",
            Reason = "duas validades",
        });
        Assert.Equal(100, result.Snapshot.TrackedQuantity);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Null(GetLotOrigin(result.DestinationLotId!.Value).PurchaseId);
        Assert.Equal(0, GetLotOrigin(result.DestinationLotId!.Value).UnitCost);
    }

    [Fact]
    public void B1_SplitLinhaDeCompra_Bloqueado()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var purchaseLotId = InsertPurchaseLot(id, 100, "ABC", ExpA, 11, 2);
        var ex = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.SplitCoverage(new LotCoverageSplitInput
            {
                ProductLotId = purchaseLotId,
                DestinationQuantity = 40,
                DestinationExpiryDate = ExpB,
                DestinationLotNumber = "ABC",
                Reason = "duas validades",
            }));
        Assert.Equal(LotCoverageRules.PurchaseOriginProtected, ex.ErrorCode);
        Assert.Equal(100, GetLotQty(purchaseLotId));
        Assert.Single(LotCoverageService.GetSnapshot(id).Lines);
    }

    [Fact]
    public void B1_EditLinhaDeCompra_PreservaIdPurchaseIdUnitCost_Audit()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var purchaseLotId = InsertPurchaseLot(id, 40, "ABC", ExpA, 77, 2.25);
        var edited = LotCoverageService.EditCoverage(new LotCoverageEditInput
        {
            ProductLotId = purchaseLotId,
            ExpiryDate = ExpB,
            LotNumber = "ABC-CORRIGIDO",
            Reason = "Etiqueta da NF",
        });

        Assert.Equal(purchaseLotId, edited.ProductLotId);
        var (purchaseId, unitCost) = GetLotOrigin(purchaseLotId);
        Assert.Equal(77, purchaseId);
        Assert.Equal(2.25, unitCost);
        Assert.Equal("ABC-CORRIGIDO", edited.Snapshot.Lines[0].LotNumber);
        Assert.Equal(ExpB, edited.Snapshot.Lines[0].ExpiryDate);
        Assert.Equal(LotCostSource.LotRecorded, edited.Snapshot.Lines[0].CostSource);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));

        var details = LastAuditDetails(LotCoverageRules.ActionEdit);
        Assert.True(AuditPayloadBuilder.TryParse(details, out var doc));
        Assert.Equal(purchaseLotId, doc.Payload.GetProperty("product_lot_id").GetInt32());
        Assert.Equal("ABC", doc.Payload.GetProperty("before").GetProperty("lot_number").GetString());
        Assert.Equal("ABC-CORRIGIDO", doc.Payload.GetProperty("after").GetProperty("lot_number").GetString());
        Assert.Equal(77, doc.Payload.GetProperty("before").GetProperty("purchase_id").GetInt32());
    }

    [Fact]
    public void B1_QuantityCorrectionLinhaDeCompra_Bloqueado()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var purchaseLotId = InsertPurchaseLot(id, 100, "ABC", ExpA, 12, 2);
        var ex = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
            {
                ProductLotId = purchaseLotId,
                Quantity = 60,
                Reason = "corrigir cobertura",
            }));
        Assert.Equal(LotCoverageRules.PurchaseOriginProtected, ex.ErrorCode);
        Assert.Equal(100, GetLotQty(purchaseLotId));
    }

    [Fact]
    public void B1_CompraReal_AddManualMesmaIdentidade_PurchaseItemLotsIntegro_CancelOk()
    {
        using var _ = Begin();
        var id = SeedProduct(0, code: "B1BUY", name: "B1 COMPRA");
        var supplierId = SeedSupplier();
        var purchaseId = PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-B1-ORIG",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = id,
                    ProductName = "B1 COMPRA",
                    Quantity = 50,
                    UnitPrice = 2,
                    LotNumber = "ABC",
                    ExpiryDate = ExpA,
                },
            ],
        }, closeOnSave: true);

        SetStock(id, 80); // sobra física legado
        var originsBefore = PurchaseService.ListPurchaseItemLots(purchaseId);
        Assert.Single(originsBefore);
        var linkedLotId = originsBefore[0].ProductLotId;
        Assert.NotNull(linkedLotId);

        var manual = Add(id, 30, ExpA, "ABC");
        Assert.NotEqual(linkedLotId, manual.ProductLotId);
        Assert.Equal(2, LotCoverageService.GetSnapshot(id).Lines.Count);

        LotCoverageService.EditCoverage(new LotCoverageEditInput
        {
            ProductLotId = linkedLotId!.Value,
            ExpiryDate = ExpA,
            LotNumber = "ABC-FIX",
            Reason = "correção etiqueta NF",
        });

        var originsAfter = PurchaseService.ListPurchaseItemLots(purchaseId);
        Assert.Single(originsAfter);
        Assert.Equal(linkedLotId, originsAfter[0].ProductLotId);
        Assert.Equal(50, GetLotQty(linkedLotId.Value));
        Assert.Equal(2, GetLotOrigin(linkedLotId.Value).UnitCost);
        Assert.Equal(purchaseId, GetLotOrigin(linkedLotId.Value).PurchaseId);

        // Cancelamento continua íntegro: stock físico precisa cobrir a compra; lotes manuais não atrapalham DeductExact por ID.
        SetStock(id, 50);
        // Remove manual coverage first so tracked purchase lot alone matches cancel deduction (stock already 50).
        LotCoverageService.RemoveCoverage(new LotCoverageRemoveInput
        {
            ProductLotId = manual.ProductLotId!.Value,
            Reason = "limpar manual antes do cancel",
        });

        PurchaseService.Cancel(purchaseId);
        Assert.Equal(0, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, SumLots(id));
        // Após DeductExact zerar o lote, ON DELETE SET NULL em purchase_item_lots.product_lot_id.
        var afterCancel = PurchaseService.ListPurchaseItemLots(purchaseId);
        Assert.Single(afterCancel);
        Assert.Equal(50, afterCancel[0].Quantity);
        Assert.Equal("ABC", afterCancel[0].LotNumber);
    }

    [Fact]
    public void B1_NenhumaManutencaoAlteraStockNemFridge()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        TestDataHelper.SetProductFridge(id, 7);
        var fridge = TestDataHelper.GetProductFridge(id);
        var a = Add(id, 40, ExpA, "A").ProductLotId!.Value;
        LotCoverageService.EditCoverage(new LotCoverageEditInput
        {
            ProductLotId = a, ExpiryDate = ExpB, LotNumber = "A2", Reason = "edit",
        });
        LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
        {
            ProductLotId = a, Quantity = 30, Reason = "qty",
        });
        var split = LotCoverageService.SplitCoverage(new LotCoverageSplitInput
        {
            ProductLotId = a,
            DestinationQuantity = 10,
            DestinationExpiryDate = ExpA,
            DestinationLotNumber = "A3",
            Reason = "split",
        });
        LotCoverageService.RemoveCoverage(new LotCoverageRemoveInput
        {
            ProductLotId = split.DestinationLotId!.Value,
            Reason = "remove",
        });

        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal(fridge, TestDataHelper.GetProductFridge(id));
    }

    [Fact]
    public void B1_OverTracked_AumentoRecusado_ReducaoManualPermitida()
    {
        using var _ = Begin();
        var id = SeedProduct(80);
        var lotId = Add(id, 60, ExpA, "MAN").ProductLotId!.Value;
        // força overtracked sem passar pelo teto do Add
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE product_lots SET quantity = 100 WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", lotId);
            cmd.ExecuteNonQuery();
        }

        Assert.Equal(LotCoverageConsistencyStatus.OverTracked, LotCoverageService.GetSnapshot(id).ConsistencyStatus);

        var addEx = Assert.Throws<LotCoverageException>(() => Add(id, 1, ExpB, "X"));
        Assert.Equal(LotCoverageRules.OverTracked, addEx.ErrorCode);

        var worse = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
            {
                ProductLotId = lotId, Quantity = 110, Reason = "piorar",
            }));
        Assert.Equal(LotCoverageRules.OverTracked, worse.ErrorCode);

        var reduced = LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
        {
            ProductLotId = lotId, Quantity = 90, Reason = "reduzir excesso",
        });
        Assert.Equal(90, reduced.Snapshot.TrackedQuantity);
        Assert.Equal(LotCoverageConsistencyStatus.OverTracked, reduced.Snapshot.ConsistencyStatus);
        Assert.Equal(80, TestDataHelper.GetProductStock(id));

        var fixedSnap = LotCoverageService.CorrectQuantity(new LotCoverageQuantityInput
        {
            ProductLotId = lotId, Quantity = 70, Reason = "recuperar",
        });
        Assert.Equal(70, fixedSnap.Snapshot.TrackedQuantity);
        Assert.Equal(LotCoverageConsistencyStatus.UnderTracked, fixedSnap.Snapshot.ConsistencyStatus);
    }

    [Fact]
    public void B1_EditColisaoComLinhaDeCompra_Recusada()
    {
        using var _ = Begin();
        var id = SeedProduct(100);
        var purchaseLotId = InsertPurchaseLot(id, 20, "ABC", ExpA, 3, 2);
        var manualId = Add(id, 10, ExpB, "XYZ").ProductLotId!.Value;
        var ex = Assert.Throws<LotCoverageException>(() =>
            LotCoverageService.EditCoverage(new LotCoverageEditInput
            {
                ProductLotId = manualId,
                ExpiryDate = ExpA,
                LotNumber = "ABC",
                Reason = "unir com compra",
            }));
        Assert.Equal(LotCoverageRules.KeyCollision, ex.ErrorCode);
        Assert.Equal(20, GetLotQty(purchaseLotId));
        Assert.Equal(10, GetLotQty(manualId));
    }

    [Fact]
    public void B1_AdvogadoDoDiabo_NaoHerdaCustoNemPurchaseId()
    {
        using var _ = Begin();
        var id = SeedProduct(50, cost: 9);
        InsertPurchaseLot(id, 20, "ABC", ExpA, 99, 2);
        var manual = Add(id, 30, ExpA, "ABC");
        var line = LotCoverageService.GetSnapshot(id).Lines.Single(l => l.Id == manual.ProductLotId);
        Assert.Null(line.PurchaseId);
        Assert.Equal(0, line.UnitCost);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, line.CostSource);
        Assert.Equal(9, line.UsedCost);
    }

    private static LotCoverageMutationResult Add(int productId, double qty, DateTime expiry, string lot) =>
        LotCoverageService.AddCoverage(new LotCoverageAddInput
        {
            ProductId = productId,
            Quantity = qty,
            ExpiryDate = expiry,
            LotNumber = lot,
        });

    private static int InsertPurchaseLot(
        int productId, double qty, string lot, DateTime expiry, int purchaseId, double unitCost)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        var id = ProductLotService.Receive(conn, tx, new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = qty,
            LotNumber = lot,
            ExpiryDate = expiry,
            PurchaseId = purchaseId,
            UnitCost = unitCost,
        });
        tx.Commit();
        return id;
    }

    private static (int? PurchaseId, double UnitCost) GetLotOrigin(int lotId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT purchase_id, IFNULL(unit_cost,0) FROM product_lots WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", lotId);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        int? purchaseId = reader.IsDBNull(0) ? null : reader.GetInt32(0);
        return (purchaseId, reader.GetDouble(1));
    }

    private static int SeedProduct(double stock, double cost = 2, string? code = null, string? name = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return TestDataHelper.SeedSimpleProduct(
            stock, salePrice: 5, costPrice: cost,
            code: code ?? $"C{suffix}",
            name: name ?? $"PROD {suffix}");
    }

    private static void ReceiveLot(int productId, double qty, DateTime expiry, string lot) =>
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = qty,
            LotNumber = lot,
            ExpiryDate = expiry,
        });

    private static void SetStock(int productId, double stock)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock = $s WHERE id = $id;";
        cmd.Parameters.AddWithValue("$s", stock);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static void SetActive(int productId, int active)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET active = $a WHERE id = $id;";
        cmd.Parameters.AddWithValue("$a", active);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORNECEDOR 70B3A', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double SumLots(int productId) => TestDataHelper.SumLots(productId);

    private static double GetLotQty(int lotId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT quantity FROM product_lots WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", lotId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static double GetLotQtyByNumber(int productId, string lot)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(quantity),0) FROM product_lots
            WHERE product_id = $id AND IFNULL(lot_number,'') = $lot;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.Parameters.AddWithValue("$lot", lot);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static int CountMovements(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movements WHERE product_id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountAudit(string action)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_log WHERE action = $a;";
        cmd.Parameters.AddWithValue("$a", action);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string LastAuditDetails(string action)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(details,'') FROM audit_log
            WHERE action = $a ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$a", action);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
}
