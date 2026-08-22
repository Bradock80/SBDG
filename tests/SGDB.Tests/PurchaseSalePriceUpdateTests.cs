using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;
using SGDB.Views;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69D-B — intenção por item e persistência de products.sale_price na tx da compra.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseSalePriceUpdateTests
{
    private static readonly JsonSerializerOptions NetworkJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static TempDatabase BeginDb()
    {
        PurchaseService.TestBeforeApplySalePrice = null;
        PurchaseService.TestAfterApplySalePrice = null;
        PurchaseService.TestBeforeApplyAverageCost = null;
        PurchaseService.TestAfterApplyAverageCost = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        return db;
    }

    [Fact]
    public void LoadPreco_NaoMarcaUpdateSalePrice()
    {
        var draft = new PurchaseItemDraft
        {
            PrevSale = 8,
            SalePrice = 8,
            SuggestedPrice = 8.50,
        };
        Assert.False(draft.UpdateSalePrice);
    }

    [Fact]
    public void EdicaoManual_MarcaUpdateSalePrice()
    {
        var draft = new PurchaseItemDraft { PrevSale = 8, SalePrice = 8 };
        draft.ApplySalePrice(9, asOperatorEdit: true);
        Assert.True(draft.UpdateSalePrice);
        Assert.Equal(9, draft.SalePrice);
    }

    [Fact]
    public void CampoSuperior_SincronizaDraft()
    {
        var draft = new PurchaseItemDraft { SalePrice = 8, UpdateSalePrice = false };
        draft.SyncSaleFromForm(9, operatorEdited: true);
        Assert.True(draft.UpdateSalePrice);
        Assert.Equal(9, draft.SalePrice);
    }

    [Fact]
    public void Grade_SalePriceDisplay_SincronizaEMarca()
    {
        var draft = new PurchaseItemDraft { SalePrice = 8 };
        draft.SalePriceDisplay = "9,00";
        Assert.True(draft.UpdateSalePrice);
        Assert.Equal(9, draft.SalePrice);
    }

    [Fact]
    public void DesmarcarDepoisDaEdicao_CancelaAtualizacao()
    {
        var draft = new PurchaseItemDraft { PrevSale = 8, SalePrice = 8 };
        draft.ApplySalePrice(9, asOperatorEdit: true);
        Assert.True(draft.UpdateSalePrice);
        draft.UpdateSalePrice = false;
        Assert.False(draft.UpdateSalePrice);
        Assert.Equal(9, draft.SalePrice);
    }

    [Fact]
    public void SugeridoAceitoExplicitamente_ContaComoUpdate()
    {
        var draft = new PurchaseItemDraft
        {
            UnitPrice = 5,
            SuggestedPrice = 8.50,
            SalePrice = 8,
        };
        draft.AcceptSuggestedSale();
        Assert.True(draft.UpdateSalePrice);
        Assert.Equal(8.50, draft.SalePrice);
    }

    [Fact]
    public void CustoAlteradoNaGrade_NaoMarcaVenda()
    {
        var draft = new PurchaseItemDraft
        {
            Quantity = 1,
            UnitPrice = 5,
            PrevSale = 8,
            SalePrice = 8,
        };
        draft.UnitPriceDisplay = "5,50";
        Assert.False(draft.UpdateSalePrice);
        Assert.Equal(8, draft.SalePrice);
        Assert.Equal(5.50, draft.UnitPrice);
    }

    [Fact]
    public void IntencaoEPorItem_NaoGlobal()
    {
        var a = new PurchaseItemDraft { ProductName = "A", SalePrice = 8 };
        var b = new PurchaseItemDraft { ProductName = "B", SalePrice = 10 };
        a.ApplySalePrice(9, asOperatorEdit: true);
        Assert.True(a.UpdateSalePrice);
        Assert.False(b.UpdateSalePrice);
    }

    [Fact]
    public void ProdutoExistente_8para9_Grava9()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, salePrice: 8, costPrice: 5, code: "P89", name: "EXISTE 8");

        CreateClosed(supplier, productId, "EXISTE 8", qty: 2, unit: 5.50, sale: 9, updateSale: true);

        var product = ProductService.GetById(productId);
        Assert.NotNull(product);
        Assert.Equal(9, product!.SalePrice);
        Assert.Contains(ProductService.List(ativo: "todos"), p => p.Id == productId && p.SalePrice == 9);
    }

    [Fact]
    public void SemEditar_Mantem8_MesmoComCustoNovo()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, salePrice: 8, costPrice: 5, code: "P80", name: "MANTÉM 8");

        CreateClosed(supplier, productId, "MANTÉM 8", qty: 2, unit: 5.50, sale: 8, updateSale: false);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(8, product.SalePrice);
        Assert.Equal(5.08, product.CostPrice);
    }

    [Fact]
    public void DoisProdutos_SoUmAltera()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var a = TestDataHelper.SeedSimpleProduct(10, 8, 5, "PA", "PROD A");
        var b = TestDataHelper.SeedSimpleProduct(10, 10, 6, "PB", "PROD B");

        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-2IT",
            GerarEstoque = true,
            Items =
            [
                Item(a, "PROD A", 1, 5.50, 9, true),
                Item(b, "PROD B", 1, 6.50, 10, false),
            ],
        }, closeOnSave: true);

        Assert.Equal(9, ProductService.GetById(a)!.SalePrice);
        Assert.Equal(10, ProductService.GetById(b)!.SalePrice);
    }

    [Fact]
    public void SalePriceInvalido_BloqueiaCompra()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "INV", "INVALIDO");

        foreach (var bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                CreateClosed(supplier, productId, "INVALIDO", 1, 5.50, bad, true, "NF-INV"));
            Assert.Contains("Preço de venda inválido", ex.Message);
        }

        Assert.Equal(8, ProductService.GetById(productId)!.SalePrice);
        Assert.Equal(0, CountPurchases());
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void ArredondaComHelperMonetario()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "AR", "ARREDONDA");
        var raw = 9.006;
        var expected = ProductPriceHelper.RoundPrice(raw);

        CreateClosed(supplier, productId, "ARREDONDA", 1, 5.50, raw, true);

        Assert.Equal(expected, ProductService.GetById(productId)!.SalePrice);
    }

    [Fact]
    public void ProdutoNovo_Sugerido850_Operador9_Grava9()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, salePrice: 8.50, costPrice: 5, code: "NVO", name: "NOVO SUG");

        CreateClosed(supplier, productId, "NOVO SUG", 1, 5.50, 9, true);

        Assert.Equal(9, ProductService.GetById(productId)!.SalePrice);
    }

    [Fact]
    public void ProdutoNovo_SemEditar_MantemSugerido()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, salePrice: 8.50, costPrice: 5, code: "NV2", name: "NOVO OK");

        CreateClosed(supplier, productId, "NOVO OK", 1, 5.50, 8.50, false);

        Assert.Equal(8.50, ProductService.GetById(productId)!.SalePrice);
    }

    [Fact]
    public void FalhaAntesDoUpdate_RollbackCompraEstoquePreco()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RB1", "ROLLBACK ANTES");

        try
        {
            PurchaseService.TestBeforeApplySalePrice = () =>
                throw new InvalidOperationException("falha controlada antes do sale_price");
            var ex = Assert.Throws<InvalidOperationException>(() =>
                CreateClosed(supplier, productId, "ROLLBACK ANTES", 4, 5.50, 9, true));
            Assert.Contains("falha controlada antes", ex.Message);
        }
        finally
        {
            PurchaseService.TestBeforeApplySalePrice = null;
        }

        Assert.Equal(8, ProductService.GetById(productId)!.SalePrice);
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountPurchases());
    }

    [Fact]
    public void FalhaDepoisDoUpdate_AntesDoCommit_RollbackPrecoEstoqueCompra()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RB2", "ROLLBACK DEPOIS");

        try
        {
            PurchaseService.TestAfterApplySalePrice = () =>
                throw new InvalidOperationException("falha controlada depois do sale_price");
            var ex = Assert.Throws<InvalidOperationException>(() =>
                CreateClosed(supplier, productId, "ROLLBACK DEPOIS", 4, 5.50, 9, true));
            Assert.Contains("falha controlada depois", ex.Message);
        }
        finally
        {
            PurchaseService.TestAfterApplySalePrice = null;
        }

        Assert.Equal(8, ProductService.GetById(productId)!.SalePrice);
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountPurchases());
    }

    [Fact]
    public void Sucesso_CommitaPrecoEstoqueECompra()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "OK1", "SUCESSO");

        var purchaseId = CreateClosed(supplier, productId, "SUCESSO", 3, 5.50, 9, true);

        Assert.True(purchaseId > 0);
        Assert.Equal(9, ProductService.GetById(productId)!.SalePrice);
        Assert.Equal(13, TestDataHelper.GetProductStock(productId));
        Assert.Equal(1, CountPurchases());
        Assert.Equal(5.50, GetPurchaseUnitPrice(purchaseId));
    }

    [Fact]
    public void AlterarVenda_NaoAlteraUnitPriceDaNf()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "UNP", "UNIT NF");

        var purchaseId = CreateClosed(supplier, productId, "UNIT NF", 1, 5.50, 9, true);

        Assert.Equal(5.50, GetPurchaseUnitPrice(purchaseId));
        Assert.Equal(9, ProductService.GetById(productId)!.SalePrice);
    }

    [Fact]
    public void Dto_SerializaSalePriceEUpdateSalePrice()
    {
        var input = new PurchaseInput
        {
            SupplierId = 1,
            EmissionDate = "2026-08-22",
            EntryDate = "2026-08-22",
            Number = "1",
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = 7,
                    ProductName = "X",
                    Quantity = 1,
                    UnitPrice = 5.5,
                    SalePrice = 9,
                    UpdateSalePrice = true,
                },
            ],
        };

        var json = JsonSerializer.Serialize(input, NetworkJson);
        Assert.Contains("\"salePrice\":9", json);
        Assert.Contains("\"updateSalePrice\":true", json);

        var back = JsonSerializer.Deserialize<PurchaseInput>(json, NetworkJson);
        Assert.NotNull(back);
        var item = Assert.Single(back!.Items);
        Assert.Equal(9, item.SalePrice);
        Assert.True(item.UpdateSalePrice);
    }

    [Fact]
    public void HostAntigo_IgnorandoCampos_NaoESucessoSilencioso()
    {
        var input = new PurchaseInput
        {
            SupplierId = 1,
            EmissionDate = "2026-08-22",
            EntryDate = "2026-08-22",
            Number = "1",
            Items = [Item(1, "X", 1, 5, 9, true)],
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PurchaseSalePriceRules.EnsureHostAppliedSalePrices(input, closeOnSave: true, salePriceUpdates: null));
        Assert.Equal(PurchaseSalePriceRules.HostNeedsUpdateMessage, ex.Message);
    }

    [Fact]
    public void Host_NaoAplicaQuandoUpdateFalse()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "HST", "HOST FALSE");

        PurchaseService.CreateLocal(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-HF",
            GerarEstoque = true,
            Items = [Item(productId, "HOST FALSE", 1, 5.50, 9, false)],
        }, closeOnSave: true);

        Assert.Equal(8, ProductService.GetById(productId)!.SalePrice);
        Assert.Equal(0, PurchaseSalePriceRules.CountRequestedSaleUpdates(
            [Item(productId, "HOST FALSE", 1, 5.50, 9, false)]));
    }

    [Fact]
    public void Host_AplicaNaMesmaTx_CreateLocal()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "HTA", "HOST APLICA");

        PurchaseService.CreateLocal(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-HA",
            GerarEstoque = true,
            Items = [Item(productId, "HOST APLICA", 1, 5.50, 9, true)],
        }, closeOnSave: true);

        Assert.Equal(9, ProductService.GetById(productId)!.SalePrice);
    }

    [Fact]
    public void Audit_SourceCompra_ComPurchaseId_UsuarioLocal()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "AUD", "AUDIT PRECO");

        var purchaseId = CreateClosed(supplier, productId, "AUDIT PRECO", 1, 5.50, 9, true);

        var row = AuditService.List(new AuditQuery { Limit = 50 })
            .First(r => r.Entity == "produto" && r.Action == "alterar"
                        && r.Details.Contains("preco_venda", StringComparison.Ordinal));
        Assert.Equal("admin_teste", row.UserLogin);
        Assert.True(AuditPayloadBuilder.TryParse(row.Details, out var doc));
        var payload = doc.Payload;
        Assert.Equal("compra", payload.GetProperty("source").GetString());
        Assert.Equal(purchaseId, payload.GetProperty("purchase_id").GetInt32());
        var change = payload.GetProperty("changes").GetProperty("preco_venda");
        Assert.Equal(8, change.GetProperty("de").GetDouble());
        Assert.Equal(9, change.GetProperty("para").GetDouble());
    }

    [Fact]
    public void XmlAudit_ComNfeKey_MarcaNfeXml()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "XML", "XML AUDIT");

        var purchaseId = PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "3526",
            NfeKey = "35260800000000000000000000000000000000000000",
            GerarEstoque = true,
            Items = [Item(productId, "XML AUDIT", 1, 5.50, 8, false)],
        }, closeOnSave: true);

        var row = AuditService.List(new AuditQuery { Limit = 50 })
            .First(r => r.Entity == "compra" && r.EntityId == purchaseId.ToString());
        Assert.True(AuditPayloadBuilder.TryParse(row.Details, out var doc));
        Assert.Equal("nfe_xml", doc.Payload.GetProperty("source").GetString());
    }

    [Fact]
    public void Pdv_UsaSalePriceNovo_NaoAlteraVendaHistorica()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(50, 8, 5, "PDV", "PDV PRECO");
        var before = ProductService.GetById(productId)!;
        var scanBefore = PdvService.ResolveManualSale(before);
        Assert.Equal(8, scanBefore.UnitPrice);

        CreateClosed(supplier, productId, "PDV PRECO", 1, 5.50, 9, true);

        var after = ProductService.GetById(productId)!;
        var scanAfter = PdvService.ResolveManualSale(after);
        Assert.Equal(9, scanAfter.UnitPrice);
        Assert.Equal(8, scanBefore.UnitPrice);
    }

    [Fact]
    public void Cigarro_MacoAvulsoFator_PreservadosSemUpdate()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var cig = SeedCigarette(stock: 200, sale: 10, cost: 8, fator: 20, avulso: 1.50, atacado: 10);

        CreateClosed(supplier, cig, "Rothmans Blue", 20, 0.40, 10, false, "NF-CIG");

        var product = ProductService.GetById(cig)!;
        var extra = ProductExtra.Parse(product.ExtraJson);
        Assert.Equal(10, product.SalePrice);
        Assert.Equal(1.50, extra.PrecoAvulso);
        Assert.Equal(10, extra.PrecoAtacado);
        Assert.Equal(20, extra.FatorEmbalagem);

        var maco = PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Maco);
        var avulso = PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Avulso);
        Assert.Equal(10, maco.UnitPrice);
        Assert.Equal(1.50, avulso.UnitPrice);
    }

    [Fact]
    public void Cigarro_UpdateMaco_NaoMudaFatorNemAvulso()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var cig = SeedCigarette(stock: 200, sale: 10, cost: 8, fator: 20, avulso: 1.50, atacado: 10);

        CreateClosed(supplier, cig, "Rothmans Blue", 20, 0.40, 11, true, "NF-CIG2");

        var product = ProductService.GetById(cig)!;
        var extra = ProductExtra.Parse(product.ExtraJson);
        Assert.Equal(11, product.SalePrice);
        Assert.Equal(11, extra.PrecoAtacado);
        Assert.Equal(1.50, extra.PrecoAvulso);
        Assert.Equal(20, extra.FatorEmbalagem);
    }

    [Fact]
    public void DesmarcarNoInput_NaoGravaMesmoComSalePrice9()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "DS", "DESMARCA");

        CreateClosed(supplier, productId, "DESMARCA", 1, 5.50, 9, false);

        Assert.Equal(8, ProductService.GetById(productId)!.SalePrice);
    }

    [Fact]
    public void Compat_HostDevolveContagem_Aceita()
    {
        var input = new PurchaseInput
        {
            SupplierId = 1,
            EmissionDate = "2026-08-22",
            EntryDate = "2026-08-22",
            Number = "1",
            Items = [Item(1, "X", 1, 5, 9, true)],
        };
        PurchaseSalePriceRules.EnsureHostAppliedSalePrices(input, closeOnSave: true, salePriceUpdates: 1);
    }

    private static int CreateClosed(
        int supplierId,
        int productId,
        string name,
        double qty,
        double unit,
        double sale,
        bool updateSale,
        string number = "NF-69DB")
    {
        return PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = number,
            GerarEstoque = true,
            Items = [Item(productId, name, qty, unit, sale, updateSale)],
        }, closeOnSave: true);
    }

    private static PurchaseItemInput Item(
        int productId, string name, double qty, double unit, double sale, bool update) =>
        new()
        {
            ProductId = productId,
            ProductName = name,
            Quantity = qty,
            UnitPrice = unit,
            SalePrice = sale,
            UpdateSalePrice = update,
        };

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN 69DB', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCigarette(
        double stock, double sale, double cost, double fator, double avulso, double atacado)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoAvulso = avulso,
            PrecoAtacado = atacado,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CIG69DB', 'Rothmans Blue', 'Cigarros', 'UN', $sale, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$sale", sale);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountPurchases()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM purchases;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double GetPurchaseUnitPrice(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unit_price FROM purchase_items WHERE purchase_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }
}
