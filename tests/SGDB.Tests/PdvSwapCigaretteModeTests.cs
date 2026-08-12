using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 44A — Swap com modalidade AVULSO / MAÇO.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvSwapCigaretteModeTests
{
    private static void EnsureStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    // ——— Gateway mapping ———

    [Fact]
    public void Gateway_Command_CigaretteMode_NullPreserved()
    {
        var previewCmd = new PreviewSwapSaleItemCommand
        {
            SaleId = 1, ItemId = 2, NewProductId = 3, CigaretteMode = null,
        };
        Assert.Null(previewCmd.CigaretteMode);

        var swapCmd = new SwapSaleItemCommand
        {
            SaleId = 1, ItemId = 2, NewProductId = 3, CigaretteMode = null,
        };
        Assert.Null(swapCmd.CigaretteMode);
    }

    [Theory]
    [InlineData("AVULSO")]
    [InlineData("MAÇO")]
    [InlineData("MACO")]
    public void Gateway_Command_CigaretteMode_Preserved(string mode)
    {
        Assert.Equal(mode, new PreviewSwapSaleItemCommand
        {
            SaleId = 1, ItemId = 1, NewProductId = 1, CigaretteMode = mode,
        }.CigaretteMode);
        Assert.Equal(mode, new SwapSaleItemCommand
        {
            SaleId = 1, ItemId = 1, NewProductId = 1, CigaretteMode = mode,
        }.CigaretteMode);
    }

    [Fact]
    public void Normalize_ModeInvalido_Lanca()
    {
        var ex = Assert.Throws<PdvException>(() =>
            PdvService.NormalizeCigaretteModeForSwap("XYZ"));
        Assert.Contains("inválida", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_Null_RetornaNull()
    {
        Assert.Null(PdvService.NormalizeCigaretteModeForSwap(null));
        Assert.Null(PdvService.NormalizeCigaretteModeForSwap(""));
    }

    // ——— Produto comum ———

    [Fact]
    public void Swap_ProdutoComum_ComportamentoPreservado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var a = TestDataHelper.SeedSimpleProduct(30, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(30, 12, 5, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 12 }],
            cashReceived: 12);

        Assert.Equal(b, GetProductId(itemId));
        Assert.Equal(12, GetUnitPrice(itemId));
        Assert.Equal(1, GetStockQty(itemId));
        Assert.Equal(12, GetSaleTotal(sale.SaleId));
    }

    // ——— AVULSO / MAÇO ———

    [Fact]
    public void Swap_ParaAvulso_PrecoEStockQty()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var comum = TestDataHelper.SeedSimpleProduct(40, 10, 4, "C", "Comum");
        var cig = SeedCigarroAvulsoMaco(stock: 100, precoAvulso: 1.50, precoMaco: 28.50, fator: 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Avulso,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 1.50 }],
            cashReceived: 1.50);

        Assert.Equal(1.50, GetUnitPrice(itemId));
        Assert.Equal(1, GetStockQty(itemId));
        Assert.Equal(1, GetQty(itemId));
        Assert.Equal(1.50, GetSaleTotal(sale.SaleId));
        Assert.Contains("AVULSO", GetProductName(itemId), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(99, TestDataHelper.GetProductStock(cig)); // -1
        Assert.Equal(40, TestDataHelper.GetProductStock(comum)); // restored
    }

    [Fact]
    public void Swap_ParaMaco_PrecoEStockQty()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var comum = TestDataHelper.SeedSimpleProduct(40, 10, 4, "C", "Comum");
        var cig = SeedCigarroAvulsoMaco(stock: 100, precoAvulso: 1.50, precoMaco: 28.50, fator: 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Maco,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 28.50 }],
            cashReceived: 28.50);

        Assert.Equal(28.50, GetUnitPrice(itemId));
        Assert.Equal(20, GetStockQty(itemId));
        Assert.Contains("MAÇO", GetProductName(itemId), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(80, TestDataHelper.GetProductStock(cig)); // -20
    }

    [Fact]
    public void Swap_NullMode_Cigarro_ResolveComoMaco()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var comum = TestDataHelper.SeedSimpleProduct(40, 10, 4, "C", "Comum");
        var cig = SeedCigarroAvulsoMaco(stock: 100, precoAvulso: 1.50, precoMaco: 28.50, fator: 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: null,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 28.50 }],
            cashReceived: 28.50);

        Assert.Equal(20, GetStockQty(itemId));
        Assert.Equal(28.50, GetUnitPrice(itemId));
    }

    [Fact]
    public void Swap_AvulsoSemPrecoAvulso_NaoAlteraEstado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var comum = TestDataHelper.SeedSimpleProduct(40, 10, 4, "C", "Comum");
        // Sem PrecoAvulso
        var cig = SeedCigarroAvulsoMaco(stock: 100, precoAvulso: 0, precoMaco: 28.50, fator: 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);
        var stockC = TestDataHelper.GetProductStock(comum);
        var stockG = TestDataHelper.GetProductStock(cig);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
                cigaretteMode: PdvCigaretteSaleMode.Avulso));

        Assert.Contains("avulsa", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(comum, GetProductId(itemId));
        Assert.Equal(10, GetSaleTotal(sale.SaleId));
        Assert.Equal(stockC, TestDataHelper.GetProductStock(comum));
        Assert.Equal(stockG, TestDataHelper.GetProductStock(cig));
        Assert.Equal(0, CountAudit(sale.SaleId));
    }

    // ——— Same ProductId ———

    [Fact]
    public void Swap_MacoParaAvulso_MesmoProductId()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var cig = SeedCigarroAvulsoMaco(stock: 100, precoAvulso: 1.50, precoMaco: 28.50, fator: 20);
        var sale = FinalizeMaco(cig, qty: 1, unit: 28.50);
        Assert.Equal(80, TestDataHelper.GetProductStock(cig));
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Avulso,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 1.50 }],
            cashReceived: 1.50);

        Assert.Equal(1.50, GetUnitPrice(itemId));
        Assert.Equal(1, GetStockQty(itemId));
        Assert.Equal(99, TestDataHelper.GetProductStock(cig)); // +20 -1 = +19 from 80 → 99
        Assert.Equal(1.50, GetSaleTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_AvulsoParaMaco_MesmoProductId()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var cig = SeedCigarroAvulsoMaco(stock: 100, precoAvulso: 1.50, precoMaco: 28.50, fator: 20);
        var sale = FinalizeAvulso(cig, qty: 1, unit: 1.50);
        Assert.Equal(99, TestDataHelper.GetProductStock(cig));
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Maco,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 28.50 }],
            cashReceived: 28.50);

        Assert.Equal(28.50, GetUnitPrice(itemId));
        Assert.Equal(20, GetStockQty(itemId));
        Assert.Equal(80, TestDataHelper.GetProductStock(cig)); // +1 -20 from 99 → 80
    }

    [Fact]
    public void Swap_MesmoProdutoMesmaModalidadeMesmaQty_Bloqueia()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var cig = SeedCigarroAvulsoMaco(stock: 100, precoAvulso: 1.50, precoMaco: 28.50, fator: 20);
        var sale = FinalizeMaco(cig, 1, 28.50);
        var itemId = GetItemId(sale.SaleId);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
                cigaretteMode: PdvCigaretteSaleMode.Maco));
        Assert.Contains("modalidade", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Swap_AvulsoQty5_StockQty5()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var comum = TestDataHelper.SeedSimpleProduct(40, 10, 4, "C", "Comum");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false, newQuantity: 5,
            cigaretteMode: PdvCigaretteSaleMode.Avulso,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 7.50 }],
            cashReceived: 7.50);

        Assert.Equal(5, GetQty(itemId));
        Assert.Equal(5, GetStockQty(itemId));
        Assert.Equal(95, TestDataHelper.GetProductStock(cig));
    }

    [Fact]
    public void Swap_MacoQty2_StockQty40()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var comum = TestDataHelper.SeedSimpleProduct(40, 10, 4, "C", "Comum");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false, newQuantity: 2,
            cigaretteMode: PdvCigaretteSaleMode.Maco,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 57 }],
            cashReceived: 57);

        Assert.Equal(2, GetQty(itemId));
        Assert.Equal(40, GetStockQty(itemId));
        Assert.Equal(60, TestDataHelper.GetProductStock(cig));
    }

    [Fact]
    public void Swap_KeepLinePrice_Avulso_Fator1()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var comum = TestDataHelper.SeedSimpleProduct(40, 10, 4, "C", "Comum");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        // total igual → sem confirmação
        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: true,
            cigaretteMode: PdvCigaretteSaleMode.Avulso);

        Assert.Equal(10, GetUnitPrice(itemId));
        Assert.Equal(1, GetStockQty(itemId));
        Assert.Equal(10, GetSaleTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_KeepLinePrice_Maco_Fator20()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var comum = TestDataHelper.SeedSimpleProduct(40, 10, 4, "C", "Comum");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: true,
            cigaretteMode: PdvCigaretteSaleMode.Maco);

        Assert.Equal(10, GetUnitPrice(itemId));
        Assert.Equal(20, GetStockQty(itemId));
        Assert.Equal(10, GetSaleTotal(sale.SaleId));
        Assert.Equal(80, TestDataHelper.GetProductStock(cig));
    }

    [Fact]
    public void Preview_MacoParaAvulso_DifferenceNegativa()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = FinalizeMaco(cig, 1, 28.50);
        var itemId = GetItemId(sale.SaleId);
        var stockBefore = TestDataHelper.GetProductStock(cig);

        var preview = PdvService.PreviewSwapSaleItem(sale.SaleId, itemId, cig,
            keepLinePrice: false, cigaretteMode: PdvCigaretteSaleMode.Avulso);

        Assert.Equal(28.50, preview.OldTotal);
        Assert.Equal(1.50, preview.NewTotal);
        Assert.Equal(-27, preview.Difference);
        Assert.True(preview.RequiresPaymentConfirmation);
        Assert.Equal(stockBefore, TestDataHelper.GetProductStock(cig)); // preview não mexe estoque
    }

    [Fact]
    public void CancelApos_MacoParaAvulso_RestauraEstoqueOriginal()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.GrantPdvCancelPermission();
        const double stock0 = 100;
        var cig = SeedCigarroAvulsoMaco(stock0, 1.50, 28.50, 20);
        var sale = FinalizeMaco(cig, 1, 28.50);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Avulso,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 1.50 }],
            cashReceived: 1.50);

        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(stock0, TestDataHelper.GetProductStock(cig));
    }

    [Fact]
    public void Swap_MacoParaAvulso_Reducao_ExigeConfirmacao()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = FinalizeMaco(cig, 1, 28.50);
        var itemId = GetItemId(sale.SaleId);

        Assert.ThrowsAny<Exception>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
                cigaretteMode: PdvCigaretteSaleMode.Avulso));

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Avulso,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 1.50 }],
            cashReceived: 1.50);
        Assert.Equal(1.50, GetSaleTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_AvulsoParaMaco_Aumento_NaoInventaPagamento()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = FinalizeAvulso(cig, 1, 1.50);
        var itemId = GetItemId(sale.SaleId);

        Assert.ThrowsAny<Exception>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
                cigaretteMode: PdvCigaretteSaleMode.Maco));

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Maco,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 28.50 }],
            cashReceived: 28.50);
        Assert.Equal(28.50, GetSaleTotal(sale.SaleId));
        Assert.DoesNotContain(GetCash(sale.SaleId), m => m.Contains("Pix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Swap_FiadoPuro_MacoParaAvulso_AjustaDivida()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var customerId = SeedCustomer("Cliente Fiado Swap");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = cig, Quantity = 1, UnitPrice = 28.50, StockUnitsPerSale = 20,
                    Name = "Rothmans", Code = "R",
                },
            ],
            PaymentType = "Fiado",
            Payments = [new PdvPaymentPart { PaymentType = "Fiado", Amount = 28.50 }],
            CustomerPersonId = customerId,
        });
        Assert.Equal(28.50, FiadoService.GetDetail(customerId).Balance);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Avulso);

        Assert.Equal(1.50, GetSaleTotal(sale.SaleId));
        Assert.Equal(1.50, FiadoService.GetDetail(customerId).Balance);
    }

    [Fact]
    public void Swap_Desconto_MacoParaAvulso_PreservaAjuste()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = cig, Quantity = 1, UnitPrice = 28.50, StockUnitsPerSale = 20,
                    Name = "Rothmans", Code = "R",
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 28,
            Discount = 0.50, // gross 28.50 → total 28; adjustment -0.50
        });
        Assert.Equal(28, sale.Total);
        var itemId = GetItemId(sale.SaleId);

        // New gross 1.50 + adjustment -0.50 → 1.00
        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Avulso,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 1 }],
            cashReceived: 1);

        Assert.Equal(1, GetSaleTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_Avulso_AuditContemStockUnits()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var comum = TestDataHelper.SeedSimpleProduct(40, 10, 4, "C", "Comum");
        var cig = SeedCigarroAvulsoMaco(100, 1.50, 28.50, 20);
        var sale = TestDataHelper.FinalizeSimpleCashSale(comum, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: false,
            cigaretteMode: PdvCigaretteSaleMode.Avulso,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 1.50 }],
            cashReceived: 1.50);

        var details = GetAuditDetails(sale.SaleId);
        Assert.Contains("new_stock_units_per_sale", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("old_stock_units_per_sale", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new_mode", details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Swap_ModeInvalido_NaoAltera()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "t");
        TestDataHelper.SetSessionRole("admin");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetItemId(sale.SaleId);

        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, a, keepLinePrice: true,
                newQuantity: 2, cigaretteMode: "FOO"));

        Assert.Equal(1, GetQty(itemId));
        Assert.Equal(10, GetSaleTotal(sale.SaleId));
    }

    // ——— helpers ———

    private static int SeedCigarroAvulsoMaco(
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

    private static PdvFinalizeResult FinalizeMaco(int cigId, double qty, double unit) =>
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = cigId, Quantity = qty, UnitPrice = unit,
                    StockUnitsPerSale = 20, Name = "Rothmans", Code = "R",
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = qty * unit,
        });

    private static PdvFinalizeResult FinalizeAvulso(int cigId, double qty, double unit) =>
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = cigId, Quantity = qty, UnitPrice = unit,
                    StockUnitsPerSale = 1, Name = "Rothmans (AVULSO)", Code = "R",
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = qty * unit,
        });

    private static int SeedCustomer(string name)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('cliente', 'fisica', $name, 1, '{"ativo":true,"clientes":true}');
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetProductId(int itemId) => ScalarInt("SELECT product_id FROM sale_items WHERE id=$id;", itemId);
    private static double GetQty(int itemId) => ScalarDouble("SELECT quantity FROM sale_items WHERE id=$id;", itemId);
    private static double GetUnitPrice(int itemId) => ScalarDouble("SELECT unit_price FROM sale_items WHERE id=$id;", itemId);
    private static double GetStockQty(int itemId) => ScalarDouble("SELECT IFNULL(stock_qty,0) FROM sale_items WHERE id=$id;", itemId);
    private static double GetSaleTotal(int saleId) => ScalarDouble("SELECT total FROM sales WHERE id=$id;", saleId);
    private static string GetProductName(int itemId) => ScalarString("SELECT product_name FROM sale_items WHERE id=$id;", itemId);

    private static int CountAudit(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM audit_log
            WHERE action = 'trocar_item' AND entity = 'venda' AND entity_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId.ToString());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string GetAuditDetails(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(details,'') FROM audit_log
            WHERE action = 'trocar_item' AND entity = 'venda' AND entity_id = $id
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", saleId.ToString());
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static List<string> GetCash(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(payment_type,'') FROM cash_movements
            WHERE ref_type = 'sale' AND ref_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static int ScalarInt(string sql, int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double ScalarDouble(string sql, int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static string ScalarString(string sql, int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
}
