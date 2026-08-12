using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 38 — Caracterização do scan/lookup do PDV (comportamento atual do PdvService).
/// Não altera produção.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvProductLookupCharacterizationTests
{
    private static void BeginStandalone()
    {
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
    }

    // ── 3. ResolveScan — produto simples ─────────────────────────────

    [Fact]
    public void ResolveScan_ProdutoSimples_PorBarcode()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedProduct(
            code: "SIMP01",
            name: "Refrigerante Lata",
            barcode: "7891000100103",
            salePrice: 4.50,
            stock: 100);

        var scan = PdvService.ResolveScan("7891000100103");
        Assert.NotNull(scan);
        Assert.Equal(id, scan.Product.Id);
        Assert.Equal(1, scan.Quantity);
        Assert.Equal(4.50, scan.UnitPrice);
        Assert.Equal(1, scan.StockUnitsPerSale);
        Assert.False(scan.IsPackSale);
        Assert.Null(scan.ModeLabel);
        Assert.Equal(1, PdvService.StockQuantityForSale(scan.Quantity, scan.StockUnitsPerSale));
    }

    // ── 4. ResolveScan — código interno ──────────────────────────────

    [Fact]
    public void ResolveScan_CodigoInterno()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedProduct(
            code: "INT99",
            name: "Produto Codigo Interno",
            barcode: null,
            salePrice: 12.30,
            stock: 40);

        var scan = PdvService.ResolveScan("INT99");
        Assert.NotNull(scan);
        Assert.Equal(id, scan.Product.Id);
        Assert.Equal(1, scan.Quantity);
        Assert.Equal(12.30, scan.UnitPrice);
        Assert.Equal(1, scan.StockUnitsPerSale);
    }

    // ── 5. Produto inexistente ───────────────────────────────────────

    [Fact]
    public void ResolveScan_Inexistente_RetornaNull()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        Assert.Null(PdvService.ResolveScan("ZZZ_INEXISTENTE_999"));
        Assert.Null(PdvService.FindProduct("ZZZ_INEXISTENTE_999"));
        Assert.Empty(PdvService.SearchProducts("ZZZ_INEXISTENTE_999"));
    }

    // ── 6. Produto inativo ───────────────────────────────────────────

    [Fact]
    public void Lookup_ProdutoInativo_IgnoradoNasTresAPIs()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        SeedProduct(
            code: "INAT1",
            name: "Produto Inativo Lookup",
            barcode: "7899999000001",
            salePrice: 9,
            stock: 10,
            active: false);

        Assert.Null(PdvService.ResolveScan("7899999000001"));
        Assert.Null(PdvService.ResolveScan("INAT1"));
        Assert.Null(PdvService.FindProduct("INAT1"));
        Assert.Empty(PdvService.SearchProducts("Inativo Lookup"));
        Assert.Empty(PdvService.SearchProducts("INAT1"));
        Assert.Empty(PdvService.SearchProducts("7899999000001"));
    }

    // ── 7–8. Cigarro / maço ──────────────────────────────────────────

    [Fact]
    public void ResolveScan_CigarroMaco_StockUnits20()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedCigarette(
            code: "CIG20",
            name: "Marlboro Box",
            barcode: "7891234000011",
            salePrice: 12.00, // >= 5
            fator: 20,
            precoAtacado: 12.00);

        var scan = PdvService.ResolveScan("7891234000011");
        Assert.NotNull(scan);
        Assert.Equal(id, scan.Product.Id);
        Assert.Equal(1, scan.Quantity);
        Assert.Equal(12.00, scan.UnitPrice);
        Assert.Equal(20, scan.StockUnitsPerSale);
        Assert.True(scan.IsPackSale);
        Assert.Equal("MAÇO", scan.ModeLabel);
        Assert.Equal(20, PdvService.StockQuantityForSale(scan.Quantity, scan.StockUnitsPerSale));
        Assert.Equal(20, ProductPriceCalculator.StockQuantityForSale(1, 20));
    }

    [Fact]
    public void ResolveManualSale_Cigarro_IgualAoScan()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedCigarette(
            code: "CIGM",
            name: "Cigarro Lucky Strike",
            barcode: "7891234000028",
            salePrice: 11.50,
            fator: 20,
            precoAtacado: 11.50);

        var product = LoadProduct(id)!;
        var manual = PdvService.ResolveManualSale(product);
        var scan = PdvService.ResolveScan("7891234000028");

        Assert.NotNull(scan);
        Assert.Equal(1, manual.Quantity);
        Assert.Equal(11.50, manual.UnitPrice);
        Assert.Equal(20, manual.StockUnitsPerSale);
        Assert.True(manual.IsPackSale);
        Assert.Equal("MAÇO", manual.ModeLabel);

        Assert.Equal(scan.Quantity, manual.Quantity);
        Assert.Equal(scan.UnitPrice, manual.UnitPrice);
        Assert.Equal(scan.StockUnitsPerSale, manual.StockUnitsPerSale);
        Assert.Equal(scan.IsPackSale, manual.IsPackSale);
        Assert.Equal(scan.ModeLabel, manual.ModeLabel);
        Assert.Equal(20, PdvService.StockQuantityForSale(manual.Quantity, manual.StockUnitsPerSale));
    }

    // ── 9. Barcode unidade × embalagem ───────────────────────────────

    [Fact]
    public void ResolveScan_BarcodeUnidadeVsEmbalagem_NaoCigarro()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        // packTotal = PrecoAtacado 24 → unit = 24/12 = 2
        var id = SeedPackProduct(
            code: "REF12",
            name: "Refrigerante CX 12",
            group: "Bebidas",
            unitBarcode: "7891000111111",
            packBarcode: "7891000222222",
            salePrice: 2.50,
            fator: 12,
            precoAtacado: 24.00,
            qtdAtacado: 12);

        var unit = PdvService.ResolveScan("7891000111111");
        Assert.NotNull(unit);
        Assert.Equal(id, unit.Product.Id);
        Assert.Equal(1, unit.Quantity);
        Assert.Equal(2.50, unit.UnitPrice);
        Assert.Equal(1, unit.StockUnitsPerSale);
        Assert.False(unit.IsPackSale);

        var pack = PdvService.ResolveScan("7891000222222");
        Assert.NotNull(pack);
        Assert.Equal(id, pack.Product.Id);
        Assert.Equal(12, pack.Quantity);
        Assert.Equal(2.00, pack.UnitPrice); // 24/12
        Assert.Equal(1, pack.StockUnitsPerSale);
        Assert.True(pack.IsPackSale);
        Assert.Equal("MAÇO/CX", pack.ModeLabel);
        Assert.Equal(12, PdvService.StockQuantityForSale(pack.Quantity, pack.StockUnitsPerSale));
    }

    // ── 10–11. Caixa não-cigarro: scan vs manual ─────────────────────

    [Fact]
    public void ResolveManualSale_EmbalagemNaoCigarro_DivergeDoScanPack()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedPackProduct(
            code: "AGUA6",
            name: "Agua Mineral CX",
            group: "Bebidas",
            unitBarcode: "7892000111118",
            packBarcode: "7892000222225",
            salePrice: 1.80,
            fator: 6,
            precoAtacado: 9.00,
            qtdAtacado: 6);

        var packScan = PdvService.ResolveScan("7892000222225");
        Assert.NotNull(packScan);
        Assert.Equal(6, packScan.Quantity);
        Assert.Equal(1.50, packScan.UnitPrice); // 9/6
        Assert.Equal(1, packScan.StockUnitsPerSale);
        Assert.True(packScan.IsPackSale);

        var manual = PdvService.ResolveManualSale(LoadProduct(id)!);
        // Manual NÃO aplica ramo isPackScan — cai no avulso.
        Assert.Equal(1, manual.Quantity);
        Assert.Equal(1.80, manual.UnitPrice);
        Assert.Equal(1, manual.StockUnitsPerSale);
        Assert.False(manual.IsPackSale);
        Assert.Null(manual.ModeLabel);

        Assert.NotEqual(packScan.Quantity, manual.Quantity);
        Assert.NotEqual(packScan.UnitPrice, manual.UnitPrice);
        Assert.NotEqual(packScan.IsPackSale, manual.IsPackSale);
    }

    // ── 12–14. UnitPriceForQuantity ──────────────────────────────────

    [Fact]
    public void UnitPriceForQuantity_AbaixoDoAtacado_PrecoNormal()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedPackProduct(
            code: "ATAC1",
            name: "Item Atacado",
            group: "Geral",
            unitBarcode: "7893000111115",
            packBarcode: null,
            salePrice: 10.00,
            fator: 1,
            precoAtacado: 8.00, // unitário (<= sale)
            qtdAtacado: 6);

        var p = LoadProduct(id)!;
        Assert.Equal(10.00, PdvService.UnitPriceForQuantity(p, 5));
        Assert.Equal(10.00, PdvService.UnitPriceForQuantity(p, 1));
    }

    [Fact]
    public void UnitPriceForQuantity_AtingindoAtacado()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedPackProduct(
            code: "ATAC2",
            name: "Item Atacado Limite",
            group: "Geral",
            unitBarcode: "7893000222222",
            packBarcode: null,
            salePrice: 10.00,
            fator: 1,
            precoAtacado: 8.00,
            qtdAtacado: 6);

        var p = LoadProduct(id)!;
        Assert.Equal(10.00, PdvService.UnitPriceForQuantity(p, 5));
        Assert.Equal(8.00, PdvService.UnitPriceForQuantity(p, 6));
        Assert.Equal(8.00, PdvService.UnitPriceForQuantity(p, 7));
    }

    [Fact]
    public void UnitPriceForQuantity_QtyIgualAoFatorEmbalagem()
    {
        // Sem QtdAtacado (>=2), mas fator=12 e qty=12 → aplica WholesaleUnitPrice.
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedPackProduct(
            code: "FAT12",
            name: "Fardo Sem QtdAtacado",
            group: "Geral",
            unitBarcode: "7893000333339",
            packBarcode: null,
            salePrice: 3.00,
            fator: 12,
            precoAtacado: 30.00, // total do lote > sale → 30/12 = 2.50
            qtdAtacado: 0);

        var p = LoadProduct(id)!;
        Assert.Equal(3.00, PdvService.UnitPriceForQuantity(p, 11));
        Assert.Equal(2.50, PdvService.UnitPriceForQuantity(p, 12));
        Assert.Equal(3.00, PdvService.UnitPriceForQuantity(p, 13));
    }

    // ── 15. FindProduct ──────────────────────────────────────────────

    [Fact]
    public void FindProduct_EspelhaProductDeResolveScan()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedProduct(
            code: "FIND1",
            name: "Achado FindProduct",
            barcode: "7894000111112",
            salePrice: 7.25,
            stock: 15);

        var byBc = PdvService.FindProduct("7894000111112");
        var scanBc = PdvService.ResolveScan("7894000111112");
        Assert.NotNull(byBc);
        Assert.NotNull(scanBc);
        Assert.Equal(scanBc.Product.Id, byBc.Id);
        Assert.Equal(id, byBc.Id);

        var byCode = PdvService.FindProduct("FIND1");
        var scanCode = PdvService.ResolveScan("FIND1");
        Assert.NotNull(byCode);
        Assert.NotNull(scanCode);
        Assert.Equal(scanCode.Product.Id, byCode.Id);

        Assert.Null(PdvService.FindProduct("NAO_EXISTE_XYZ"));
    }

    // ── 16. SearchProducts ───────────────────────────────────────────

    [Fact]
    public void SearchProducts_NomeCodigoBarcode_OrdenacaoLimit_EInativo()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        SeedProduct("Z999", "Zebra Produto", "7895000111119", 5, 10);
        SeedProduct("A001", "Alpha Produto", "7895000222226", 6, 10);
        SeedProduct("B002", "Beta Produto", "7895000333333", 7, 10);
        SeedProduct("INAT", "Alpha Inativo", "7895000444440", 8, 10, active: false);

        var byName = PdvService.SearchProducts("Produto", limit: 10);
        Assert.Equal(3, byName.Count); // inativo fora
        Assert.Equal("Alpha Produto", byName[0].Name);
        Assert.Equal("Beta Produto", byName[1].Name);
        Assert.Equal("Zebra Produto", byName[2].Name);

        var byCode = PdvService.SearchProducts("A001");
        Assert.Single(byCode);
        Assert.Equal("A001", byCode[0].Code);

        var byBc = PdvService.SearchProducts("7895000222226");
        Assert.Single(byBc);
        Assert.Equal("Alpha Produto", byBc[0].Name);

        var limited = PdvService.SearchProducts("Produto", limit: 2);
        Assert.Equal(2, limited.Count);
        Assert.Equal("Alpha Produto", limited[0].Name);
        Assert.Equal("Beta Produto", limited[1].Name);

        Assert.Empty(PdvService.SearchProducts("Alpha Inativo"));
    }

    // ── 17. Rede Loja Client — lookup local ──────────────────────────

    [Fact]
    public void ClientMode_LookupContinuaNoSqliteLocal()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedProduct(
            code: "CLI01",
            name: "Produto Client Local",
            barcode: "7896000111116",
            salePrice: 5.55,
            stock: 8);

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            Assert.True(StoreNetworkMode.IsClient);

            // Sem servidor: se usasse StoreNetworkClient falharia/não acharia.
            // Comportamento atual: SQL local.
            var search = PdvService.SearchProducts("Client Local");
            Assert.Single(search);
            Assert.Equal(id, search[0].Id);

            var scan = PdvService.ResolveScan("7896000111116");
            Assert.NotNull(scan);
            Assert.Equal(id, scan.Product.Id);

            var found = PdvService.FindProduct("CLI01");
            Assert.NotNull(found);
            Assert.Equal(id, found.Id);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static int SeedProduct(
        string code, string name, string? barcode, double salePrice, double stock,
        bool active = true, string? group = null, string? extraJson = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, barcode, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $bc, $name, $group, 'UN', $sale, $stock, $cost, $active, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$bc", (object?)barcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$group", (object?)group ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sale", salePrice);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", salePrice * 0.5);
        cmd.Parameters.AddWithValue("$active", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$extra", extraJson ?? "{}");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCigarette(
        string code, string name, string barcode, double salePrice, double fator, double precoAtacado)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            PrecoAtacado = precoAtacado,
            QtdAtacado = fator,
        }.ToJson();
        return SeedProduct(code, name, barcode, salePrice, stock: 200, group: "Cigarros", extraJson: extra);
    }

    private static int SeedPackProduct(
        string code, string name, string group, string unitBarcode, string? packBarcode,
        double salePrice, double fator, double precoAtacado, double qtdAtacado)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            PrecoAtacado = precoAtacado,
            QtdAtacado = qtdAtacado,
            BarcodeEmbalagem = packBarcode,
        }.ToJson();
        return SeedProduct(code, name, unitBarcode, salePrice, stock: 100, group: group, extraJson: extra);
    }

    private static Product? LoadProduct(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at,
                   IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new Product
        {
            Id = r.GetInt32(0),
            Code = r.IsDBNull(1) ? null : r.GetString(1),
            Barcode = r.IsDBNull(2) ? null : r.GetString(2),
            Name = r.GetString(3),
            GroupName = r.IsDBNull(4) ? null : r.GetString(4),
            Unit = r.GetString(5),
            CostPrice = r.GetDouble(6),
            SalePrice = r.GetDouble(7),
            MinStock = r.GetInt32(8),
            Stock = r.GetDouble(9),
            Location = r.IsDBNull(10) ? null : r.GetString(10),
            ExtraJson = r.IsDBNull(11) ? "{}" : r.GetString(11),
            Active = r.GetInt32(12) != 0,
            CreatedAt = r.IsDBNull(13) ? "" : r.GetString(13)!,
            StockFridge = r.GetDouble(14),
            StockFridgeMin = Convert.ToInt32(r.GetValue(15)),
        };
    }
}
