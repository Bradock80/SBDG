using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Caracterização do comportamento atual de PdvService.SwapSaleItem.
/// Não altera produção — documenta efeitos observados no DB.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvSwapSaleItemTests
{
    private static void EnsureStandalone()
    {
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
    }

    [Fact]
    public void SwapSaleItem_Simples_AparaB_AtualizaItemEstoqueTotalECaixa()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(100, 10, 4, "A001", "Produto A");
        var b = TestDataHelper.SeedSimpleProduct(50, 12, 5, "B001", "Produto B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);

        var result = PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 24 }],
            cashReceived: 24);

        Assert.Equal(sale.SaleId, result.Sale.Id);
        Assert.Equal(24, GetSaleTotal(sale.SaleId)); // 2 * 12
        Assert.Null(result.RefundHint);
        Assert.Contains("diferença", result.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(b, GetItemProductId(itemId));
        Assert.Equal(2, GetItemQty(itemId));
        Assert.Equal(2, GetItemStockQty(itemId)); // simples: stock_qty == quantity
        Assert.Equal(12, GetItemUnitPrice(itemId));
        Assert.Equal(24, GetItemSubtotal(itemId));

        Assert.Equal(100, TestDataHelper.GetProductStock(a)); // restaurado
        Assert.Equal(48, TestDataHelper.GetProductStock(b)); // 50 - 2

        Assert.True(CountMovements(a, "sale_edit", sale.SaleId) >= 1);
        Assert.True(CountMovements(b, "sale_edit", sale.SaleId) >= 1);

        var cash = GetCashMovements(sale.SaleId);
        // ETAPA 24.5: operador confirmou Dinheiro 24 — sem inventar Pix.
        Assert.Single(cash);
        Assert.Equal("Dinheiro", cash[0].PaymentType);
        Assert.Equal(24, cash[0].AmountIn);
    }

    [Fact]
    public void SwapSaleItem_FatorDiferente_RestauraEBaixaFisicoCorreto()
    {
        // A vendido com fator 10 (stock_qty=10); B cigarro/maço fator 20 via ResolveManualSale.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(100, 10, 4, "A10", "Produto A");
        var b = SeedCigaretteProduct("B20", "Marlboro Box", stock: 100, salePrice: 12, fator: 20);

        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = a,
                    Quantity = 1,
                    UnitPrice = 10,
                    StockUnitsPerSale = 10,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 10,
        });
        Assert.Equal(90, TestDataHelper.GetProductStock(a));
        Assert.Equal(10, GetItemStockQty(GetSaleItemId(sale.SaleId)));
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(100, TestDataHelper.GetProductStock(a)); // +10
        Assert.Equal(80, TestDataHelper.GetProductStock(b)); // -20
        Assert.Equal(1, GetItemQty(itemId));
        Assert.Equal(20, GetItemStockQty(itemId));
    }

    [Fact]
    public void SwapSaleItem_StockUnitsPerSale_RestauraStockQtyEAtualizaLinha()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var cigarro = TestDataHelper.SeedSimpleProduct(100, 10, 4, "CIG", "Cigarro unidade");
        var outro = TestDataHelper.SeedSimpleProduct(50, 10, 4, "OUT", "Outro");

        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = cigarro,
                    Quantity = 1,
                    UnitPrice = 10,
                    StockUnitsPerSale = 20,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 10,
        });
        Assert.Equal(80, TestDataHelper.GetProductStock(cigarro));
        Assert.Equal(20, GetItemStockQty(GetSaleItemId(sale.SaleId)));
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, outro, keepLinePrice: true);

        Assert.Equal(100, TestDataHelper.GetProductStock(cigarro)); // +20
        Assert.Equal(49, TestDataHelper.GetProductStock(outro)); // -1
        Assert.Equal(1, GetItemQty(itemId));
        Assert.Equal(1, GetItemStockQty(itemId));
    }

    [Fact]
    public void SwapSaleItem_DepoisCancelSale_RestauraEstoqueExatamente()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        TestDataHelper.GrantPdvCancelPermission();
        const double stockA = 100;
        const double stockB = 50;
        var a = TestDataHelper.SeedSimpleProduct(stockA, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(stockB, 10, 4, "B", "B");

        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = a,
                    Quantity = 1,
                    UnitPrice = 10,
                    StockUnitsPerSale = 20,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 10,
        });
        Assert.Equal(80, TestDataHelper.GetProductStock(a));
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);
        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(49, TestDataHelper.GetProductStock(b));
        Assert.Equal(1, GetItemStockQty(itemId));

        PdvService.CancelSale(sale.SaleId);

        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(stockB, TestDataHelper.GetProductStock(b));
    }

    [Fact]
    public void SwapSaleItem_NewQuantity_ComFator_AtualizaStockQty()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var cig = SeedCigaretteProduct("MACO", "Marlboro Red", stock: 200, salePrice: 15, fator: 20);
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = cig,
                    Quantity = 1,
                    UnitPrice = 15,
                    StockUnitsPerSale = 20,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 15,
        });
        Assert.Equal(180, TestDataHelper.GetProductStock(cig));
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, cig, keepLinePrice: true, newQuantity: 3,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 45 }],
            cashReceived: 45);

        // restore 20 + deduct 60 → líquido -40 a partir de 180 → 140
        Assert.Equal(140, TestDataHelper.GetProductStock(cig));
        Assert.Equal(3, GetItemQty(itemId));
        Assert.Equal(60, GetItemStockQty(itemId));
    }

    [Fact]
    public void SwapSaleItem_KeepLinePriceTrue_PreservaPrecoAntigo()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 99, 5, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        var result = PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(10, GetItemUnitPrice(itemId));
        Assert.Equal(10, GetSaleTotal(sale.SaleId));
        Assert.Equal(b, GetItemProductId(itemId));
        Assert.Contains("atualizados", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.RefundHint);
    }

    [Fact]
    public void SwapSaleItem_KeepLinePriceFalse_UsaSalePriceDoProdutoNovo()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 7, 3, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        var result = PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 7 }],
            cashReceived: 7);

        Assert.Equal(7, GetItemUnitPrice(itemId));
        Assert.Equal(7, GetSaleTotal(sale.SaleId));
        Assert.Equal(3, result.RefundHint);
        Assert.Contains("Devolver", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SwapSaleItem_NewQuantity_AlteraQuantidadeESubtotal()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(30, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(30, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true, newQuantity: 5,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 50 }],
            cashReceived: 50);

        Assert.Equal(5, GetItemQty(itemId));
        Assert.Equal(5, GetItemStockQty(itemId));
        Assert.Equal(50, GetItemSubtotal(itemId));
        Assert.Equal(50, GetSaleTotal(sale.SaleId));
        Assert.Equal(30, TestDataHelper.GetProductStock(a));
        Assert.Equal(25, TestDataHelper.GetProductStock(b));
    }

    [Fact]
    public void SwapSaleItem_NewQuantityNull_MantemQuantidadeAntiga()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 3, 10, 30);
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true, newQuantity: null);

        Assert.Equal(3, GetItemQty(itemId));
    }

    [Fact]
    public void SwapSaleItem_NewQuantityZeroOuNegativo_UsaQtyAntiga()
    {
        // Comportamento atual: newQuantity is > 0 ? ... : oldQty
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true, newQuantity: 0);
        Assert.Equal(2, GetItemQty(itemId));

        var c = TestDataHelper.SeedSimpleProduct(20, 10, 4, "C", "C");
        PdvService.SwapSaleItem(sale.SaleId, itemId, c, keepLinePrice: true, newQuantity: -5);
        Assert.Equal(2, GetItemQty(itemId));
        Assert.Equal(c, GetItemProductId(itemId));
    }

    [Fact]
    public void SwapSaleItem_ProdutoMaisBarato_RecalculaTrocoDinheiro()
    {
        // cash_received só fica gravado quando há overpay na finalização.
        // Venda 100 recebidos 150 (troco 50) → troca total 80 → troco recalculado 70.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(200, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 80, 30, "B", "B");
        var sale = FinalizeCash(a, 1, 100, cashReceived: 150);
        var itemId = GetSaleItemId(sale.SaleId);

        Assert.Equal(150, GetCashReceived(sale.SaleId));
        Assert.Equal(50, GetChangeAmount(sale.SaleId));

        var result = PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 80 }],
            cashReceived: 150);

        Assert.Equal(80, GetSaleTotal(sale.SaleId));
        Assert.Equal(150, GetCashReceived(sale.SaleId));
        Assert.Equal(70, GetChangeAmount(sale.SaleId));
        Assert.Equal(20, result.RefundHint);
        Assert.Equal(80, GetCashMovements(sale.SaleId).Single().AmountIn);
    }

    [Fact]
    public void SwapSaleItem_ProdutoMaisCaro_ComConfirmacaoDinheiro_NaoInventaPix()
    {
        // ETAPA 24.5: total sobe → exige confirmedPayments; não inventa Pix.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(200, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 120, 50, "B", "B");
        var sale = FinalizeCash(a, 1, 100, cashReceived: 100);
        var itemId = GetSaleItemId(sale.SaleId);
        Assert.Null(GetCashReceived(sale.SaleId));

        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false));

        var result = PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 120 }],
            cashReceived: 120);

        Assert.Equal(120, GetSaleTotal(sale.SaleId));
        Assert.Null(result.RefundHint);
        Assert.Contains("Diferença", result.Message, StringComparison.OrdinalIgnoreCase);

        var cash = GetCashMovements(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("Dinheiro", cash[0].PaymentType);
        Assert.Equal(120, cash[0].AmountIn);
        Assert.Equal("Dinheiro", GetPaymentType(sale.SaleId));
    }

    [Fact]
    public void SwapSaleItem_Pix_AjustaUnicoMovimentoAoNovoTotal()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 7, 3, "B", "B");
        var sale = FinalizePayment(a, 1, 10, "Pix", 0);
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Pix", Amount = 7 }]);

        Assert.Equal("Pix", GetPaymentType(sale.SaleId));
        var cash = GetCashMovements(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("Pix", cash[0].PaymentType);
        Assert.Equal(7, cash[0].AmountIn);
        Assert.Null(GetCashReceived(sale.SaleId));
        Assert.Equal(0, GetChangeAmount(sale.SaleId));
    }

    [Fact]
    public void SwapSaleItem_Misto_TotalMenor_ExigeConfirmacaoExplicita()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(40, 30, 10, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(40, 15, 5, "B", "B");
        var sale = FinalizeMixed(a, 1, 30, dinheiro: 10, pix: 20);
        var itemId = GetSaleItemId(sale.SaleId);

        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false));

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments:
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 5 },
                new PdvPaymentPart { PaymentType = "Pix", Amount = 10 },
            ],
            cashReceived: 5);

        Assert.Equal(15, GetSaleTotal(sale.SaleId));
        var cash = GetCashMovements(sale.SaleId);
        Assert.Equal(2, cash.Count);
        Assert.Contains(cash, c => c.PaymentType == "Dinheiro" && Math.Abs(c.AmountIn - 5) < 0.02);
        Assert.Contains(cash, c => c.PaymentType == "Pix" && Math.Abs(c.AmountIn - 10) < 0.02);
    }

    [Fact]
    public void SwapSaleItem_Misto_TotalMaior_ExigeConfirmacaoExplicita()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(40, 30, 10, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(40, 40, 12, "B", "B");
        var sale = FinalizeMixed(a, 1, 30, dinheiro: 10, pix: 20);
        var itemId = GetSaleItemId(sale.SaleId);

        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false));

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments:
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 10 },
                new PdvPaymentPart { PaymentType = "Pix", Amount = 30 },
            ],
            cashReceived: 10);

        Assert.Equal(40, GetSaleTotal(sale.SaleId));
        var cash = GetCashMovements(sale.SaleId);
        Assert.Contains(cash, c => c.PaymentType == "Dinheiro" && c.AmountIn == 10);
        Assert.Contains(cash, c => c.PaymentType == "Pix" && c.AmountIn == 30);
    }

    [Fact]
    public void SwapSaleItem_Fiado_AtualizaTotalESaldoDerivado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var customerId = SeedCustomer("Cliente Swap");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 7, 3, "B", "B");
        var sale = FinalizePayment(a, 1, 10, "Fiado", 0, customerId);
        Assert.Equal(10, FiadoService.GetDetail(customerId).Balance);
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false);

        Assert.Equal(customerId, GetCustomerId(sale.SaleId));
        Assert.Equal(7, GetSaleTotal(sale.SaleId));
        Assert.Equal(7, FiadoService.GetDetail(customerId).Balance);
        var cash = GetCashMovements(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("venda_fiado", cash[0].Kind.ToLowerInvariant());
        Assert.Equal(7, cash[0].AmountIn);
        Assert.Equal(0, CountTable("fiado_payments"));
    }

    [Fact]
    public void SwapSaleItem_DescontoOriginal_PreservaAjusteLiquido()
    {
        // sales não guarda discount; ajuste líquido = oldTotal − SUM(subtotal) é reaplicado.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = a,
                    Quantity = 2,
                    UnitPrice = 10,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 15,
            Discount = 5,
        });
        Assert.Equal(15, sale.Total);
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        // ETAPA 24.3: total permanece 15 (ajuste −5 preservado sobre bruto 20).
        Assert.Equal(15, GetSaleTotal(sale.SaleId));
    }

    [Fact]
    public void SwapSaleItem_Composicao_RestauraComponenteEBaixaNovo()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var componentId = TestDataHelper.SeedSimpleProduct(100, 1, 0.5, "CMP", "Componente");
        var kitId = SeedCompositionProduct("KIT", "Kit", salePrice: 20, componentId, componentQty: 3);
        var simpleId = TestDataHelper.SeedSimpleProduct(40, 15, 5, "S", "Simples");

        var sale = FinalizeCash(kitId, 2, 20, cashReceived: 40); // baixa 2*3=6 componentes
        Assert.Equal(94, TestDataHelper.GetProductStock(componentId));
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, simpleId, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 30 }],
            cashReceived: 30);

        Assert.Equal(100, TestDataHelper.GetProductStock(componentId)); // restaurado
        Assert.Equal(38, TestDataHelper.GetProductStock(simpleId)); // 40 - 2
        Assert.Equal(simpleId, GetItemProductId(itemId));
        Assert.Equal(2, GetItemStockQty(itemId));
        Assert.Equal(30, GetSaleTotal(sale.SaleId)); // 2 * 15
    }

    [Fact]
    public void SwapSaleItem_SimplesParaComposicao_BaixaComponentes()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var componentId = TestDataHelper.SeedSimpleProduct(100, 1, 0.5, "CMP2", "Componente2");
        var kitId = SeedCompositionProduct("KIT2", "Kit2", salePrice: 25, componentId, componentQty: 4);
        var simpleId = TestDataHelper.SeedSimpleProduct(40, 15, 5, "S2", "Simples2");

        var sale = FinalizeCash(simpleId, 1, 15, cashReceived: 15);
        Assert.Equal(39, TestDataHelper.GetProductStock(simpleId));
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, kitId, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 25 }],
            cashReceived: 25);

        Assert.Equal(40, TestDataHelper.GetProductStock(simpleId)); // restaurado
        Assert.Equal(96, TestDataHelper.GetProductStock(componentId)); // 100 - 4
        Assert.Equal(kitId, GetItemProductId(itemId));
        Assert.Equal(1, GetItemQty(itemId));
        Assert.Equal(1, GetItemStockQty(itemId)); // kit: stock_qty comercial
    }

    [Fact]
    public void SwapSaleItem_VendaCancelada_Bloqueia()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        TestDataHelper.GrantPdvCancelPermission();
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);
        PdvService.CancelSale(sale.SaleId);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false));
        Assert.Contains("cancelada", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(a, GetItemProductId(itemId));
    }

    [Fact]
    public void SwapSaleItem_VendaOutraData_Bloqueia()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);
        SetSaleSessionDate(sale.SaleId, DateTime.Today.AddDays(-1));

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false));
        Assert.Contains("hoje", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SwapSaleItem_ItemInexistente_NaoAlteraEstado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var stockA = TestDataHelper.GetProductStock(a);
        var cashBefore = CountCashMovements(sale.SaleId);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, 999999, b, keepLinePrice: false));
        Assert.Contains("não encontrado", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(cashBefore, CountCashMovements(sale.SaleId));
        Assert.Equal(10, GetSaleTotal(sale.SaleId));
    }

    [Fact]
    public void SwapSaleItem_ProdutoNovoInexistente_EstadoIntacto()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);
        var stockA = TestDataHelper.GetProductStock(a);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, 999999, keepLinePrice: false));
        Assert.Contains("não encontrado", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(a, GetItemProductId(itemId));
        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(10, GetSaleTotal(sale.SaleId));
    }

    [Fact]
    public void SwapSaleItem_MesmoProdutoMesmaQty_Bloqueia()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, a, keepLinePrice: true));
        Assert.Contains("diferente", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SwapSaleItem_MesmoProdutoQtyDiferente_PermiteEGeraMovimentos()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 2, 10, 20);
        Assert.Equal(18, TestDataHelper.GetProductStock(a));
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, a, keepLinePrice: true, newQuantity: 5,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 50 }],
            cashReceived: 50);

        // restore 2 + deduct 5 = líquido -3 a partir de 18 → 15
        Assert.Equal(15, TestDataHelper.GetProductStock(a));
        Assert.Equal(5, GetItemQty(itemId));
        Assert.Equal(5, GetItemStockQty(itemId));
        Assert.Equal(50, GetSaleTotal(sale.SaleId));
        Assert.True(CountMovements(a, "sale_edit", sale.SaleId) >= 2);
    }

    [Fact]
    public void SwapSaleItem_Client_NaoAlteraVendaLocal()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);
        var stockA = TestDataHelper.GetProductStock(a);
        var stockB = TestDataHelper.GetProductStock(b);
        var movBefore = CountAllMovements();
        var cashBefore = CountCashMovements(sale.SaleId);

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false));

        Assert.Equal(a, GetItemProductId(itemId));
        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(stockB, TestDataHelper.GetProductStock(b));
        Assert.Equal(movBefore, CountAllMovements());
        Assert.Equal(cashBefore, CountCashMovements(sale.SaleId));
        Assert.Equal(0, CountAuditTrocarItem(sale.SaleId));

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
    }

    [Fact]
    public void SwapSaleItem_Sucesso_GeraAuditTrocarItem()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false);

        Assert.Equal(1, CountAuditTrocarItem(sale.SaleId));
        var details = GetAuditTrocarItemDetails(sale.SaleId);
        Assert.Contains("old_product_id", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new_product_id", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("old_total", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new_total", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("old_payment_type", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new_payment_type", details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SwapSaleItem_Gestor_PermiteEGeraAudit()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);
        TestDataHelper.SetSessionRole("gestor");

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false);

        Assert.Equal(b, GetItemProductId(itemId));
        Assert.Equal(1, CountAuditTrocarItem(sale.SaleId));
    }

    [Fact]
    public void SwapSaleItem_VendedorSemPermissao_NaoAlteraEstado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);
        var stockA = TestDataHelper.GetProductStock(a);
        var stockB = TestDataHelper.GetProductStock(b);
        var totalBefore = GetSaleTotal(sale.SaleId);
        var cashBefore = GetCashMovements(sale.SaleId);
        var movBefore = CountAllMovements();

        TestDataHelper.SetSessionRole("vendedor");

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false));
        Assert.Contains("permissão", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(a, GetItemProductId(itemId));
        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(stockB, TestDataHelper.GetProductStock(b));
        Assert.Equal(totalBefore, GetSaleTotal(sale.SaleId));
        Assert.Equal(cashBefore, GetCashMovements(sale.SaleId));
        Assert.Equal(movBefore, CountAllMovements());
        Assert.Equal(0, CountAuditTrocarItem(sale.SaleId));
    }

    [Fact]
    public void SwapSaleItem_VendedorComOverride_PodeExecutar()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 10, 4, "B", "B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = GetSaleItemId(sale.SaleId);

        TestDataHelper.SetSessionCustomPermissions("vendedor", p => p.PdvEditarVenda = true);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false);

        Assert.Equal(b, GetItemProductId(itemId));
        Assert.Equal(1, CountAuditTrocarItem(sale.SaleId));
    }

    [Fact]
    public void SwapSaleItem_Falha_NaoGeraAuditTrocarItem()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var broken = TestDataHelper.SeedSimpleProduct(20, 12, 5, "BRK", "Quebrado");
        CorruptAsEmptyComposition(broken);
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);

        Assert.ThrowsAny<Exception>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, broken, keepLinePrice: false));

        Assert.Equal(0, CountAuditTrocarItem(sale.SaleId));
    }

    [Fact]
    public void SwapSaleItem_FalhaAposRestore_RollbackCompleto()
    {
        // Falha natural: produto novo com composição marcada sem itens → throw após restore do antigo.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A", "A");
        var broken = TestDataHelper.SeedSimpleProduct(20, 12, 5, "BRK", "Quebrado");
        CorruptAsEmptyComposition(broken);

        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);
        var stockA = TestDataHelper.GetProductStock(a);
        var stockB = TestDataHelper.GetProductStock(broken);
        var totalBefore = GetSaleTotal(sale.SaleId);
        var stockQtyBefore = GetItemStockQty(itemId);
        var cashBefore = GetCashMovements(sale.SaleId);
        var movBefore = CountAllMovements();

        Assert.ThrowsAny<Exception>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, broken, keepLinePrice: false));

        Assert.Equal(a, GetItemProductId(itemId));
        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(stockB, TestDataHelper.GetProductStock(broken));
        Assert.Equal(totalBefore, GetSaleTotal(sale.SaleId));
        Assert.Equal(stockQtyBefore, GetItemStockQty(itemId));
        Assert.Equal(cashBefore, GetCashMovements(sale.SaleId));
        Assert.Equal(movBefore, CountAllMovements());
        Assert.Equal(0, CountAuditTrocarItem(sale.SaleId));
    }

    // ——— helpers ———

    private static PdvFinalizeResult FinalizeCash(int productId, double qty, double unitPrice, double cashReceived) =>
        FinalizePayment(productId, qty, unitPrice, "Dinheiro", cashReceived);

    private static PdvFinalizeResult FinalizePayment(
        int productId, double qty, double unitPrice, string paymentType,
        double cashReceived, int? customerId = null)
    {
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = unitPrice,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = paymentType,
            Payments = [new PdvPaymentPart { PaymentType = paymentType, Amount = qty * unitPrice }],
            CashReceived = cashReceived,
            CustomerPersonId = customerId,
        });
    }

    private static PdvFinalizeResult FinalizeMixed(int productId, double qty, double unitPrice, double dinheiro, double pix)
    {
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Quantity = qty,
                    UnitPrice = unitPrice,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            Payments =
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = dinheiro },
                new PdvPaymentPart { PaymentType = "Pix", Amount = pix },
            ],
            CashReceived = dinheiro,
        });
    }

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

    private static int SeedCompositionProduct(
        string code, string name, double salePrice, int componentId, double componentQty)
    {
        var extra = new ProductExtra
        {
            Composicao = true,
            ComposicaoItens =
            [
                new ProductCompositionItem
                {
                    ProductId = componentId,
                    Quantity = componentQty,
                    Code = "CMP",
                    Name = "Componente",
                    Unit = "UN",
                },
            ],
        }.ToJson();

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $name, 'UN', $sale, 0, 0, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$sale", salePrice);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCigaretteProduct(
        string code, string name, double stock, double salePrice, double fator)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            PrecoAtacado = salePrice,
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
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$sale", salePrice);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", salePrice * 0.6);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void CorruptAsEmptyComposition(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE products SET extra_json = '{"composicao":true,"composicao_itens":[]}'
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static void SetSaleSessionDate(int saleId, DateTime date)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sales SET session_date = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$id", saleId);
        cmd.ExecuteNonQuery();
    }

    private static int GetSaleItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetItemProductId(int itemId) => ScalarInt("SELECT product_id FROM sale_items WHERE id = $id;", itemId);
    private static double GetItemQty(int itemId) => ScalarDouble("SELECT quantity FROM sale_items WHERE id = $id;", itemId);
    private static double GetItemUnitPrice(int itemId) => ScalarDouble("SELECT unit_price FROM sale_items WHERE id = $id;", itemId);
    private static double GetItemSubtotal(int itemId) => ScalarDouble("SELECT subtotal FROM sale_items WHERE id = $id;", itemId);
    private static double GetItemStockQty(int itemId) => ScalarDouble("SELECT IFNULL(stock_qty,0) FROM sale_items WHERE id = $id;", itemId);
    private static double GetSaleTotal(int saleId) => ScalarDouble("SELECT total FROM sales WHERE id = $id;", saleId);
    private static string GetPaymentType(int saleId) => ScalarString("SELECT payment_type FROM sales WHERE id = $id;", saleId);

    private static double? GetCashReceived(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cash_received FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToDouble(v);
    }

    private static double GetChangeAmount(int saleId) =>
        ScalarDouble("SELECT IFNULL(change_amount,0) FROM sales WHERE id = $id;", saleId);

    private static int? GetCustomerId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_id FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToInt32(v);
    }

    private static int CountCashMovements(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountMovements(int productId, string refType, int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE product_id = $pid
              AND IFNULL(ref_type,'') = $rt
              AND ref_id = $sale;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$rt", refType);
        cmd.Parameters.AddWithValue("$sale", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountAllMovements()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movements;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountTable(string table)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountAuditTrocarItem(int saleId)
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

    private static string GetAuditTrocarItemDetails(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(details,'') FROM audit_log
            WHERE action = 'trocar_item' AND entity = 'venda' AND entity_id = $id
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", saleId.ToString());
        return (string)(cmd.ExecuteScalar() ?? "");
    }

    private static List<(string Kind, string PaymentType, double AmountIn)> GetCashMovements(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT kind, IFNULL(payment_type,''), IFNULL(amount_in,0)
            FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<(string, string, double)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetString(1), r.GetDouble(2)));
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
        return (string)(cmd.ExecuteScalar() ?? "");
    }
}
