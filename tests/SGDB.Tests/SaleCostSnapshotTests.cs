using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69E-B1 — snapshot cost_at_sale nas vendas novas (sem DRE/legado).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class SaleCostSnapshotTests
{
    private static TempDatabase BeginDb()
    {
        PdvService.TestBeforeInsertSaleItems = null;
        PdvService.TestAfterInsertSaleItems = null;
        PdvService.TestAfterSwapItemUpdate = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(80, "cost-at-sale");
        return db;
    }

    [Fact]
    public void Schema_CriaColunaNullable()
    {
        using var _ = BeginDb();
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(sale_items);";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notNull = false;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var name = r.GetString(1);
            names.Add(name);
            if (name.Equals("cost_at_sale", StringComparison.OrdinalIgnoreCase))
                notNull = r.GetInt32(3) != 0;
        }
        Assert.Contains("cost_at_sale", names);
        Assert.False(notNull);
    }

    [Fact]
    public void Legado_SemBackfill_FicaNull()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(10, 8, 5);
        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
            VALUES ('2020-01-01', 8, 'Dinheiro', 0, datetime('now','localtime'));
            SELECT last_insert_rowid();
            """;
        var saleId = Convert.ToInt32(ins.ExecuteScalar());
        using var item = conn.CreateCommand();
        item.CommandText = """
            INSERT INTO sale_items (sale_id, product_id, product_name, quantity, unit_price, subtotal)
            VALUES ($s, $p, 'LEGADO', 1, 8, 8);
            """;
        item.Parameters.AddWithValue("$s", saleId);
        item.Parameters.AddWithValue("$p", pid);
        item.ExecuteNonQuery();
        Assert.Null(ReadCost(saleId));
    }

    [Fact]
    public void Helper_Normal_UnPorUn()
    {
        Assert.Equal(5, SaleCostSnapshotRules.ComputeLineUnitCost(
            3, 3, 5, "AGUA", "Bebidas", new ProductExtra()));
    }

    [Fact]
    public void Helper_CigarroAvulso_050()
    {
        var extra = new ProductExtra { FatorEmbalagem = 20 };
        Assert.Equal(0.50, SaleCostSnapshotRules.ComputeLineUnitCost(
            1, 1, 10, "Rothmans Blue", "Cigarros", extra));
    }

    [Fact]
    public void Helper_CigarroMaco_10()
    {
        var extra = new ProductExtra { FatorEmbalagem = 20 };
        Assert.Equal(10, SaleCostSnapshotRules.ComputeLineUnitCost(
            1, 20, 10, "Rothmans Blue", "Cigarros", extra));
    }

    [Fact]
    public void Helper_PackComercial1_Fisico24()
    {
        Assert.Equal(120, SaleCostSnapshotRules.ComputeLineUnitCost(
            1, 24, 5, "AGUA CX", "Bebidas", new ProductExtra()));
    }

    [Fact]
    public void Helper_Fracionado_NaoArredondaQuantidade()
    {
        Assert.Equal(10, SaleCostSnapshotRules.ComputeLineUnitCost(
            2.5, 2.5, 10, "AÇÚCAR KG", "Mercearia", new ProductExtra()));
        Assert.Equal(25, ProductPriceHelper.RoundPrice(2.5 * 10));
    }

    [Fact]
    public void Helper_Invalido_Bloqueia()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SaleCostSnapshotRules.ComputeLineUnitCost(0, 1, 5, "X", null, new ProductExtra()));
        Assert.Throws<InvalidOperationException>(() =>
            SaleCostSnapshotRules.ComputeLineUnitCost(1, 1, double.NaN, "X", null, new ProductExtra()));
        Assert.Throws<InvalidOperationException>(() =>
            SaleCostSnapshotRules.ComputeLineUnitCost(1, 1, -1, "X", null, new ProductExtra()));
    }

    [Fact]
    public void Venda_Normal_GravaSnapshot()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "N1", "AGUA 500");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 3, 8, 24);
        Assert.Equal(5, ReadCost(sale.SaleId));
        Assert.Equal(15, ProductPriceHelper.RoundPrice(3 * ReadCost(sale.SaleId)!.Value));
    }

    [Fact]
    public void CustoZero_GravaZeroNaoNull()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(10, 8, 0, "Z0", "BRINDE");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        Assert.Equal(0, ReadCost(sale.SaleId));
        Assert.False(IsCostNull(sale.SaleId));
    }

    [Fact]
    public void CustoManualPosterior_NaoAlteraSnapshot()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "CM", "CUSTO MANUAL");
        var a = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        SetCost(pid, 6);
        var b = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        Assert.Equal(5, ReadCost(a.SaleId));
        Assert.Equal(6, ReadCost(b.SaleId));
    }

    [Fact]
    public void CompraPosterior_NaoAlteraSnapshot()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(0, 8, 5, "CP", "COMPRA DEPOIS");
        var supplier = SeedSupplier();
        var first = PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-CAS1",
            GerarEstoque = true,
            Items = [new PurchaseItemInput { ProductId = pid, ProductName = "COMPRA DEPOIS", Quantity = 10, UnitPrice = 5, SalePrice = 8 }],
        }, closeOnSave: true);
        Assert.True(first > 0);
        var a = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        var costAfterA = ReadCost(a.SaleId);
        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-CAS2",
            GerarEstoque = true,
            Items = [new PurchaseItemInput { ProductId = pid, ProductName = "COMPRA DEPOIS", Quantity = 10, UnitPrice = 9, SalePrice = 8 }],
        }, closeOnSave: true);
        var b = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        Assert.Equal(costAfterA, ReadCost(a.SaleId));
        Assert.NotEqual(ReadCost(a.SaleId), ReadCost(b.SaleId));
        Assert.Equal(ProductService.GetById(pid)!.CostPrice, ReadCost(b.SaleId));
    }

    [Fact]
    public void CigarroAvulso_050_ECincoUnidades()
    {
        using var _ = BeginDb();
        var cig = SeedCig(200, 10, 20);
        var one = FinalizeCig(cig, PdvCigaretteSaleMode.Avulso, 1);
        var five = FinalizeCig(cig, PdvCigaretteSaleMode.Avulso, 5);
        Assert.Equal(0.50, ReadCost(one.SaleId));
        Assert.Equal(0.50, ReadCost(five.SaleId));
        Assert.Equal(2.50, ProductPriceHelper.RoundPrice(5 * ReadCost(five.SaleId)!.Value));
        var product = ProductService.GetById(cig)!;
        Assert.Equal(1.50, PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Avulso).UnitPrice);
        Assert.Equal(10, PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Maco).UnitPrice);
    }

    [Fact]
    public void CigarroMaco_Unitario10_Cmv20()
    {
        using var _ = BeginDb();
        var cig = SeedCig(200, 10, 20);
        var two = FinalizeCig(cig, PdvCigaretteSaleMode.Maco, 2);
        Assert.Equal(10, ReadCost(two.SaleId));
        Assert.Equal(20, ProductPriceHelper.RoundPrice(2 * ReadCost(two.SaleId)!.Value));
    }

    [Fact]
    public void AcrescimoEDesconto_NaoAlteramCusto()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "AD", "DESC ACR");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [Line(pid, 2, 8)],
            PaymentType = "Pix",
            Discount = 1,
            Surcharge = 0.50,
            CashReceived = 0,
        });
        Assert.Equal(5, ReadCost(sale.SaleId));

        var cig = SeedCig(200, 10, 20);
        var maco = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [CigLine(cig, PdvCigaretteSaleMode.Maco, 1)],
            PaymentType = "Cartão Crédito",
            Surcharge = 0.80,
            CashReceived = 0,
        });
        Assert.Equal(10, ReadCost(maco.SaleId));
    }

    [Theory]
    [InlineData("Dinheiro")]
    [InlineData("Pix")]
    [InlineData("Cartão Crédito")]
    public void Pagamento_Irrelevante(string pay)
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "PG", "PAG " + pay);
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [Line(pid, 1, 8)],
            PaymentType = pay,
            CashReceived = pay == "Dinheiro" ? 8 : 0,
        });
        Assert.Equal(5, ReadCost(sale.SaleId));
    }

    [Fact]
    public void Fiado_GravaSnapshot()
    {
        using var _ = BeginDb();
        var customer = SeedCustomer();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "FI", "FIADO SNAP");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [Line(pid, 1, 8)],
            PaymentType = "Fiado",
            Payments = [new PdvPaymentPart { PaymentType = "Fiado", Amount = 8 }],
            CustomerPersonId = customer,
        });
        Assert.Equal(5, ReadCost(sale.SaleId));
    }

    [Fact]
    public void EstoqueNegativo_AindaGravaCusto()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(0, 8, 5, "NEG", "SEM ESTOQUE");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        Assert.Equal(5, ReadCost(sale.SaleId));
        Assert.Equal(-1, TestDataHelper.GetProductStock(pid));
    }

    [Fact]
    public void Pack_Quantity1_Stock24()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(100, 8, 5, "PK", "FARDO 24");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = pid,
                    Quantity = 1,
                    UnitPrice = 120,
                    StockUnitsPerSale = 24,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 120,
        });
        Assert.Equal(120, ReadCost(sale.SaleId));
        Assert.Equal(120, ProductPriceHelper.RoundPrice(1 * ReadCost(sale.SaleId)!.Value));
        Assert.Equal(76, TestDataHelper.GetProductStock(pid));
    }

    [Fact]
    public void Pack_DoisFardos()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(100, 8, 5, "PK2", "FARDO 24 B");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = pid,
                    Quantity = 2,
                    UnitPrice = 120,
                    StockUnitsPerSale = 24,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 240,
        });
        Assert.Equal(120, ReadCost(sale.SaleId));
        Assert.Equal(240, ProductPriceHelper.RoundPrice(2 * ReadCost(sale.SaleId)!.Value));
    }

    [Fact]
    public void Fracionado_2e5Kg()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 12, 10, "KG", "AÇÚCAR KG");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [new PdvCartLine { ProductId = pid, Quantity = 2.5, UnitPrice = 12, Unit = "KG" }],
            PaymentType = "Dinheiro",
            CashReceived = 30,
        });
        Assert.Equal(10, ReadCost(sale.SaleId));
        Assert.Equal(25, ProductPriceHelper.RoundPrice(2.5 * ReadCost(sale.SaleId)!.Value));
    }

    [Fact]
    public void Deck_SnapshotNoFechamento()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "DK", "DECK SNAP");
        var tab = OpenTabService.Create("Mesa snapshot");
        OpenTabService.AddProduct(tab, pid, 1, 8);
        Assert.Equal(0, CountSaleItems());
        SetCost(pid, 6);
        var lines = OpenTabService.ToCartLines(tab).ToList();
        var sale = OpenTabSettlementService.SettleOpenTab(tab, new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Dinheiro",
            CashReceived = 8,
        });
        Assert.Equal(6, ReadCost(sale.SaleId));
    }

    [Fact]
    public void Swap_AtualizaParaCustoDoNovo()
    {
        using var _ = BeginDb();
        var a = TestDataHelper.SeedSimpleProduct(20, 8, 5, "SWA", "SWAP A");
        var b = TestDataHelper.SeedSimpleProduct(20, 12, 7, "SWB", "SWAP B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 8, 8);
        Assert.Equal(5, ReadCost(sale.SaleId));
        var itemId = ReadItemId(sale.SaleId);
        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 12 }],
            cashReceived: 12);
        Assert.Equal(7, ReadCost(sale.SaleId));
        Assert.Equal(b, ReadProductId(sale.SaleId));
    }

    [Fact]
    public void Swap_Rollback_PreservaSnapshotAnterior()
    {
        using var _ = BeginDb();
        var a = TestDataHelper.SeedSimpleProduct(20, 8, 5, "SRA", "SWAP RB A");
        var b = TestDataHelper.SeedSimpleProduct(20, 12, 7, "SRB", "SWAP RB B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 8, 8);
        var itemId = ReadItemId(sale.SaleId);
        try
        {
            PdvService.TestAfterSwapItemUpdate = () =>
                throw new InvalidOperationException("falha controlada no swap");
            Assert.Throws<InvalidOperationException>(() =>
                PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
                    confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 12 }],
                    cashReceived: 12));
        }
        finally
        {
            PdvService.TestAfterSwapItemUpdate = null;
        }
        Assert.Equal(5, ReadCost(sale.SaleId));
        Assert.Equal(a, ReadProductId(sale.SaleId));
        Assert.Equal(19, TestDataHelper.GetProductStock(a));
        Assert.Equal(20, TestDataHelper.GetProductStock(b));
    }

    [Fact]
    public void CancelarVenda_MantemSnapshot()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "CV", "CANCEL SNAP");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(5, ReadCost(sale.SaleId));
        Assert.Equal(1, ReadCancelled(sale.SaleId));
    }

    [Fact]
    public void MergePosterior_NaoRecalculaSnapshot()
    {
        using var _ = BeginDb();
        var keep = TestDataHelper.SeedSimpleProduct(20, 8, 7, "MK", "KEEP SNAP");
        var absorb = TestDataHelper.SeedSimpleProduct(20, 8, 5, "MA", "ABS SNAP");
        var sale = TestDataHelper.FinalizeSimpleCashSale(absorb, 1, 8, 8);
        Assert.Equal(5, ReadCost(sale.SaleId));
        ProductService.MergeProducts(keep, absorb);
        Assert.Equal(5, ReadCost(sale.SaleId));
        Assert.Equal(keep, ReadProductId(sale.SaleId));
    }

    [Fact]
    public void FalhaAntesDoInsert_RollbackSemVenda()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RB1", "RB ANTES");
        try
        {
            PdvService.TestBeforeInsertSaleItems = () =>
                throw new InvalidOperationException("falha controlada antes do item");
            Assert.Throws<InvalidOperationException>(() =>
                TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8));
        }
        finally
        {
            PdvService.TestBeforeInsertSaleItems = null;
        }
        Assert.Equal(0, CountSales());
        Assert.Equal(10, TestDataHelper.GetProductStock(pid));
        Assert.Equal(0, CountCashSales());
        Assert.Equal(0, CountSaleItems());
    }

    [Fact]
    public void FalhaDepoisDoItem_RollbackEstoqueECaixa()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RB2", "RB DEPOIS");
        try
        {
            PdvService.TestAfterInsertSaleItems = () =>
                throw new InvalidOperationException("falha controlada depois do item");
            Assert.Throws<InvalidOperationException>(() =>
                TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8));
        }
        finally
        {
            PdvService.TestAfterInsertSaleItems = null;
        }
        Assert.Equal(0, CountSales());
        Assert.Equal(10, TestDataHelper.GetProductStock(pid));
        Assert.Equal(0, CountCashSales());
        Assert.Equal(0, CountSaleItems());
    }

    private static PdvCartLine Line(int productId, double qty, double price) =>
        new() { ProductId = productId, Quantity = qty, UnitPrice = price };

    private static PdvCartLine CigLine(int productId, string mode, double qty)
    {
        var product = ProductService.GetById(productId)!;
        var resolved = PdvService.ResolveManualSale(product, mode);
        return new PdvCartLine
        {
            ProductId = productId,
            Quantity = qty,
            UnitPrice = resolved.UnitPrice,
            StockUnitsPerSale = resolved.StockUnitsPerSale,
        };
    }

    private static PdvFinalizeResult FinalizeCig(int productId, string mode, double qty)
    {
        var line = CigLine(productId, mode, qty);
        var total = ProductPriceHelper.RoundPrice(line.Quantity * line.UnitPrice);
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [line],
            PaymentType = "Dinheiro",
            CashReceived = total,
        });
    }

    private static int SeedCig(double stock, double cost, double fator)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoAvulso = 1.50,
            PrecoAtacado = 10,
            PrecoCompra = cost,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CIGCAS', 'Rothmans Blue', 'Cigarros', 'UN', 10, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCustomer()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('cliente', 'fisica', 'CLI SNAP', 1, '{"ativo":true,"clientes":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN SNAP', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetCost(int productId, double cost)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET cost_price = $c WHERE id = $id;";
        cmd.Parameters.AddWithValue("$c", cost);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static double? ReadCost(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cost_at_sale FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var o = cmd.ExecuteScalar();
        return o is null or DBNull ? null : Convert.ToDouble(o);
    }

    private static bool IsCostNull(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cost_at_sale IS NULL FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    private static int ReadItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int ReadProductId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int ReadCancelled(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(cancelled,0) FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountSales()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sales;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountSaleItems()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sale_items;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountCashSales()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cash_movements WHERE IFNULL(ref_type,'') = 'sale';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
