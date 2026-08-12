using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 24.5 — política híbrida de pagamento no SwapSaleItem.
/// Fiado puro auto-ajusta; demais formas exigem confirmedPayments quando o total muda.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvSwapSaleItemPaymentAdjustmentTests
{
    private static void EnsureStandalone()
    {
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
    }

    private static PdvPaymentPart[] Pay(string type, double amount) =>
        [new PdvPaymentPart { PaymentType = type, Amount = amount }];

    // ─── Preview / total igual ──────────────────────────────────────

    [Fact]
    public void Preview_TotalIgual_NaoExigeConfirmacao()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 100);
        var b = Seed(20, 100, "B", "B");
        var sale = Finalize(a, 1, 100, "Dinheiro", cash: 90, discount: 10);

        var preview = PdvService.PreviewSwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: true);

        Assert.Equal(90, preview.OldTotal);
        Assert.Equal(90, preview.NewTotal);
        Assert.Equal(0, preview.Difference);
        Assert.False(preview.RequiresPaymentConfirmation);
        Assert.False(preview.IsPureFiado);
    }

    [Fact]
    public void TotalIgual_PartesPagamentoECaixaIntactos()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 100);
        var b = Seed(20, 100, "B", "B");
        var sale = Finalize(a, 1, 100, "Dinheiro", cash: 90, discount: 10);
        var cashBefore = Cash(sale.SaleId);

        var result = PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: true);

        Assert.Equal(90, Total(sale.SaleId));
        Assert.Null(result.RefundHint);
        Assert.Equal(cashBefore, Cash(sale.SaleId));
    }

    // ─── Sem confirmação → bloqueia ─────────────────────────────────

    [Fact]
    public void Aumento_Dinheiro_SemConfirmacao_BloqueiaENaoPersiste()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(200, "teste");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = Finalize(a, 1, 90, "Dinheiro", cash: 90);
        var stockA = TestDataHelper.GetProductStock(a);
        var stockB = TestDataHelper.GetProductStock(b);
        var itemId = ItemId(sale.SaleId);

        var preview = PdvService.PreviewSwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false);
        Assert.True(preview.RequiresPaymentConfirmation);
        Assert.Equal(20, preview.Difference);

        var ex = Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false));
        Assert.Contains("Confirme a nova forma de pagamento", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(90, Total(sale.SaleId));
        Assert.Equal(a, ItemProduct(itemId));
        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(stockB, TestDataHelper.GetProductStock(b));
        Assert.Single(Cash(sale.SaleId));
        Assert.DoesNotContain(Cash(sale.SaleId), c => c.PaymentType == "Pix");
    }

    [Fact]
    public void Aumento_Dinheiro_ComConfirmacao_PersisteExatamente()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(200, "teste");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = Finalize(a, 1, 90, "Dinheiro", cash: 90);

        var result = PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: Pay("Dinheiro", 110), cashReceived: 110);

        Assert.Equal(110, Total(sale.SaleId));
        Assert.Null(result.RefundHint);
        var cash = Cash(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("Dinheiro", cash[0].PaymentType);
        Assert.Equal(110, cash[0].AmountIn);
        Assert.Equal("Dinheiro", PayType(sale.SaleId));
        Assert.DoesNotContain(cash, c => c.PaymentType == "Pix");
    }

    [Fact]
    public void Aumento_Pix_SemConfirmacao_Bloqueia()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = Finalize(a, 1, 90, "Pix", cash: 0);

        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false));
        Assert.Equal(90, Total(sale.SaleId));
    }

    [Fact]
    public void Aumento_Pix_ComConfirmacao_Pix110()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = Finalize(a, 1, 90, "Pix", cash: 0);

        PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: Pay("Pix", 110));

        Assert.Equal(110, Total(sale.SaleId));
        Assert.Equal("Pix", PayType(sale.SaleId));
        Assert.Single(Cash(sale.SaleId));
        Assert.Equal(110, Cash(sale.SaleId)[0].AmountIn);
    }

    [Fact]
    public void Aumento_Pix_ComConfirmacao_MistoExplicitamente()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = Finalize(a, 1, 90, "Pix", cash: 0);

        PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments:
            [
                new PdvPaymentPart { PaymentType = "Pix", Amount = 90 },
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 20 },
            ],
            cashReceived: 20);

        var cash = Cash(sale.SaleId);
        Assert.Equal(2, cash.Count);
        Assert.Contains(cash, c => c.PaymentType == "Pix" && c.AmountIn == 90);
        Assert.Contains(cash, c => c.PaymentType == "Dinheiro" && c.AmountIn == 20);
    }

    [Fact]
    public void Aumento_CartaoDebito_NuncaInventaPix()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = Finalize(a, 1, 90, "Cartão Débito", cash: 0);

        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false));

        PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: Pay("Cartão Débito", 110));

        Assert.Equal("Cartão Débito", PayType(sale.SaleId));
        Assert.Single(Cash(sale.SaleId));
        Assert.DoesNotContain(Cash(sale.SaleId), c => c.PaymentType == "Pix");
    }

    // ─── Fiado puro ─────────────────────────────────────────────────

    [Fact]
    public void Aumento_FiadoPuro_AjustaDividaSemPix()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var customerId = SeedCustomer("Cliente Fiado+");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = Finalize(a, 1, 90, "Fiado", cash: 0, customerId: customerId);

        var preview = PdvService.PreviewSwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false);
        Assert.True(preview.IsPureFiado);
        Assert.False(preview.RequiresPaymentConfirmation);

        var result = PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false);

        Assert.Equal(110, Total(sale.SaleId));
        Assert.Equal(customerId, CustomerId(sale.SaleId));
        Assert.Null(result.RefundHint);
        Assert.Contains("Fiado atualizado", result.Message);
        Assert.Equal(110, FiadoService.GetDetail(customerId).Balance);
        var cash = Cash(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("Fiado", cash[0].PaymentType);
        Assert.Equal(110, cash[0].AmountIn);
        Assert.Equal("venda_fiado", cash[0].Kind);
        Assert.DoesNotContain(cash, c => c.PaymentType == "Pix");
    }

    [Fact]
    public void Reducao_FiadoPuro_AtualizaSaldo_SemRefundHintFisico()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var customerId = SeedCustomer("Cliente Fiado-");
        var a = Seed(20, 100);
        var b = Seed(20, 80, "B", "B");
        var sale = Finalize(a, 1, 100, "Fiado", cash: 0, customerId: customerId);

        var result = PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false);

        Assert.Equal(80, Total(sale.SaleId));
        Assert.Equal(80, FiadoService.GetDetail(customerId).Balance);
        Assert.Null(result.RefundHint); // não é devolução física
        Assert.DoesNotContain("Devolver", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Fiado", PayType(sale.SaleId));
    }

    // ─── Misto / redução ────────────────────────────────────────────

    [Fact]
    public void Misto_Aumento_SemConfirmacao_Bloqueia()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = FinalizeMixed(a, 1, 90, dinheiro: 40, pix: 50);

        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false));
    }

    [Fact]
    public void Misto_Aumento_ComConfirmacao_PersisteEscolha()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = FinalizeMixed(a, 1, 90, dinheiro: 40, pix: 50);

        PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments:
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 40 },
                new PdvPaymentPart { PaymentType = "Pix", Amount = 70 },
            ],
            cashReceived: 40);

        Assert.Equal(110, Total(sale.SaleId));
        var cash = Cash(sale.SaleId);
        Assert.Contains(cash, c => c.PaymentType == "Dinheiro" && c.AmountIn == 40);
        Assert.Contains(cash, c => c.PaymentType == "Pix" && c.AmountIn == 70);
    }

    [Fact]
    public void Reducao_Pix_SemConfirmacao_Bloqueia_ComConfirmacaoPix80()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 100);
        var b = Seed(20, 80, "B", "B");
        var sale = Finalize(a, 1, 100, "Pix", cash: 0);

        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false));

        var result = PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: Pay("Pix", 80));

        Assert.Equal(80, Total(sale.SaleId));
        Assert.Equal(20, result.RefundHint);
        Assert.Equal(80, Cash(sale.SaleId).Single().AmountIn);
        Assert.Equal(0, CountCashOut(sale.SaleId));
    }

    [Fact]
    public void Reducao_Dinheiro_ComConfirmacao_Caixa80_RefundHint20()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(200, "teste");
        var a = Seed(20, 100);
        var b = Seed(20, 80, "B", "B");
        var sale = Finalize(a, 1, 100, "Dinheiro", cash: 100);

        var result = PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: Pay("Dinheiro", 80), cashReceived: 80);

        Assert.Equal(80, Total(sale.SaleId));
        Assert.Equal(20, result.RefundHint);
        Assert.Single(Cash(sale.SaleId));
        Assert.Equal(80, Cash(sale.SaleId)[0].AmountIn);
        Assert.DoesNotContain(Cash(sale.SaleId), c => c.PaymentType == "Pix");
    }

    [Fact]
    public void Reducao_DinheiroComOverpay_Confirmado_RecalculaTroco()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(200, "teste");
        var a = Seed(20, 100);
        var b = Seed(20, 80, "B", "B");
        var sale = Finalize(a, 1, 100, "Dinheiro", cash: 150);

        var result = PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: Pay("Dinheiro", 80), cashReceived: 150);

        Assert.Equal(80, Total(sale.SaleId));
        Assert.Equal(20, result.RefundHint);
        Assert.Equal(150, CashReceived(sale.SaleId));
        Assert.Equal(70, Change(sale.SaleId));
    }

    [Fact]
    public void FiadoMaisDinheiro_NaoEPuro_ExigeConfirmacao()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var customerId = SeedCustomer("Misto Fiado");
        var a = Seed(20, 90);
        var b = Seed(20, 110, "B", "B");
        var sale = FinalizeParts(a, 1, 90, customerId,
            ("Fiado", 50), ("Dinheiro", 40));

        var preview = PdvService.PreviewSwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false);
        Assert.False(preview.IsPureFiado);
        Assert.True(preview.RequiresPaymentConfirmation);
        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false));
    }

    // ─── Centavos / total zero ──────────────────────────────────────

    [Fact]
    public void Diff_001_ExigeConfirmacao_SemGapEntreTotalEPartes()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 100);
        var b = Seed(20, 100.01, "B", "B");
        var sale = Finalize(a, 1, 100, "Dinheiro", cash: 100);

        var preview = PdvService.PreviewSwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false);
        Assert.True(preview.RequiresPaymentConfirmation);
        Assert.Equal(0.01, preview.Difference);

        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false));

        PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: Pay("Dinheiro", 100.01), cashReceived: 100.01);

        Assert.Equal(100.01, Total(sale.SaleId));
        Assert.Equal(100.01, Cash(sale.SaleId).Single().AmountIn);
    }

    [Fact]
    public void Diff_002_ExigeConfirmacao()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 100);
        var b = Seed(20, 100.02, "B", "B");
        var sale = Finalize(a, 1, 100, "Dinheiro", cash: 100);

        Assert.True(PdvService.PreviewSwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, false)
            .RequiresPaymentConfirmation);
        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false));
    }

    [Fact]
    public void TotalZero_ComConfirmacaoDinheiroZero()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 50);
        var b = Seed(20, 0, "B", "Gratis");
        var sale = Finalize(a, 1, 50, "Dinheiro", cash: 50);

        var result = PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: Pay("Dinheiro", 0));

        // NormalizePaymentParts filtra Amount > 0 → pode falhar?
        // Se Amount 0 filtrado, Normalize lança "Informe ao menos uma forma"
        // Documentar: newTotal 0 com Dinheiro 0 — se filtrado, usar empty? Spec: coerente com modelo.
        // Na prática NormalizePaymentParts Where Amount > 0 → empty → throw.
        // Então total zero precisa de representação: talvez parts com amount 0 não passe.
        // Alternativa no service: newTotal == 0 permite parts vazias ou Dinheiro 0.
        Assert.Equal(0, Total(sale.SaleId));
        Assert.Equal(50, result.RefundHint);
    }

    [Fact]
    public void ConfirmacaoInvalida_RollbackCompleto()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 100);
        var b = Seed(20, 80, "B", "B");
        var sale = Finalize(a, 1, 100, "Dinheiro", cash: 100);
        var stockA = TestDataHelper.GetProductStock(a);
        var stockB = TestDataHelper.GetProductStock(b);
        var itemId = ItemId(sale.SaleId);

        // Soma 50 ≠ 80
        Assert.Throws<PdvException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
                confirmedPayments: Pay("Dinheiro", 50)));

        Assert.Equal(100, Total(sale.SaleId));
        Assert.Equal(a, ItemProduct(itemId));
        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(stockB, TestDataHelper.GetProductStock(b));
        Assert.Equal(100, Cash(sale.SaleId).Single().AmountIn);
    }

    [Fact]
    public void Swap_NaoChamaMercadoPago()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = Seed(20, 50);
        var b = Seed(20, 70, "B", "B");
        var sale = Finalize(a, 1, 50, "Pix", cash: 0);

        PdvService.SwapSaleItem(sale.SaleId, ItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: Pay("Pix", 70));

        Assert.Null(GetSalesNotes(sale.SaleId));
    }

    // ─── helpers ────────────────────────────────────────────────────

    private static int Seed(double stock, double price, string code = "A", string name = "A") =>
        TestDataHelper.SeedSimpleProduct(stock, price, price * 0.4, code, name);

    private static PdvFinalizeResult Finalize(
        int productId, double qty, double unitPrice, string payment,
        double cash, double discount = 0, int? customerId = null)
    {
        var total = ProductPriceHelper.RoundPrice(qty * unitPrice - discount);
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
            PaymentType = payment,
            Payments = [new PdvPaymentPart { PaymentType = payment, Amount = total }],
            CashReceived = cash,
            Discount = discount,
            CustomerPersonId = customerId,
        });
    }

    private static PdvFinalizeResult FinalizeMixed(
        int productId, double qty, double unitPrice, double dinheiro, double pix)
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

    private static PdvFinalizeResult FinalizeParts(
        int productId, double qty, double unitPrice, int? customerId,
        params (string Type, double Amount)[] parts)
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
            PaymentType = parts[0].Type,
            Payments = parts.Select(p => new PdvPaymentPart { PaymentType = p.Type, Amount = p.Amount }).ToList(),
            CashReceived = parts.Where(p => p.Type.Equals("Dinheiro", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount),
            CustomerPersonId = customerId,
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

    private static int ItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int ItemProduct(int itemId) => (int)ScalarD("SELECT product_id FROM sale_items WHERE id = $id;", itemId);
    private static double Total(int saleId) => ScalarD("SELECT total FROM sales WHERE id = $id;", saleId);
    private static string PayType(int saleId) => ScalarS("SELECT payment_type FROM sales WHERE id = $id;", saleId);
    private static double? CashReceived(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cash_received FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToDouble(v);
    }
    private static double Change(int saleId) => ScalarD("SELECT IFNULL(change_amount,0) FROM sales WHERE id = $id;", saleId);
    private static int? CustomerId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_id FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToInt32(v);
    }

    private static List<(string PaymentType, double AmountIn, string Kind)> Cash(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(payment_type,''), IFNULL(amount_in,0), lower(IFNULL(kind,''))
            FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<(string, double, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetDouble(1), r.GetString(2)));
        return list;
    }

    private static int CountCashOut(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM cash_movements
            WHERE ref_type = 'sale' AND ref_id = $id AND IFNULL(amount_out,0) > 0.009;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string? GetSalesNotes(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT notes FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToString(v);
    }

    private static double ScalarD(string sql, int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static string ScalarS(string sql, int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        return (string)(cmd.ExecuteScalar() ?? "");
    }
}
