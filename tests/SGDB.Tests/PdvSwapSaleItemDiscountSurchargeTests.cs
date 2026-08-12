using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 24.3 — regressão: SwapSaleItem preserva o ajuste líquido original
/// (oldTotal − oldGross) ao recalcular o total da venda.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvSwapSaleItemDiscountSurchargeTests
{
    private static void EnsureStandalone()
    {
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin"); // PdvDesconto incluso
    }

    [Fact]
    public void Swap_SemAjustes_TotalIgualSomaDosItens()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 50, 20, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 50, 20, "B", "B");
        var sale = Finalize(a, 2, 50, cash: 100, discount: 0, surcharge: 0);
        Assert.Equal(100, sale.Total);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(100, GetTotal(sale.SaleId));
        Assert.Equal(100, SumItemSubtotals(sale.SaleId));
    }

    [Fact]
    public void Swap_DescontoFixo_PreservaAjusteLiquido()
    {
        // Bruto 100, desconto 10, total 90 → swap keep price (bruto 100) → total 90.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 100, 40, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 90, discount: 10, surcharge: 0);
        Assert.Equal(90, sale.Total);
        Assert.Equal(100, GetItemSubtotal(GetItemId(sale.SaleId))); // item guarda bruto
        Assert.False(SalesHasDiscountColumn());
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(90, GetTotal(sale.SaleId));
        Assert.Equal(100, SumItemSubtotals(sale.SaleId));
    }

    [Fact]
    public void Swap_Desconto_NovoItemMaisBarato_PreservaAjusteLiquido()
    {
        // Original: 100 − 10 = 90. Novo bruto 80 → total = 70.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 80, 30, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 90, discount: 10, surcharge: 0);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 70 }],
            cashReceived: 70);

        Assert.Equal(70, GetTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_Desconto_NovoItemMaisCaro_PreservaAjusteLiquido()
    {
        // Original: 100 − 10 = 90. Novo bruto 120 → total = 110.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 120, 50, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 90, discount: 10, surcharge: 0);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 110 }],
            cashReceived: 110);

        Assert.Equal(110, GetTotal(sale.SaleId));
    }

    [Fact]
    public void Finalize_DescontoPercentualNaUI_PersisteApenasValorAbsoluto()
    {
        // UI converte % → R$; FinalizeSale só recebe Discount absoluto.
        // Após persistir, percentual NÃO é reconstruível com segurança só do DB
        // (faltam colunas; audit da View é opcional e fora do Service).
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        // Equivalente a 10% de 100
        var sale = Finalize(a, 1, 100, cash: 90, discount: 10, surcharge: 0);
        Assert.Equal(90, sale.Total);
        Assert.Equal(100, GetItemSubtotal(GetItemId(sale.SaleId)));
        // Sem coluna/campo: impossível saber se foi R$ 10 fixo ou 10%.
        Assert.Null(GetSalesNotes(sale.SaleId));
    }

    [Fact]
    public void Swap_SurchargePuro_PreservaAjusteLiquido()
    {
        // Bruto 100 + surcharge 5 = 105 → swap keep → total 105.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 100, 40, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 105, discount: 0, surcharge: 5);
        Assert.Equal(105, sale.Total);
        Assert.Equal(100, GetItemSubtotal(GetItemId(sale.SaleId))); // surcharge NÃO no unit_price
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(105, GetTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_Surcharge_NovoItemMaisBarato_PreservaAjusteLiquido()
    {
        // oldGross 100, total 105 (+5). newGross 80 → 85.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 80, 30, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 105, discount: 0, surcharge: 5);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 85 }],
            cashReceived: 85);

        Assert.Equal(85, GetTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_Surcharge_NovoItemMaisCaro_PreservaAjusteLiquido()
    {
        // newGross 120 + 5 → 125.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 120, 50, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 105, discount: 0, surcharge: 5);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 125 }],
            cashReceived: 125);

        Assert.Equal(125, GetTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_DescontoESurcharge_PreservaAjusteLiquido()
    {
        // 100 − 10 + 5 = 95 → ajuste líquido −5; swap keep → 95.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 100, 40, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 95, discount: 10, surcharge: 5);
        Assert.Equal(95, sale.Total);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(95, GetTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_TotalNaoPodeSerNegativo_ClampZero()
    {
        // oldGross 100, total 10 (ajuste −90). newGross 50 → −40 → clamp 0.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 50, 20, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 10, discount: 90, surcharge: 0);
        Assert.Equal(10, sale.Total);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 0 }]);

        Assert.Equal(0, GetTotal(sale.SaleId));
        var cash = GetCash(sale.SaleId);
        Assert.DoesNotContain(cash, c => c.AmountIn < 0);
    }

    [Fact]
    public void Swap_TabelaDePreco_PreservaAjusteLiquidoDoSurcharge()
    {
        // PriceTablesService gera surcharge no pagamento; unit_price permanece o do carrinho.
        // keepLinePrice=true preserva preço da linha E o ajuste líquido (+5).
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 50, 20, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 99, 30, "B", "B");
        var sale = Finalize(a, 2, 50, cash: 105, discount: 0, surcharge: 5); // simula tabela
        Assert.Equal(105, sale.Total);
        Assert.Equal(50, GetItemUnitPrice(GetItemId(sale.SaleId)));
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(50, GetItemUnitPrice(itemId)); // preço da linha preservado
        Assert.Equal(105, GetTotal(sale.SaleId)); // ajuste líquido +5 preservado
        Assert.Equal(b, GetItemProductId(itemId));
    }

    [Fact]
    public void Swap_KeepLinePriceFalse_UsaSalePriceEPreservaAjusteGlobal()
    {
        // keepLinePrice=false → preço novo; ajuste +5 aplicado sobre novo bruto.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 50, 20, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 40, 15, "B", "B");
        var sale = Finalize(a, 1, 50, cash: 55, discount: 0, surcharge: 5);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 45 }],
            cashReceived: 45);

        Assert.Equal(40, GetItemUnitPrice(itemId));
        Assert.Equal(45, GetTotal(sale.SaleId)); // 40 + 5
    }

    [Fact]
    public void Swap_KeepLinePriceTrue_PreservaPrecoLinhaEAjusteGlobal()
    {
        // keepLinePrice=true: unit_price antigo + ajuste líquido −10.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 200, 80, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 90, discount: 10, surcharge: 0);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(100, GetItemUnitPrice(itemId));
        Assert.Equal(90, GetTotal(sale.SaleId));
    }

    [Fact]
    public void Swap_DescontoPreservado_Dinheiro_NaoAdicionaPix()
    {
        // Total 90 permanece 90 → sem complemento artificial de Pix.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 100, 40, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 90, discount: 10, surcharge: 0, payment: "Dinheiro");
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(90, GetTotal(sale.SaleId));
        var cash = GetCash(sale.SaleId);
        Assert.Single(cash);
        Assert.Contains(cash, c => c.PaymentType == "Dinheiro" && c.AmountIn == 90);
        Assert.DoesNotContain(cash, c => c.PaymentType == "Pix");
    }

    [Fact]
    public void Swap_DescontoPreservado_Pix_NaoAdicionaDinheiro()
    {
        // Pix 90 permanece coerente com total 90.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 100, 40, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 0, discount: 10, surcharge: 0, payment: "Pix");
        Assert.Equal(90, sale.Total);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(90, GetTotal(sale.SaleId));
        var cash = GetCash(sale.SaleId);
        Assert.Single(cash);
        Assert.Contains(cash, c => c.PaymentType == "Pix" && c.AmountIn == 90);
        Assert.DoesNotContain(cash, c => c.PaymentType == "Dinheiro");
    }

    [Fact]
    public void Swap_DescontoPreservado_Fiado_MantemSaldoECliente()
    {
        // Total 90, Fiado 90; sem Pix complementar; customer_id permanece.
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var customerId = SeedCustomer("Cliente Desc");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 100, 40, "B", "B");
        var sale = Finalize(a, 1, 100, cash: 0, discount: 10, surcharge: 0,
            payment: "Fiado", customerId: customerId);
        Assert.Equal(90, FiadoService.GetDetail(customerId).Balance);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(90, GetTotal(sale.SaleId));
        Assert.Equal(90, FiadoService.GetDetail(customerId).Balance);
        Assert.Equal(customerId, GetCustomerId(sale.SaleId));
        var cash = GetCash(sale.SaleId);
        Assert.Single(cash);
        Assert.Contains(cash, c => c.PaymentType == "Fiado" && c.AmountIn == 90);
        Assert.DoesNotContain(cash, c => c.PaymentType == "Pix");
    }

    [Fact]
    public void Swap_DescontoPreservado_Misto_MantemPartes()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 100, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 100, 40, "B", "B");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = a,
                    Quantity = 1,
                    UnitPrice = 100,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            Payments =
            [
                new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 40 },
                new PdvPaymentPart { PaymentType = "Pix", Amount = 50 },
            ],
            CashReceived = 40,
            Discount = 10,
        });
        Assert.Equal(90, sale.Total);
        var itemId = GetItemId(sale.SaleId);

        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true);

        Assert.Equal(90, GetTotal(sale.SaleId));
        var cash = GetCash(sale.SaleId);
        Assert.Contains(cash, c => c.PaymentType == "Dinheiro" && c.AmountIn == 40);
        Assert.Contains(cash, c => c.PaymentType == "Pix" && c.AmountIn == 50);
    }

    // ——— helpers ———

    private static PdvFinalizeResult Finalize(
        int productId, double qty, double unitPrice, double cash,
        double discount, double surcharge, string payment = "Dinheiro", int? customerId = null)
    {
        var total = ProductPriceHelper.RoundPrice(qty * unitPrice - discount + surcharge);
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
            Surcharge = surcharge,
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

    private static bool SalesHasDiscountColumn()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(sales);";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), "discount", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(r.GetString(1), "surcharge", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int GetItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double GetTotal(int saleId) => ScalarD("SELECT total FROM sales WHERE id = $id;", saleId);
    private static int GetItemProductId(int itemId) => (int)ScalarD("SELECT product_id FROM sale_items WHERE id = $id;", itemId);
    private static double GetItemUnitPrice(int itemId) => ScalarD("SELECT unit_price FROM sale_items WHERE id = $id;", itemId);
    private static double GetItemSubtotal(int itemId) => ScalarD("SELECT subtotal FROM sale_items WHERE id = $id;", itemId);

    private static double SumItemSubtotals(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(SUM(subtotal),0) FROM sale_items WHERE sale_id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToDouble(cmd.ExecuteScalar());
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

    private static int? GetCustomerId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_id FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToInt32(v);
    }

    private static List<(string PaymentType, double AmountIn)> GetCash(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(payment_type,''), IFNULL(amount_in,0)
            FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<(string, double)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetDouble(1)));
        return list;
    }

    private static double ScalarD(string sql, int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }
}
