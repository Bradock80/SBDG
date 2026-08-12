using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 40 — Modalidade AVULSO / MAÇO (contrato + resolução; sem UI PDV).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvCigaretteSaleModeTests
{
    private static void BeginStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    // ── ProductExtra.preco_avulso ────────────────────────────────────

    [Fact]
    public void ProductExtra_SemPrecoAvulso_DefaultZero()
    {
        var extra = ProductExtra.Parse("""{"fator_embalagem":20,"preco_atacado":28.5}""");
        Assert.Equal(0, extra.PrecoAvulso);
        Assert.False(PdvService.AllowsCigaretteAvulso(extra));
    }

    [Fact]
    public void ProductExtra_ComPrecoAvulso_RoundTrip()
    {
        var original = new ProductExtra
        {
            FatorEmbalagem = 20,
            PrecoAtacado = 28.50,
            PrecoAvulso = 1.50,
        };
        var json = original.ToJson();
        Assert.Contains("preco_avulso", json, StringComparison.Ordinal);

        var parsed = ProductExtra.Parse(json);
        Assert.Equal(1.50, parsed.PrecoAvulso);
        Assert.Equal(28.50, parsed.PrecoAtacado);
        Assert.True(PdvService.AllowsCigaretteAvulso(parsed));
    }

    [Fact]
    public void ProductExtra_PrecoAvulsoZero_NaoPermiteAvulso()
    {
        var extra = ProductExtra.Parse("""{"preco_avulso":0,"fator_embalagem":20}""");
        Assert.Equal(0, extra.PrecoAvulso);
        Assert.False(PdvService.AllowsCigaretteAvulso(extra));
    }

    // ── Resolve AVULSO / MAÇO ────────────────────────────────────────

    [Fact]
    public void ResolveCigaretteSale_Avulso()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var product = LoadProduct(SeedRothmans());

        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);
        Assert.Equal(1, scan.Quantity);
        Assert.Equal(1.50, scan.UnitPrice);
        Assert.Equal(1, scan.StockUnitsPerSale);
        Assert.Equal(PdvCigaretteSaleMode.Avulso, scan.ModeLabel);
        Assert.False(scan.IsPackSale);
        Assert.Equal(1, PdvService.StockQuantityForSale(scan.Quantity, scan.StockUnitsPerSale));
    }

    [Fact]
    public void ResolveCigaretteSale_Maco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var product = LoadProduct(SeedRothmans());

        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);
        Assert.Equal(1, scan.Quantity);
        Assert.Equal(28.50, scan.UnitPrice);
        Assert.Equal(20, scan.StockUnitsPerSale);
        Assert.Equal(PdvCigaretteSaleMode.Maco, scan.ModeLabel);
        Assert.True(scan.IsPackSale);
        Assert.Equal(20, PdvService.StockQuantityForSale(scan.Quantity, scan.StockUnitsPerSale));
    }

    [Fact]
    public void VinteAvulsos_StockQty20_NaoConverteEmMaco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var product = LoadProduct(SeedRothmans());
        var unit = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);

        const double commercialQty = 20;
        var stockQty = PdvService.StockQuantityForSale(commercialQty, unit.StockUnitsPerSale);
        var subtotal = Math.Round(commercialQty * unit.UnitPrice, 2, MidpointRounding.AwayFromZero);

        Assert.Equal(1, unit.StockUnitsPerSale);
        Assert.Equal(20, stockQty);
        Assert.Equal(30.00, subtotal); // 20 × 1,50 — comercialmente 20 avulsos, não 1 maço
        Assert.NotEqual(PdvCigaretteSaleMode.Maco, unit.ModeLabel);
    }

    [Fact]
    public void ResolveCigaretteSale_AvulsoSemPreco_Lanca()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var product = LoadProduct(SeedRothmans(precoAvulso: 0));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso));
        Assert.Contains("avulsa", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("28", ex.Message); // não vazou preço de maço
    }

    [Fact]
    public void ResolveManualSale_Default_ContinuaMaco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var product = LoadProduct(SeedRothmans());

        var legacy = PdvService.ResolveManualSale(product);
        Assert.Equal(PdvCigaretteSaleMode.Maco, legacy.ModeLabel);
        Assert.Equal(20, legacy.StockUnitsPerSale);
        Assert.Equal(28.50, legacy.UnitPrice);
    }

    [Fact]
    public void ResolveManualSale_Avulso_Explicito()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var product = LoadProduct(SeedRothmans());

        var scan = PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Avulso);
        Assert.Equal(PdvCigaretteSaleMode.Avulso, scan.ModeLabel);
        Assert.Equal(1, scan.StockUnitsPerSale);
        Assert.Equal(1.50, scan.UnitPrice);
    }

    [Fact]
    public void ResolveScan_Default_ContinuaMaco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        SeedRothmans(barcode: "7895555000011");

        var scan = PdvService.ResolveScan("7895555000011");
        Assert.NotNull(scan);
        Assert.Equal(PdvCigaretteSaleMode.Maco, scan.ModeLabel);
        Assert.Equal(20, scan.StockUnitsPerSale);
        Assert.Equal(28.50, scan.UnitPrice);
    }

    [Fact]
    public void ResolveScan_Avulso_Explicito()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        SeedRothmans(barcode: "7895555000028");

        var scan = PdvService.ResolveScan("7895555000028", PdvCigaretteSaleMode.Avulso);
        Assert.NotNull(scan);
        Assert.Equal(PdvCigaretteSaleMode.Avulso, scan.ModeLabel);
        Assert.Equal(1, scan.StockUnitsPerSale);
        Assert.Equal(1.50, scan.UnitPrice);
    }

    // ── Estoque / cancel ─────────────────────────────────────────────

    [Fact]
    public void FinalizeCancel_Avulso_BaixaERestaura1()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(50, "cig-avulso");

        var id = SeedRothmans(stock: 200, costPrice: 24);
        var product = LoadProduct(id);
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);

        var sale = FinalizeFromScan(scan, commercialQty: 1);
        Assert.Equal(199, TestDataHelper.GetProductStock(id));
        Assert.Equal(1, GetSaleItemStockQty(sale.SaleId));

        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(200, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void FinalizeCancel_Maco_BaixaERestaura20()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(50, "cig-maco");

        var id = SeedRothmans(stock: 200, costPrice: 24);
        var product = LoadProduct(id);
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);

        var sale = FinalizeFromScan(scan, commercialQty: 1);
        Assert.Equal(180, TestDataHelper.GetProductStock(id));
        Assert.Equal(20, GetSaleItemStockQty(sale.SaleId));

        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(200, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void EstoqueCompartilhado_MacoEAvulsos()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        CashService.OpenSession(50, "cig-share");

        var id = SeedRothmans(stock: 40, costPrice: 24);
        var product = LoadProduct(id);

        FinalizeFromScan(
            PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco),
            commercialQty: 1);
        Assert.Equal(20, TestDataHelper.GetProductStock(id));

        FinalizeFromScan(
            PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso),
            commercialQty: 3);
        Assert.Equal(17, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void Finalize_Avulso_SmokePagamentoDinheiro()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        CashService.OpenSession(50, "cig-pay");

        var id = SeedRothmans(stock: 50, costPrice: 24);
        var scan = PdvService.ResolveCigaretteSale(LoadProduct(id), PdvCigaretteSaleMode.Avulso);
        var sale = FinalizeFromScan(scan, commercialQty: 1);

        Assert.True(sale.SaleId > 0);
        Assert.Equal(1.50, sale.Total);
        Assert.Equal("Dinheiro", GetPaymentType(sale.SaleId));
    }

    // ── Custo / lucro ────────────────────────────────────────────────

    [Fact]
    public void UnitCost_Avulso_DivideCustoMacoPeloFator()
    {
        var extra = new ProductExtra { FatorEmbalagem = 20, PrecoAvulso = 1.50, PrecoAtacado = 28.50 };
        var unitCost = ProductPriceHelper.UnitCostForSoldLine(
            catalogCost: 24,
            soldUnitPrice: 1.50,
            extra,
            name: "Rothmans Blue",
            group: "Cigarros");

        Assert.Equal(1.20, unitCost);
    }

    [Fact]
    public void UnitCost_Maco_MantemCustoMaco()
    {
        var extra = new ProductExtra { FatorEmbalagem = 20, PrecoAvulso = 1.50, PrecoAtacado = 28.50 };
        var unitCost = ProductPriceHelper.UnitCostForSoldLine(
            catalogCost: 24,
            soldUnitPrice: 28.50,
            extra,
            name: "Rothmans Blue",
            group: "Cigarros");

        Assert.Equal(24, unitCost);
    }

    /// <summary>
    /// Swap futuro: ResolveManualSale(mode) recalcula StockUnitsPerSale (sem UI).
    /// </summary>
    [Fact]
    public void ResolveModes_AvulsoParaMaco_RecalculaStockUnits()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var product = LoadProduct(SeedRothmans());

        var avulso = PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Avulso);
        var maco = PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Maco);

        Assert.Equal(1, PdvService.StockQuantityForSale(1, avulso.StockUnitsPerSale));
        Assert.Equal(20, PdvService.StockQuantityForSale(1, maco.StockUnitsPerSale));
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static int SeedRothmans(
        double stock = 200,
        double costPrice = 24,
        double precoAvulso = 1.50,
        string? barcode = null)
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
                code, barcode, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $bc, $name, 'Cigarros', 'UN', $sale, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", "ROTH1");
        cmd.Parameters.AddWithValue("$bc", (object?)barcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", "Rothmans Blue");
        cmd.Parameters.AddWithValue("$sale", 28.50);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", costPrice);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static Product LoadProduct(int id)
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
        Assert.True(r.Read());
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

    private static PdvFinalizeResult FinalizeFromScan(PdvScanResult scan, double commercialQty)
    {
        var stockUnits = scan.StockUnitsPerSale;
        var unitPrice = scan.UnitPrice;
        var total = Math.Round(commercialQty * unitPrice, 2, MidpointRounding.AwayFromZero);
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = scan.Product.Id,
                    Code = scan.Product.Code ?? "",
                    Name = scan.Product.Name,
                    Unit = scan.Product.Unit,
                    Quantity = commercialQty,
                    UnitPrice = unitPrice,
                    StockUnitsPerSale = stockUnits,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = total,
        });
    }

    private static double GetSaleItemStockQty(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(stock_qty,0) FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static string GetPaymentType(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payment_type FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
}
