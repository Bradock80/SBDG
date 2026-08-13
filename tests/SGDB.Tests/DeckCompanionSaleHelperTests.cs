using System.Text.Json;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 43A — Companion: resolução no Host (sem confiar em UnitPrice do cliente).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class DeckCompanionSaleHelperTests
{
    private static void BeginStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    [Fact]
    public void MapProduct_Comum_AllowsAvulsoFalse()
    {
        var p = new Product
        {
            Id = 1,
            Name = "Agua 500ml",
            GroupName = "Bebidas",
            SalePrice = 3,
            ExtraJson = "{}",
            Unit = "UN",
        };
        var json = JsonSerializer.Serialize(DeckCompanionSaleHelper.MapProductForApi(p));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("allowsAvulso").GetBoolean());
        Assert.Equal(0, root.GetProperty("precoAvulso").GetDouble());
    }

    [Fact]
    public void MapProduct_CigarroComAvulso_FlagsEPrecos()
    {
        var p = MakeRothmansProduct();
        var json = JsonSerializer.Serialize(DeckCompanionSaleHelper.MapProductForApi(p));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("allowsAvulso").GetBoolean());
        Assert.Equal(1.50, root.GetProperty("precoAvulso").GetDouble());
        Assert.Equal(28.50, root.GetProperty("precoMaco").GetDouble());
    }

    [Fact]
    public void Add_ProdutoComum_SemMode()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var pid = TestDataHelper.SeedSimpleProduct(40, 4, 2, "AG1", "Agua");
        var tab = OpenTabService.Create("Comp Comum");
        var item = DeckCompanionSaleHelper.AddByProductId(tab, pid, 2, mode: null);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(4, item.UnitPrice);
        Assert.Equal(1, item.StockUnitsPerSale);
    }

    [Fact]
    public void Add_CigarroSemMode_EntraMaco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab();
        var item = DeckCompanionSaleHelper.AddByProductId(tab, id, 1, mode: null);
        Assert.Equal(28.50, item.UnitPrice);
        Assert.Equal(20, item.StockUnitsPerSale);
        Assert.Contains("MAÇO", item.ProductName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Add_CigarroSemPrecoAvulso_SemMode_Maco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab(precoAvulso: 0);
        var item = DeckCompanionSaleHelper.AddByProductId(tab, id, 1, mode: null);
        Assert.Equal(28.50, item.UnitPrice);
        Assert.Equal(20, item.StockUnitsPerSale);
    }

    [Fact]
    public void Add_Avulso_E_Maco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab();

        var a = DeckCompanionSaleHelper.AddByProductId(tab, id, 1, PdvCigaretteSaleMode.Avulso);
        Assert.Equal(1.50, a.UnitPrice);
        Assert.Equal(1, a.StockUnitsPerSale);
        Assert.Contains("AVULSO", a.ProductName, StringComparison.OrdinalIgnoreCase);

        var m = DeckCompanionSaleHelper.AddByProductId(tab, id, 1, PdvCigaretteSaleMode.Maco);
        Assert.Equal(28.50, m.UnitPrice);
        Assert.Equal(20, m.StockUnitsPerSale);

        Assert.Equal(2, OpenTabService.Get(tab).Items.Count);
    }

    [Fact]
    public void Add_AvulsoQty5_MacoQty2()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab();

        var a = DeckCompanionSaleHelper.AddByProductId(tab, id, 5, PdvCigaretteSaleMode.Avulso);
        Assert.Equal(5, a.Quantity);
        Assert.Equal(1, a.StockUnitsPerSale);
        Assert.Equal(7.50, a.Subtotal);

        var m = DeckCompanionSaleHelper.AddByProductId(tab, id, 2, PdvCigaretteSaleMode.Maco);
        Assert.Equal(2, m.Quantity);
        Assert.Equal(20, m.StockUnitsPerSale);
        Assert.Equal(40, m.Quantity * m.StockUnitsPerSale);
    }

    [Fact]
    public void Add_DoisAvulsos_MergePreservaPreco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab();
        DeckCompanionSaleHelper.AddByProductId(tab, id, 1, PdvCigaretteSaleMode.Avulso);
        DeckCompanionSaleHelper.AddByProductId(tab, id, 1, PdvCigaretteSaleMode.Avulso);
        var items = OpenTabService.Get(tab).Items;
        Assert.Single(items);
        Assert.Equal(2, items[0].Quantity);
        Assert.Equal(1.50, items[0].UnitPrice);
    }

    [Fact]
    public void Add_ModeInvalido_Erro()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab();
        Assert.Throws<OpenTabException>(() =>
            DeckCompanionSaleHelper.AddByProductId(tab, id, 1, "ABC"));
        Assert.Empty(OpenTabService.Get(tab).Items);
    }

    [Fact]
    public void Add_AvulsoSemPreco_Erro()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab(precoAvulso: 0);
        Assert.Throws<OpenTabException>(() =>
            DeckCompanionSaleHelper.AddByProductId(tab, id, 1, PdvCigaretteSaleMode.Avulso));
        Assert.Empty(OpenTabService.Get(tab).Items);
    }

    [Fact]
    public void Add_ComumComMode_IgnoraMode()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var pid = TestDataHelper.SeedSimpleProduct(20, 5, 2, "CX1", "Refrigerante");
        var tab = OpenTabService.Create("Comp Mode Ignorado");
        var item = DeckCompanionSaleHelper.AddByProductId(tab, pid, 1, PdvCigaretteSaleMode.Avulso);
        Assert.Equal(5, item.UnitPrice);
        Assert.Equal(1, item.StockUnitsPerSale);
    }

    [Fact]
    public void Settle_MacoETresAvulsos_ViaHelper()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        CashService.OpenSession(50, "comp-cig");
        var (tab, id) = SeedRothmansTab(stock: 100);

        DeckCompanionSaleHelper.AddByProductId(tab, id, 1, PdvCigaretteSaleMode.Maco);
        DeckCompanionSaleHelper.AddByProductId(tab, id, 3, PdvCigaretteSaleMode.Avulso);

        var lines = OpenTabService.ToCartLines(tab).ToList();
        Assert.Equal(23, lines.Sum(l => l.StockQuantity));

        OpenTabSettlementService.SettleOpenTab(tab, new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Dinheiro",
            CashReceived = 33,
        });
        Assert.Equal(77, TestDataHelper.GetProductStock(id));
    }

    // ── ETAPA 53B — term / scan → modeRequired ───────────────────────

    [Fact]
    public void Term_ProdutoComum_NaoPedeMode_AddFromScan()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var pid = TestDataHelper.SeedSimpleProduct(40, 4, 2, "COCA1", "Coca-Cola");
        SetBarcode(pid, "7891000100103");
        var tab = OpenTabService.Create("Term Comum");

        Assert.Null(DeckCompanionSaleHelper.TryGetModeRequiredForTerm("7891000100103", 2));
        var item = OpenTabService.AddFromScan(tab, "7891000100103", 2);
        Assert.Equal(pid, item.ProductId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(4, item.UnitPrice);
        Assert.Equal(1, item.StockUnitsPerSale);
    }

    [Fact]
    public void Term_BarcodeEmbalagemComum_NaoIntercepta_PreservaPackScan()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedPackProduct(
            code: "REF12",
            name: "Refrigerante CX 12",
            group: "Bebidas",
            unitBarcode: "7891000111111",
            packBarcode: "7891000222222",
            salePrice: 2.50,
            fator: 12,
            precoAtacado: 24.00);

        Assert.Null(DeckCompanionSaleHelper.TryGetModeRequiredForTerm("7891000222222", 1));

        var tab = OpenTabService.Create("Term Pack");
        var item = OpenTabService.AddFromScan(tab, "7891000222222", 1);
        Assert.Equal(id, item.ProductId);
        Assert.Equal(12, item.Quantity); // qty scan * 1
        Assert.Equal(2.00, item.UnitPrice);
        Assert.Equal(1, item.StockUnitsPerSale);
    }

    [Fact]
    public void Term_CigarroSemPrecoAvulso_NaoPedeMode_AddFromScanMaco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab(precoAvulso: 0);
        SetBarcode(id, "7895555000099");

        Assert.Null(DeckCompanionSaleHelper.TryGetModeRequiredForTerm("7895555000099", 1));
        var item = OpenTabService.AddFromScan(tab, "7895555000099", 1);
        Assert.Equal(28.50, item.UnitPrice);
        Assert.Equal(20, item.StockUnitsPerSale);
    }

    [Fact]
    public void Term_CigarroComAvulso_ModeRequired_NaoInsere()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab();
        SetBarcode(id, "7895555000011");

        var req = DeckCompanionSaleHelper.TryGetModeRequiredForTerm("7895555000011", 5);
        Assert.NotNull(req);
        Assert.Equal(id, req.ProductId);
        Assert.Equal(5, req.Qty);
        Assert.Equal(1.50, req.PrecoAvulso);
        Assert.Equal(28.50, req.PrecoMaco);
        Assert.True(req.AllowsAvulso);
        Assert.Contains("Rothmans", req.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(OpenTabService.Get(tab).Items);
    }

    [Fact]
    public void Term_ModeRequired_DepoisAvulso_QtyPreservada()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab();
        SetBarcode(id, "7895555000011");

        var req = DeckCompanionSaleHelper.TryGetModeRequiredForTerm("7895555000011", 5);
        Assert.NotNull(req);
        var item = DeckCompanionSaleHelper.AddByProductId(
            tab, req.ProductId, req.Qty, PdvCigaretteSaleMode.Avulso);
        Assert.Equal(5, item.Quantity);
        Assert.Equal(1.50, item.UnitPrice);
        Assert.Equal(1, item.StockUnitsPerSale);
        Assert.Contains("AVULSO", item.ProductName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, item.Quantity * item.StockUnitsPerSale);
    }

    [Fact]
    public void Term_ModeRequired_DepoisMaco_QtyPreservada()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tab, id) = SeedRothmansTab();
        SetBarcode(id, "7895555000011");

        var req = DeckCompanionSaleHelper.TryGetModeRequiredForTerm("7895555000011", 2);
        Assert.NotNull(req);
        var item = DeckCompanionSaleHelper.AddByProductId(
            tab, req.ProductId, req.Qty, PdvCigaretteSaleMode.Maco);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(28.50, item.UnitPrice);
        Assert.Equal(20, item.StockUnitsPerSale);
        Assert.Contains("MAÇO", item.ProductName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(40, item.Quantity * item.StockUnitsPerSale);
    }

    [Fact]
    public void Term_Inexistente_ModeRequiredNull_AddFromScanErro()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var tab = OpenTabService.Create("Term Miss");
        Assert.Null(DeckCompanionSaleHelper.TryGetModeRequiredForTerm("ZZZ_INEXISTENTE_999", 1));
        Assert.Throws<OpenTabException>(() =>
            OpenTabService.AddFromScan(tab, "ZZZ_INEXISTENTE_999", 1));
        Assert.Empty(OpenTabService.Get(tab).Items);
    }

    private static Product MakeRothmansProduct(double precoAvulso = 1.50) =>
        new()
        {
            Id = 9,
            Name = "Rothmans Blue",
            GroupName = "Cigarros",
            SalePrice = 28.50,
            Unit = "UN",
            ExtraJson = new ProductExtra
            {
                FatorEmbalagem = 20,
                QtdAtacado = 20,
                PrecoAtacado = 28.50,
                PrecoAvulso = precoAvulso,
            }.ToJson(),
        };

    private static (int TabId, int ProductId) SeedRothmansTab(
        double stock = 200, double precoAvulso = 1.50)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = 20,
            QtdAtacado = 20,
            PrecoAtacado = 28.50,
            PrecoAvulso = precoAvulso,
        }.ToJson();
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'ROTHC', 'Rothmans Blue', 'Cigarros', 'UN', 28.5, $stock, 24, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return (OpenTabService.Create("Comp Cigarro"), id);
    }

    private static void SetBarcode(int productId, string barcode)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET barcode = $bc WHERE id = $id;";
        cmd.Parameters.AddWithValue("$bc", barcode);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static int SeedPackProduct(
        string code, string name, string group,
        string unitBarcode, string packBarcode,
        double salePrice, double fator, double precoAtacado)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoAtacado = precoAtacado,
            BarcodeEmbalagem = packBarcode,
        }.ToJson();
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, barcode, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $bc, $name, $grp, 'UN', $sale, 100, 1, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$bc", unitBarcode);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$grp", group);
        cmd.Parameters.AddWithValue("$sale", salePrice);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
