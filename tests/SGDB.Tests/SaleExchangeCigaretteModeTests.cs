using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 46 — Troca/Devolução (SaleExchange) com modalidade AVULSO / MAÇO.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class SaleExchangeCigaretteModeTests
{
    private static void EnsureStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    // ——— Resolve (service, sem UI) ———

    [Fact]
    public void Resolve_Avulso_PrecoEFator1()
    {
        using var _ = TempDatabase.Create();
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var product = ProductService.GetByIdLocal(cig)!;

        var r = SaleExchangeService.ResolveNewSaleLine(product, new SaleExchangeNewLine
        {
            ProductId = cig, Qty = 1, CigaretteMode = PdvCigaretteSaleMode.Avulso,
            UnitPrice = 999, // ignorado
        });

        Assert.Equal(1.50, r.UnitPrice);
        Assert.Equal(1, r.StockUnitsPerSale);
        Assert.Equal(1, r.StockQty);
        Assert.Equal(1.50, r.Amount);
        Assert.Contains("AVULSO", r.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Maco_PrecoEFator()
    {
        using var _ = TempDatabase.Create();
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var product = ProductService.GetByIdLocal(cig)!;

        var r = SaleExchangeService.ResolveNewSaleLine(product, new SaleExchangeNewLine
        {
            ProductId = cig, Qty = 1, CigaretteMode = PdvCigaretteSaleMode.Maco,
            UnitPrice = 1.50, // ignorado
        });

        Assert.Equal(28.50, r.UnitPrice);
        Assert.Equal(20, r.StockUnitsPerSale);
        Assert.Equal(20, r.StockQty);
        Assert.Equal(28.50, r.Amount);
        Assert.Contains("MAÇO", r.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_QtyAvulso_Fisico5()
    {
        using var _ = TempDatabase.Create();
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var product = ProductService.GetByIdLocal(cig)!;

        var r = SaleExchangeService.ResolveNewSaleLine(product, new SaleExchangeNewLine
        {
            ProductId = cig, Qty = 5, CigaretteMode = PdvCigaretteSaleMode.Avulso,
        });

        Assert.Equal(5, r.StockQty);
        Assert.Equal(7.50, r.Amount);
    }

    [Fact]
    public void Resolve_QtyMaco_Fisico40()
    {
        using var _ = TempDatabase.Create();
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var product = ProductService.GetByIdLocal(cig)!;

        var r = SaleExchangeService.ResolveNewSaleLine(product, new SaleExchangeNewLine
        {
            ProductId = cig, Qty = 2, CigaretteMode = PdvCigaretteSaleMode.Maco,
        });

        Assert.Equal(40, r.StockQty);
        Assert.Equal(57.00, r.Amount);
    }

    [Fact]
    public void Resolve_SemPrecoAvulso_Maco()
    {
        using var _ = TempDatabase.Create();
        var cig = SeedCigarro(100, 0, 28.50, 20);
        var product = ProductService.GetByIdLocal(cig)!;

        var r = SaleExchangeService.ResolveNewSaleLine(product, new SaleExchangeNewLine
        {
            ProductId = cig, Qty = 1, CigaretteMode = null,
        });

        Assert.Equal(28.50, r.UnitPrice);
        Assert.Equal(20, r.StockUnitsPerSale);
        Assert.Equal(20, r.StockQty);
    }

    [Fact]
    public void Resolve_SemMode_Cigarro_Maco()
    {
        using var _ = TempDatabase.Create();
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var product = ProductService.GetByIdLocal(cig)!;

        var r = SaleExchangeService.ResolveNewSaleLine(product, new SaleExchangeNewLine
        {
            ProductId = cig, Qty = 1, CigaretteMode = null, UnitPrice = 99,
        });

        Assert.Equal(28.50, r.UnitPrice);
        Assert.Equal(20, r.StockQty);
    }

    [Fact]
    public void Resolve_ProdutoComum_SalePrice()
    {
        using var _ = TempDatabase.Create();
        var id = TestDataHelper.SeedSimpleProduct(50, 10, 4, "C1", "Comum");
        var product = ProductService.GetByIdLocal(id)!;

        var r = SaleExchangeService.ResolveNewSaleLine(product, new SaleExchangeNewLine
        {
            ProductId = id, Qty = 3, UnitPrice = null, CigaretteMode = "AVULSO",
        });

        Assert.Equal(10, r.UnitPrice);
        Assert.Equal(1, r.StockUnitsPerSale);
        Assert.Equal(3, r.StockQty);
        Assert.Equal(30, r.Amount);
        Assert.Equal("Comum", r.DisplayName);
        Assert.Null(r.ModeLabel);
    }

    [Fact]
    public void Resolve_ModeInvalido_Lanca()
    {
        using var _ = TempDatabase.Create();
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var product = ProductService.GetByIdLocal(cig)!;

        Assert.Throws<SaleExchangeException>(() =>
            SaleExchangeService.ResolveNewSaleLine(product, new SaleExchangeNewLine
            {
                ProductId = cig, Qty = 1, CigaretteMode = "FOO",
            }));
    }

    // ——— Confirm integração ———

    [Fact]
    public void Confirm_NovoAvulso_PrecoEEstoque()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionRole("admin");

        var comum = TestDataHelper.SeedSimpleProduct(20, 10, 4, "X", "X");
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems =
            [
                new SaleExchangeNewLine
                {
                    ProductId = cig, Qty = 1, CigaretteMode = PdvCigaretteSaleMode.Avulso,
                    UnitPrice = 28.50, // deve ser ignorado
                },
            ],
            PaymentType = "Dinheiro",
        });

        Assert.Equal(1.50, result.NewTotal);
        Assert.Equal(ProductPriceHelper.RoundPrice(1.50 - 10), result.Difference);
        Assert.Equal(99, TestDataHelper.GetProductStock(cig)); // -1 físico
        Assert.Equal(20, TestDataHelper.GetProductStock(comum)); // restore

        var (qty, price, name) = GetNewExchangeItem(result.ExchangeId);
        Assert.Equal(1, qty);
        Assert.Equal(1.50, price);
        Assert.Contains("AVULSO", name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Confirm_NovoMaco_PrecoEEstoque()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionRole("admin");

        var comum = TestDataHelper.SeedSimpleProduct(20, 10, 4, "X", "X");
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems =
            [
                new SaleExchangeNewLine
                {
                    ProductId = cig, Qty = 1, CigaretteMode = PdvCigaretteSaleMode.Maco,
                },
            ],
            PaymentType = "Dinheiro",
        });

        Assert.Equal(28.50, result.NewTotal);
        Assert.Equal(80, TestDataHelper.GetProductStock(cig)); // -20
        var (_, price, name) = GetNewExchangeItem(result.ExchangeId);
        Assert.Equal(28.50, price);
        Assert.Contains("MAÇO", name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Confirm_MacoEAvulsos_BaixaFisica23()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(200, "t");
        TestDataHelper.SetSessionRole("admin");

        var comum = TestDataHelper.SeedSimpleProduct(20, 10, 4, "X", "X");
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems =
            [
                new SaleExchangeNewLine
                {
                    ProductId = cig, Qty = 1, CigaretteMode = PdvCigaretteSaleMode.Maco,
                },
                new SaleExchangeNewLine
                {
                    ProductId = cig, Qty = 3, CigaretteMode = PdvCigaretteSaleMode.Avulso,
                },
            ],
            PaymentType = "Dinheiro",
        });

        // 20 + 3 = 23 físico
        Assert.Equal(77, TestDataHelper.GetProductStock(cig));
        Assert.Equal(ProductPriceHelper.RoundPrice(28.50 + 4.50), result.NewTotal);
    }

    [Fact]
    public void Confirm_SemPrecoAvulso_EntraMaco()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionRole("admin");

        var comum = TestDataHelper.SeedSimpleProduct(20, 10, 4, "X", "X");
        var cig = SeedCigarro(100, 0, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems =
            [
                new SaleExchangeNewLine { ProductId = cig, Qty = 1, CigaretteMode = null },
            ],
            PaymentType = "Dinheiro",
        });

        Assert.Equal(80, TestDataHelper.GetProductStock(cig));
    }

    [Fact]
    public void Confirm_ProdutoComum_Preservado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionRole("admin");

        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(30, 12, 5, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems =
            [
                new SaleExchangeNewLine { ProductId = b, Qty = 2, UnitPrice = 12 },
            ],
            PaymentType = "Dinheiro",
        });

        Assert.Equal(24, result.NewTotal);
        Assert.Equal(28, TestDataHelper.GetProductStock(b)); // 30 - 2
        Assert.Equal(20, TestDataHelper.GetProductStock(a)); // restore
    }

    [Fact]
    public void Confirm_AvulsoSemPreco_NaoAlteraEstoque()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionRole("admin");

        var comum = TestDataHelper.SeedSimpleProduct(20, 10, 4, "X", "X");
        var cig = SeedCigarro(100, 0, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);
        var stockCig = TestDataHelper.GetProductStock(cig);
        var stockComum = TestDataHelper.GetProductStock(comum);

        Assert.Throws<SaleExchangeException>(() =>
            SaleExchangeService.Confirm(new SaleExchangeRequest
            {
                OriginalSaleId = sale.SaleId,
                Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
                NewItems =
                [
                    new SaleExchangeNewLine
                    {
                        ProductId = cig, Qty = 1, CigaretteMode = PdvCigaretteSaleMode.Avulso,
                    },
                ],
                PaymentType = "Dinheiro",
            }));

        Assert.Equal(stockCig, TestDataHelper.GetProductStock(cig));
        Assert.Equal(stockComum, TestDataHelper.GetProductStock(comum));
        Assert.Equal(0, CountExchanges());
    }

    [Fact]
    public void Confirm_ModeInvalido_Rollback()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionRole("admin");

        var comum = TestDataHelper.SeedSimpleProduct(20, 10, 4, "X", "X");
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        Assert.Throws<SaleExchangeException>(() =>
            SaleExchangeService.Confirm(new SaleExchangeRequest
            {
                OriginalSaleId = sale.SaleId,
                Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
                NewItems =
                [
                    new SaleExchangeNewLine { ProductId = cig, Qty = 1, CigaretteMode = "XYZ" },
                ],
                PaymentType = "Dinheiro",
            }));

        Assert.Equal(100, TestDataHelper.GetProductStock(cig));
        Assert.Equal(19, TestDataHelper.GetProductStock(comum));
        Assert.Equal(0, CountExchanges());
    }

    [Fact]
    public void Confirm_Sucesso_GeraAuditComMode()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionRole("admin");

        var comum = TestDataHelper.SeedSimpleProduct(20, 10, 4, "X", "X");
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems =
            [
                new SaleExchangeNewLine
                {
                    ProductId = cig, Qty = 1, CigaretteMode = PdvCigaretteSaleMode.Avulso,
                },
            ],
            PaymentType = "Dinheiro",
        });

        var details = GetAuditTrocaDetails(sale.SaleId);
        Assert.False(string.IsNullOrWhiteSpace(details));
        Assert.True(AuditPayloadBuilder.TryParse(details, out var doc));
        var payload = doc.Payload;
        Assert.Equal("troca_venda", payload.GetProperty("op").GetString());
        var items = payload.GetProperty("new_items");
        Assert.Equal(1, items.GetArrayLength());
        var first = items[0];
        Assert.Equal(1, first.GetProperty("stock_units_per_sale").GetDouble());
        Assert.Equal(1, first.GetProperty("stock_qty").GetDouble());
        Assert.Equal(1.50, first.GetProperty("unit_price").GetDouble());
        Assert.Contains("AVULSO", first.GetProperty("mode").GetString() ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Confirm_SemPermissao_Bloqueado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionCustomPermissions("operador", p => p.PdvTrocaDevolucao = false);

        var comum = TestDataHelper.SeedSimpleProduct(20, 10, 4, "X", "X");
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);
        var stock = TestDataHelper.GetProductStock(comum);

        var ex = Assert.Throws<SaleExchangeException>(() =>
            SaleExchangeService.Confirm(new SaleExchangeRequest
            {
                OriginalSaleId = sale.SaleId,
                Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
                NewItems = [],
                PaymentType = "Dinheiro",
            }));
        Assert.Contains("permissão", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(stock, TestDataHelper.GetProductStock(comum));
    }

    [Fact]
    public void Confirm_ClientMode_Bloqueado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionRole("admin");

        var comum = TestDataHelper.SeedSimpleProduct(20, 10, 4, "X", "X");
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        try
        {
            Assert.Throws<StoreNetworkClientBlockedException>(() =>
                SaleExchangeService.Confirm(new SaleExchangeRequest
                {
                    OriginalSaleId = sale.SaleId,
                    Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
                    NewItems =
                    [
                        new SaleExchangeNewLine
                        {
                            ProductId = cig, Qty = 1, CigaretteMode = PdvCigaretteSaleMode.Avulso,
                        },
                    ],
                    PaymentType = "Dinheiro",
                }));
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.Equal(100, TestDataHelper.GetProductStock(cig));
        Assert.Equal(19, TestDataHelper.GetProductStock(comum));
    }

    [Fact]
    public void Confirm_DiferencaFinanceira_UsaPrecoAvulso()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(100, "t");
        TestDataHelper.SetSessionRole("admin");

        // Devolve item de R$ 28,50 (venda maço simbólica via comum caro)
        var caro = TestDataHelper.SeedSimpleProduct(20, 28.50, 10, "CARO", "Caro");
        var cig = SeedCigarro(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(caro, 1, 28.50, 28.50);
        var itemId = GetSaleItemId(sale.SaleId);

        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems =
            [
                new SaleExchangeNewLine
                {
                    ProductId = cig, Qty = 1, CigaretteMode = PdvCigaretteSaleMode.Avulso,
                },
            ],
            PaymentType = "Dinheiro",
        });

        Assert.Equal(28.50, result.ReturnTotal);
        Assert.Equal(1.50, result.NewTotal);
        Assert.Equal(-27.00, result.Difference);
    }

    // ——— helpers ———

    private static int SeedCigarro(
        double stock, double precoAvulso, double precoMaco, double fator)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            PrecoAtacado = precoMaco,
            PrecoAvulso = precoAvulso,
            QtdAtacado = fator,
        }.ToJson();
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $name, 'Cigarros', 'UN', $sale, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", "CIG" + Guid.NewGuid().ToString("N")[..6]);
        cmd.Parameters.AddWithValue("$name", "Rothmans Blue");
        cmd.Parameters.AddWithValue("$sale", precoMaco);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", precoMaco * 0.5);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetSaleItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static (double Qty, double Price, string Name) GetNewExchangeItem(int exchangeId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT qty, unit_price, product_name
            FROM sale_exchange_new_items WHERE exchange_id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", exchangeId);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        return (r.GetDouble(0), r.GetDouble(1), r.GetString(2));
    }

    private static int CountExchanges()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sale_exchanges;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string GetAuditTrocaDetails(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(details,'') FROM audit_log
            WHERE action = 'troca' AND entity = 'venda' AND entity_id = $id
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", saleId.ToString());
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
}
